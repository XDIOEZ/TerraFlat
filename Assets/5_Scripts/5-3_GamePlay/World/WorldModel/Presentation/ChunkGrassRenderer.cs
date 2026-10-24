using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// 按权威草格提交全局 BatchRendererGroup 实例；每格只保存确定性变体、局部矩阵与色调。
/// 32×32 区块按 128 格分步绑定，运行时 Sprite 变体按切片配置跨区块共享，避免重复对象和逐 Chunk Tilemap。
/// </summary>
public sealed class ChunkGrassRenderer : MonoBehaviour, IIncrementalChunkViewRenderer, IRecoverableChunkViewRenderer
{
    #region 配置与缓存

    private static readonly ProfilerMarker RenderRangeMarker =
        new("FlatWorld.ChunkStreaming.RenderGrass");
    private const int CommonVariantCount = 24;
    private const int CellsPerStep = 128;

    [SerializeField] private Sprite[] spriteVariants = Array.Empty<Sprite>(); // 前 24 项为小草，后续切片只作为少量点缀。
    [SerializeField, Min(1f)] private float pixelsPerUnit = 32f;
    [SerializeField, Range(0f, 0.45f)] private float positionJitter = 0.22f;
    [SerializeField] private Vector2 scaleRange = new(0.85f, 1.15f);
    [SerializeField, Range(0f, 1f)] private float accentVariantChance = 0.2f;
    [SerializeField] private Material grassMaterial;

    private readonly HashSet<int> visibleCells = new(); // 快速判断区块草格是否已有实例。
    private ChunkRuntime boundChunk; // 当前绑定的权威区块。
    private ChunkTerrainData boundTerrain; // 订阅与当前地形身份一起保存，替换数据时解除旧订阅。
    private ChunkTilemapRenderer ownerRenderer; // 共享区块边界裁剪与 BRG 生命周期。
    private Sprite[] sharedSprites; // 同配置区块共用同一组 Sprite 身份。

    public bool IsConfigured => spriteVariants != null && spriteVariants.Length > 0 && grassMaterial != null;
    public Material GrassMaterial => grassMaterial;

    #endregion

    #region 绑定与解绑

    /// <summary>同步入口复用相同范围提交逻辑，供非分帧绑定调用。</summary>
    public void Bind(ChunkRuntime chunk)
    {
        if (!PrepareBinding(chunk))
            return;
        try
        {
            RenderRange(chunk.Terrain, 0, chunk.Terrain.CellCount);
        }
        catch
        {
            Unbind();
            throw;
        }
    }

    /// <summary>每次最多提交 128 格，避免视距加载时单区块草层独占主线程。</summary>
    public IEnumerator BindIncremental(ChunkRuntime chunk)
    {
        if (!PrepareBinding(chunk))
            yield break;

        bool completed = false;
        try
        {
            int cellCount = chunk.Terrain.CellCount;
            for (int start = 0; start < cellCount; start += CellsPerStep)
            {
                RenderRange(chunk.Terrain, start, Mathf.Min(CellsPerStep, cellCount - start));
                if (start + CellsPerStep < cellCount)
                    yield return null;
            }
            completed = true;
        }
        finally
        {
            if (!completed)
                Unbind();
        }
    }

    /// <summary>确认基础 Owner 已就绪后订阅地形脏格并取得共享草切片变体。</summary>
    private bool PrepareBinding(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new InvalidOperationException("草地表现绑定前必须完成区块数据。");
        if (ReferenceEquals(boundChunk, chunk))
            return false;

        Unbind();
        if (!IsConfigured)
            throw new InvalidOperationException("ChunkGrassRenderer 缺少草地切片或 BRG 材质。");
        ownerRenderer = GetComponent<ChunkTilemapRenderer>();
        if (ownerRenderer == null || !ownerRenderer.IsBatchPresentationComplete)
            throw new InvalidOperationException("草地表现必须在基础地形 BRG Owner 注册后绑定。");

        sharedSprites = GrassSpriteVariantCache.Get(spriteVariants, pixelsPerUnit);
        if (sharedSprites.Length == 0)
            throw new InvalidOperationException("草地贴图未包含可用的变体 Sprite。");

        boundChunk = chunk;
        boundTerrain = chunk.Terrain;
        boundTerrain.Changed += HandleTerrainChanged;
        ownerRenderer.BatchPresentationRebuilt += HandleOwnerPresentationRebuilt;
        return true;
    }

    /// <summary>回池时移除本图层实例，不销毁跨区块共享的 Sprite 变体。</summary>
    public void Unbind()
    {
        if (boundTerrain != null)
            boundTerrain.Changed -= HandleTerrainChanged;
        if (ownerRenderer != null)
            ownerRenderer.BatchPresentationRebuilt -= HandleOwnerPresentationRebuilt;
        if (ownerRenderer != null)
            ChunkBatchRendererGroupService.ClearOwnerLayer(ownerRenderer,
                ChunkBatchRendererGroupService.VisualLayer.Grass);

        visibleCells.Clear();
        sharedSprites = null;
        ownerRenderer = null;
        boundChunk = null;
        boundTerrain = null;
    }

    /// <summary>区块销毁时解除 Terrain 与 Owner 事件订阅。</summary>
    private void OnDestroy() => Unbind();

    #endregion

    #region 增量提交

    /// <summary>仅草层数据变化时更新对应 BRG 单格。</summary>
    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (changed.Kind == TerrainChangeKind.Grass && boundChunk?.Terrain != null)
            RenderCell(boundChunk.Terrain, changed.LocalCell.X, changed.LocalCell.Y);
    }

    /// <summary>基础 BRG Owner 修复后从权威草格重建，不依赖可能已丢失的提交缓存。</summary>
    private void HandleOwnerPresentationRebuilt()
    {
        if (boundChunk?.Terrain == null)
            return;
        RebuildGrassPresentation();
    }

    /// <summary>流送窗口检查真实实例与权威草格，空缓存或局部丢失也能恢复。</summary>
    public bool RepairPresentationIfNeeded(ChunkRuntime chunk)
    {
        if (chunk?.Terrain == null || chunk.Terrain.IsDisposed)
            return false;
        if (!ReferenceEquals(boundChunk, chunk) || !ReferenceEquals(boundTerrain, chunk.Terrain))
        {
            Unbind();
            Bind(chunk);
        }
        if (ownerRenderer == null || !ownerRenderer.IsBatchPresentationComplete)
            return false;

        int expected = 0;
        bool matches = sharedSprites != null && sharedSprites.Length > 0 && sharedSprites[0] != null;
        for (int index = 0; index < chunk.Terrain.CellCount; index++)
        {
            bool present = chunk.Terrain.GetGrass(index % chunk.Terrain.Width,
                index / chunk.Terrain.Width) == ChunkTerrainData.GrassPresent;
            if (present) expected++;
            matches &= present == visibleCells.Contains(index);
        }
        int submitted = ChunkBatchRendererGroupService.GetOwnerLayerVisualCount(ownerRenderer,
            ChunkBatchRendererGroupService.VisualLayer.Grass);
        if (matches && submitted == expected)
            return true;

        if (Application.isEditor || Debug.isDebugBuild)
            Debug.LogWarning($"[ChunkRenderDebug] RepairGrass chunk={chunk.Address} " +
                             $"expected={expected} cached={visibleCells.Count} submitted={submitted}。");
        RebuildGrassPresentation();
        return ChunkBatchRendererGroupService.GetOwnerLayerVisualCount(ownerRenderer,
            ChunkBatchRendererGroupService.VisualLayer.Grass) == expected;
    }

    private void RebuildGrassPresentation()
    {
        ChunkBatchRendererGroupService.ClearOwnerLayer(ownerRenderer,
            ChunkBatchRendererGroupService.VisualLayer.Grass);
        visibleCells.Clear();
        sharedSprites = GrassSpriteVariantCache.Get(spriteVariants, pixelsPerUnit);
        RenderRange(boundChunk.Terrain, 0, boundChunk.Terrain.CellCount);
    }

    /// <summary>逐段读取权威草层，空格无需进入 BRG 后端。</summary>
    private void RenderRange(ChunkTerrainData terrain, int startIndex, int count)
    {
        using (RenderRangeMarker.Auto())
        {
            ChunkBatchRendererGroupService.BeginBulkSubmit();
            try
            {
                for (int index = startIndex; index < startIndex + count; index++)
                {
                    int x = index % terrain.Width;
                    int y = index / terrain.Width;
                    if (terrain.GetGrass(x, y) == ChunkTerrainData.GrassPresent)
                        RenderPresentCell(x, y);
                }
            }
            finally { ChunkBatchRendererGroupService.EndBulkSubmit(); }
        }
    }

    /// <summary>更新单格草的确定性变体、摆动矩阵和轻微色调。</summary>
    private void RenderCell(ChunkTerrainData terrain, int x, int y)
    {
        if (terrain.GetGrass(x, y) == ChunkTerrainData.GrassPresent)
            RenderPresentCell(x, y);
        else
            ClearCell(x, y);
    }

    /// <summary>按规范世界格坐标生成稳定外观，并提交到 Grass BRG 图层。</summary>
    private void RenderPresentCell(int x, int y)
    {
        int worldX = boundChunk.Address.ChunkOrigin.X + x;
        int worldY = boundChunk.Address.ChunkOrigin.Y + y;
        uint state = MixSeed(worldX, worldY);
        Sprite sprite = sharedSprites[SelectVariant(ref state)];

        float offsetX = Mathf.Lerp(-positionJitter, positionJitter, Next01(ref state));
        float offsetY = Mathf.Lerp(-positionJitter, positionJitter, Next01(ref state));
        float scale = Mathf.Lerp(
            Mathf.Min(scaleRange.x, scaleRange.y),
            Mathf.Max(scaleRange.x, scaleRange.y),
            Next01(ref state));
        float flipX = Next01(ref state) < 0.5f ? -1f : 1f;
        float tint = Mathf.Lerp(0.9f, 1f, Next01(ref state));
        Matrix4x4 localToWorld = Matrix4x4.Translate(new Vector3(worldX + 0.5f, worldY + 0.5f, 0f)) *
                                 Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                                     new Vector3(scale * flipX, scale, 1f));

        if (!ownerRenderer.SetLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Grass, x, y,
                sprite, grassMaterial, localToWorld, new Color(tint, tint, tint, 1f)))
            throw new InvalidOperationException($"草实例提交失败：{boundChunk.Address} local={x},{y}。");
        visibleCells.Add(y * boundChunk.Terrain.Width + x);
    }

    /// <summary>草格消失时只清理已登记的单格实例。</summary>
    private void ClearCell(int x, int y)
    {
        int index = y * boundChunk.Terrain.Width + x;
        if (!visibleCells.Remove(index))
            return;
        ownerRenderer.ClearLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Grass, x, y);
    }

    #endregion

    #region 确定性外观

    /// <summary>按权重选择基础草变体或少量强调变体。</summary>
    private int SelectVariant(ref uint state)
    {
        int count = sharedSprites.Length;
        int commonCount = Mathf.Min(CommonVariantCount, count);
        if (count > commonCount && Next01(ref state) < accentVariantChance)
            return commonCount + (int)(Next(ref state) % (uint)(count - commonCount));
        return (int)(Next(ref state) % (uint)commonCount);
    }

    private static float Next01(ref uint state) =>
        (Next(ref state) & 0xFFFFFFu) / (float)0x1000000;

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static uint MixSeed(int x, int y)
    {
        unchecked
        {
            uint state = 2166136261u;
            state = (state ^ (uint)x) * 16777619u;
            state = (state ^ (uint)y) * 16777619u;
            return state == 0u ? 0x9E3779B9u : state;
        }
    }

    #endregion

    #region 跨区块 Sprite 共享

    /// <summary>不同源图的切片统一大小与中心锚点，并跨区块共用同一组 Sprite。</summary>
    private static class GrassSpriteVariantCache
    {
        private static readonly Dictionary<SpriteSetKey, Sprite[]> spriteSets = new();

        static GrassSpriteVariantCache()
        {
            SharedSpriteMeshCache.Clearing += Clear;
        }

        /// <summary>首次使用时从已选切片创建共享 Sprite，不受源图 PPU 与 Pivot 差异影响。</summary>
        public static Sprite[] Get(Sprite[] sources, float ppu)
        {
            var key = new SpriteSetKey(sources, ppu);
            if (spriteSets.TryGetValue(key, out Sprite[] existing))
                return existing;

            var sprites = new Sprite[sources.Length];
            try
            {
                for (int index = 0; index < sources.Length; index++)
                {
                    Sprite source = sources[index];
                    if (source == null)
                        throw new InvalidOperationException($"草地第 {index + 1} 个变体未绑定切片。");
                    Sprite sprite = Sprite.Create(
                        source.texture,
                        source.rect,
                        new Vector2(0.5f, 0.5f),
                        ppu,
                        0,
                        SpriteMeshType.Tight);
                    sprite.name = "ChunkGrassDetail_" + index;
                    sprite.hideFlags = HideFlags.HideAndDontSave;
                    sprites[index] = sprite;
                }
            }
            catch
            {
                DestroySprites(sprites);
                throw;
            }

            // 缓存键保留切片快照，避免 Inspector 改动数组后破坏已有字典键。
            spriteSets.Add(new SpriteSetKey((Sprite[])sources.Clone(), ppu), sprites);
            return sprites;
        }

        /// <summary>共享网格缓存通知后，BRG 已释放引用，可销毁本缓存自有的运行时 Sprite。</summary>
        private static void Clear()
        {
            foreach (Sprite[] sprites in spriteSets.Values)
                DestroySprites(sprites);
            spriteSets.Clear();
        }

        private static void DestroySprites(Sprite[] sprites)
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite == null)
                    continue;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(sprite);
                else
                    UnityEngine.Object.DestroyImmediate(sprite);
            }
        }
    }

    /// <summary>按切片顺序与目标 PPU 共享变体，允许组合多张源图。</summary>
    private readonly struct SpriteSetKey : IEquatable<SpriteSetKey>
    {
        private readonly Sprite[] sources;
        private readonly float pixelsPerUnit;
        private readonly int hashCode;

        public SpriteSetKey(Sprite[] sources, float pixelsPerUnit)
        {
            this.sources = sources;
            this.pixelsPerUnit = pixelsPerUnit;
            unchecked
            {
                int hash = pixelsPerUnit.GetHashCode();
                for (int index = 0; index < sources.Length; index++)
                    hash = (hash * 397) ^ (sources[index] != null ? sources[index].GetInstanceID() : 0);
                hashCode = hash;
            }
        }

        public bool Equals(SpriteSetKey other)
        {
            if (!pixelsPerUnit.Equals(other.pixelsPerUnit) || sources.Length != other.sources.Length)
                return false;
            for (int index = 0; index < sources.Length; index++)
                if (!ReferenceEquals(sources[index], other.sources[index]))
                    return false;
            return true;
        }

        public override bool Equals(object obj) => obj is SpriteSetKey other && Equals(other);

        public override int GetHashCode() => hashCode;
    }

    #endregion
}
