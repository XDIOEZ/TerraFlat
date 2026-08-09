using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 新版区块的静态光照遮挡表现层。
///
/// 洞穴岩壁和其它静态阻挡地块仍由 ChunkTerrainData 负责，
/// 本组件只在主线程把 BlockingTileId 合并成少量矩形 ShadowCaster2D。
/// 阴影体使用固定的单位方形网格，再通过 Transform 缩放到墙体矩形，
/// 这样区块重绑和墙体编辑时可以复用组件，避免每个墙格创建一个对象。
/// </summary>
[DisallowMultipleComponent]
public sealed class ChunkLightOccluderRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 常量与字段

    private const string OccluderName = "Occluder";
    private const int OccluderWarningThreshold = 128;

    private readonly List<LightOccluderSlot> slots = new();
    private readonly List<RectInt> mergedRectangles = new();
    private bool[] blockedCells;
    private bool[] visitedCells;
    private ChunkRuntime boundChunk;
    private int activeOccluderCount;
    private bool warningLogged;

    /// <summary>当前区块实际启用的阴影体数量，供调试和 Golden Path 断言使用。</summary>
    public int ActiveOccluderCount => activeOccluderCount;

    /// <summary>当前是否已经绑定到一个可见区块。</summary>
    public bool IsBound => boundChunk != null;

    #endregion

    #region Unity 生命周期与绑定

    private void Awake()
    {
        EnsureCompositeShadowGroup();
    }

    /// <summary>绑定区块地形，并按阻挡地块生成光照遮挡矩形。</summary>
    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new InvalidOperationException(
                "Cannot bind light occluders before terrain is ready.");
        if (ReferenceEquals(boundChunk, chunk))
            return;

        Unbind();
        boundChunk = chunk;
        boundChunk.Terrain.Changed += HandleTerrainChanged;
        Rebuild(boundChunk.Terrain);
    }

    /// <summary>解除区块绑定并隐藏所有阴影体，保留对象供下次区块重用。</summary>
    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i]?.GameObject != null)
                slots[i].GameObject.SetActive(false);
        }

        activeOccluderCount = 0;
        warningLogged = false;
        boundChunk = null;
    }

    /// <summary>墙体或其它阻挡地块变化时，仅重建当前区块的光照遮挡层。</summary>
    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (boundChunk?.Terrain == null ||
            (changed.Kind != TerrainChangeKind.TileStack &&
             changed.Kind != TerrainChangeKind.Cell))
        {
            return;
        }

        Rebuild(boundChunk.Terrain);
    }

    #endregion

    #region 阻挡数据转阴影体

    /// <summary>扫描阻挡格并刷新当前区块的阴影体对象。</summary>
    private void Rebuild(ChunkTerrainData terrain)
    {
        EnsureCompositeShadowGroup();
        BuildMergedRectangles(terrain);

        for (int i = 0; i < mergedRectangles.Count; i++)
        {
            LightOccluderSlot slot = GetOrCreateSlot(i);
            ConfigureSlot(slot, mergedRectangles[i]);
        }

        for (int i = mergedRectangles.Count; i < slots.Count; i++)
        {
            if (slots[i]?.GameObject != null)
                slots[i].GameObject.SetActive(false);
        }

        activeOccluderCount = mergedRectangles.Count;
        if (activeOccluderCount > OccluderWarningThreshold && !warningLogged)
        {
            Debug.LogWarning(
                $"[ChunkLightOccluderRenderer] 区块光照遮挡矩形较多：{activeOccluderCount}，" +
                "建议检查洞穴墙体是否需要进一步合并。",
                this);
            warningLogged = true;
        }
    }

    /// <summary>把同一块连续阻挡区域拆成较少的轴对齐矩形。</summary>
    private void BuildMergedRectangles(ChunkTerrainData terrain)
    {
        int cellCount = terrain.CellCount;
        if (blockedCells == null || blockedCells.Length != cellCount)
        {
            blockedCells = new bool[cellCount];
            visitedCells = new bool[cellCount];
        }
        else
        {
            Array.Clear(blockedCells, 0, blockedCells.Length);
            Array.Clear(visitedCells, 0, visitedCells.Length);
        }

        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < terrain.Width; x++)
            {
                TerrainCell cell = terrain.GetCell(x, y);
                blockedCells[y * terrain.Width + x] =
                    cell.BlockingTileId != 0 &&
                    (cell.Flags & TerrainCellFlags.Blocking) != 0;
            }
        }

        mergedRectangles.Clear();
        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < terrain.Width; x++)
            {
                int index = y * terrain.Width + x;
                if (!blockedCells[index] || visitedCells[index])
                    continue;

                int width = 1;
                while (x + width < terrain.Width &&
                       IsUnvisitedBlocked(terrain.Width, x + width, y))
                {
                    width++;
                }

                int height = 1;
                while (y + height < terrain.Height &&
                       CanExtendRectangle(terrain.Width, x, y + height, width))
                {
                    height++;
                }

                for (int row = y; row < y + height; row++)
                {
                    for (int column = x; column < x + width; column++)
                        visitedCells[row * terrain.Width + column] = true;
                }

                mergedRectangles.Add(new RectInt(x, y, width, height));
            }
        }
    }

    /// <summary>判断一个格子是否是尚未归入矩形的阻挡格。</summary>
    private bool IsUnvisitedBlocked(int width, int x, int y)
    {
        int index = y * width + x;
        return blockedCells[index] && !visitedCells[index];
    }

    /// <summary>判断下一行能否继续扩展当前矩形。</summary>
    private bool CanExtendRectangle(int width, int x, int y, int rectangleWidth)
    {
        for (int column = x; column < x + rectangleWidth; column++)
        {
            if (!IsUnvisitedBlocked(width, column, y))
                return false;
        }

        return true;
    }

    #endregion

    #region URP ShadowCaster2D 对象池

    /// <summary>确保遮挡子层拥有一个 URP 阴影分组。</summary>
    private void EnsureCompositeShadowGroup()
    {
        if (GetComponent<CompositeShadowCaster2D>() == null)
            gameObject.AddComponent<CompositeShadowCaster2D>();
    }

    /// <summary>按需创建固定单位方形阴影体，后续只改变位置和缩放。</summary>
    private LightOccluderSlot GetOrCreateSlot(int index)
    {
        while (slots.Count <= index)
            slots.Add(CreateSlot(slots.Count));
        return slots[index];
    }

    /// <summary>创建一个不参与游戏碰撞、只服务 URP 阴影网格的对象。</summary>
    private LightOccluderSlot CreateSlot(int index)
    {
        var shadowObject = new GameObject($"{OccluderName}_{index:000}");
        shadowObject.layer = ResolveIgnoreRaycastLayer();
        shadowObject.transform.SetParent(transform, false);

        // ShadowCaster2D 在 Awake 时从 Collider2D.bounds 生成默认方形形状，
        // 因此先准备单位碰撞体，再添加 ShadowCaster2D；随后关闭碰撞体避免影响玩法物理。
        BoxCollider2D shapeSource = shadowObject.AddComponent<BoxCollider2D>();
        shapeSource.size = Vector2.one;
        shapeSource.offset = Vector2.zero;
        ShadowCaster2D shadowCaster = shadowObject.AddComponent<ShadowCaster2D>();
        shadowCaster.castsShadows = true;
        shadowCaster.selfShadows = false;
        shapeSource.enabled = false;
        shadowObject.SetActive(false);

        return new LightOccluderSlot(shadowObject, shadowCaster);
    }

    /// <summary>把单位方形缩放到地块矩形，并立即刷新 URP 的世界变换缓存。</summary>
    private static void ConfigureSlot(LightOccluderSlot slot, RectInt rectangle)
    {
        Transform target = slot.GameObject.transform;
        target.localPosition = new Vector3(
            rectangle.x + rectangle.width * 0.5f,
            rectangle.y + rectangle.height * 0.5f,
            0f);
        target.localRotation = Quaternion.identity;
        target.localScale = new Vector3(rectangle.width, rectangle.height, 1f);
        slot.GameObject.SetActive(true);
        slot.ShadowCaster.castsShadows = true;
        slot.ShadowCaster.selfShadows = false;
        // URP 14 的 ShadowCaster2D.Update 是公开方法；绑定后主动调用，
        // 避免刚重绑的区块要等到下一帧才更新阴影缓存。
        slot.ShadowCaster.Update();
    }

    /// <summary>遮挡体不参与游戏碰撞，只让 URP 2D 阴影系统看到它。</summary>
    private int ResolveIgnoreRaycastLayer()
    {
        int layer = LayerMask.NameToLayer("Ignore Raycast");
        return layer >= 0 ? layer : gameObject.layer;
    }

    private sealed class LightOccluderSlot
    {
        public LightOccluderSlot(GameObject gameObject, ShadowCaster2D shadowCaster)
        {
            GameObject = gameObject;
            ShadowCaster = shadowCaster;
        }

        public GameObject GameObject { get; }
        public ShadowCaster2D ShadowCaster { get; }
    }

    #endregion
}
