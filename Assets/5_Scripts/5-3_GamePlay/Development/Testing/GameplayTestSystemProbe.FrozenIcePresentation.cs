#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 冰面区块表现采样

    // 使用正式地形快照和区块绑定器，Lua 比较实际提交的网格数据。
    private static JObject ServiceFrozenIceSurface(JObject args)
    {
        const int iceId = 9;
        int[] beds = { 3, 4, 1, 9, 999999, 4, 9 };
        int[] biomes = { (int)SurfaceBiomeKind.Ocean, (int)SurfaceBiomeKind.River,
            (int)SurfaceBiomeKind.Grassland, 0, 0, 0, 0 };
        using var world = new WorldRuntime("probe:frozen-ice", 1);
        var profile = new ChunkGenerationProfileSnapshot("probe:frozen-ice", 1, beds.Length, 1);
        ChunkRuntime Commit(Int2 origin, bool frozen)
        {
            var address = new FlatWorld.WorldModel.WorldAddress("probe:frozen-ice", origin);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(profile.Width, profile.Height);
            for (int x = 0; x < beds.Length; x++)
            {
                var bed = new TerrainCell(beds[x], 0, 0, biomes[x], 3, TerrainCellFlags.Walkable);
                int ground = frozen ? (x == 5 ? 3 : iceId) : (x == 0 ? 3 : 0);
                buffer.SetCell(x, 0, new TerrainCell(ground, 0, 0, biomes[x], 4, TerrainCellFlags.Walkable));
                if (!frozen || x == 3) continue;
                string liquidId = x == 0 ? LiquidTypeCatalog.SeaWaterId : LiquidTypeCatalog.DirtyWaterId;
                FrozenSurfaceWaterLayer.WriteGenerationSource(buffer, x,
                    new FrozenSurfaceWaterSource(bed, iceId, buffer.LiquidTypes.GetIndex(liquidId), 80 - x));
            }
            using var result = new ChunkGenerationResult(request, buffer);
            if (!world.TryCommit(result, out string reason)) throw new InvalidOperationException(reason);
            world.TryGetChunk(address, out ChunkRuntime chunk);
            return chunk;
        }

        Vector2Int origin = WorldTopologyRuntime.NormalizeCell(new Vector2Int(1000000, 1000000));
        ChunkRuntime iceChunk = Commit(new Int2(origin.x, origin.y), true);
        Vector2Int otherOrigin = WorldTopologyRuntime.NormalizeCell(origin + new Vector2Int(profile.Width, 0));
        ChunkRuntime otherChunk = Commit(new Int2(otherOrigin.x, otherOrigin.y), false);
        var palette = Resources.Load<ChunkTilePaletteSO>("Config/WorldModel/ChunkTilePalette_Default");
        Shader shader = Shader.Find("FlatWorld/2D/Chunk Mesh Contact Lit");
        if (palette == null || shader == null) throw new InvalidOperationException("冰面规则缺少正式地块目录或地形 Shader。");
        Material material = null;
        GameObject root = null;
        ChunkTilemapRenderer renderer = null;
        IEnumerator binding = null;
        try
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            root = new GameObject("GameplayTest Frozen Ice Surface") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            root.transform.position = new Vector3(origin.x, origin.y, 0f);
            renderer = root.AddComponent<ChunkTilemapRenderer>();
            renderer.ConfigureFrozenIcePresentationTests(palette, material);
            renderer.SetWorld(world);
            renderer.Bind(iceChunk);
            var initial = new JArray();
            for (int x = 0; x < beds.Length; x++)
            {
                int expectedGround = x == 3 || x == 4 ? iceId : x == 5 ? 3 : beds[x];
                initial.Add(renderer.CaptureFrozenIcePresentationTestCell(x, expectedGround, iceId));
            }
            ChunkTerrainData terrain = iceChunk.Terrain;
            terrain.SetEnvironmentBatch(FrozenSurfaceWaterLayer.MeltProgressLayer, new[]
            {
                new EnvironmentCellUpdate(0, .2f), new EnvironmentCellUpdate(1, .4f),
                new EnvironmentCellUpdate(2, .6f)
            });
            JObject partial = renderer.CaptureFrozenIcePresentationTestCell(0, beds[0], iceId);

            terrain.SetEnvironmentBatch(FrozenSurfaceWaterLayer.MeltProgressLayer, new[]
            {
                new EnvironmentCellUpdate(0, 1f), new EnvironmentCellUpdate(1, 1f)
            });
            JObject meltedProgress = renderer.CaptureFrozenIcePresentationTestCell(0, beds[0], iceId);
            terrain.SetCell(0, 0, new TerrainCell(beds[0], 0, 0, biomes[0], 3, TerrainCellFlags.Walkable));
            terrain.SetCell(1, 0, new TerrainCell(beds[1], 0, 0, biomes[1], 3, TerrainCellFlags.Walkable));
            JObject meltedBed = renderer.CaptureFrozenIcePresentationTestCell(0, beds[0], iceId);
            terrain.SetCell(0, 0, new TerrainCell(iceId, 0, 0, biomes[0], 4, TerrainCellFlags.Walkable));
            JObject replacedIce = renderer.CaptureFrozenIcePresentationTestCell(0, iceId, iceId);
            terrain.SetCell(0, 0, new TerrainCell(beds[0], 0, 0, biomes[0], 3, TerrainCellFlags.Walkable));
            terrain.SetEnvironmentBatch(FrozenSurfaceWaterLayer.MeltProgressLayer, new[]
            {
                new EnvironmentCellUpdate(0, .3f), new EnvironmentCellUpdate(1, .4f)
            });
            JObject rollbackBeforeCell = renderer.CaptureFrozenIcePresentationTestCell(0, beds[0], iceId);
            terrain.SetCell(0, 0, new TerrainCell(iceId, 0, 0, biomes[0], 4, TerrainCellFlags.Walkable));
            terrain.SetCell(1, 0, new TerrainCell(iceId, 0, 0, biomes[1], 4, TerrainCellFlags.Walkable));
            JObject rollback = renderer.CaptureFrozenIcePresentationTestCell(0, beds[0], iceId);

            renderer.Unbind();
            bool unbound = renderer.BaseTerrainVisualCount == 0 && !renderer.IsBatchPresentationRegistered;
            renderer.Bind(otherChunk);
            JObject other = renderer.CaptureFrozenIcePresentationTestCell(0, 3, iceId);
            renderer.Bind(iceChunk);
            JObject rebound = renderer.CaptureFrozenIcePresentationTestCell(0, beds[0], iceId);
            long beforeUnknownLayer = renderer.IncrementalFullTerrainCellWrites;
            renderer.NotifyUnknownEnvironmentForFrozenIceTests();
            bool unknownLayerRefreshed = renderer.IncrementalFullTerrainCellWrites > beforeUnknownLayer;

            renderer.Unbind();
            binding = renderer.BindIncremental(iceChunk);
            binding.MoveNext();
            binding.MoveNext();
            binding.MoveNext();
            bool bindingWasIncomplete = !renderer.IsBatchPresentationComplete;
            terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, 2, 0, 1f);
            int steps = 0;
            while (binding.MoveNext())
                if (++steps > 128) throw new InvalidOperationException("冰面分步重绑没有完成。");
            JObject changedDuringBinding = renderer.CaptureFrozenIcePresentationTestCell(2, beds[2], iceId);
            terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, 2, 0, .6f);
            JObject restoredAfterBinding = renderer.CaptureFrozenIcePresentationTestCell(2, beds[2], iceId);

            (binding as IDisposable)?.Dispose();
            binding = null;
            renderer.Unbind();
            terrain.SetEnvironmentBatch(FrozenSurfaceWaterLayer.MeltProgressLayer, new[]
            {
                new EnvironmentCellUpdate(0, 1f), new EnvironmentCellUpdate(1, 1f),
                new EnvironmentCellUpdate(2, 1f), new EnvironmentCellUpdate(6, 1f)
            });
            binding = renderer.BindIncremental(iceChunk);
            for (int i = 0; i < 4; i++)
                if (!binding.MoveNext()) throw new InvalidOperationException("冰面分步绑定提前结束。");
            bool lazyInitiallyEmpty = !(bool)renderer.CaptureFrozenIcePresentationTestCell(0, iceId, iceId)["ice"];
            terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, 0, 0, .3f);
            steps = 0;
            while (binding.MoveNext())
                if (++steps > 128) throw new InvalidOperationException("回滚后冰面分步绑定没有完成。");
            JObject lazyRebound = renderer.CaptureFrozenIcePresentationTestCell(0, beds[0], iceId);
            bool lazyMeshUploaded = false;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.gameObject.name.StartsWith("IceMesh_", StringComparison.Ordinal) && filter.gameObject.activeSelf)
                    lazyMeshUploaded = true;

            return new JObject
            {
                ["initial"] = initial, ["partial"] = partial,
                ["meltedProgress"] = meltedProgress, ["meltedBed"] = meltedBed, ["replacedIce"] = replacedIce,
                ["rollbackBeforeCell"] = rollbackBeforeCell, ["rollback"] = rollback,
                ["unbound"] = unbound, ["other"] = other, ["rebound"] = rebound,
                ["unknownLayerRefreshed"] = unknownLayerRefreshed,
                ["bindingWasIncomplete"] = bindingWasIncomplete, ["bindingCompleted"] = renderer.IsBatchPresentationComplete,
                ["changedDuringBinding"] = changedDuringBinding, ["restoredAfterBinding"] = restoredAfterBinding,
                ["lazyInitiallyEmpty"] = lazyInitiallyEmpty, ["lazyRebound"] = lazyRebound,
                ["lazyMeshUploaded"] = lazyMeshUploaded,
                ["geometry"] = CaptureFrozenIceMeshGeometryBounds(palette, material),
                ["waterQueue"] = ChunkBatchRendererGroupService.GetLayerRenderQueue(ChunkBatchRendererGroupService.VisualLayer.Water)
            };
        }
        finally
        {
            (binding as IDisposable)?.Dispose();
            if (renderer != null) { renderer.Unbind(); renderer.SetWorld(null); }
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    // 直接增删真实网格格子，采样收缩、清空、重用和纹理切换后的包围盒。
    private static JObject CaptureFrozenIceMeshGeometryBounds(ChunkTilePaletteSO palette, Material material)
    {
        if (!SystemInfo.supportsComputeShaders) return new JObject { ["available"] = false };
        if (!palette.TryGetVisual(9, out Sprite ice, out _, out Matrix4x4 iceTransform) ||
            !palette.TryGetVisual(4, out Sprite stone, out _, out Matrix4x4 stoneTransform) ||
            ice == null || stone == null || ice.texture == stone.texture)
            throw new InvalidOperationException("冰面网格边界测试缺少两种正式贴图。");
        var root = new GameObject("GameplayTest Ice Mesh Bounds") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            using var mesh = new ChunkGroundMeshRenderer(root.transform, 6, 4, material,
                ChunkBatchRendererGroupService.VisualLayer.Ice);
            void Put(int x, int y, Sprite sprite, Matrix4x4 transform)
            {
                if (!mesh.TrySetCell(x, y, sprite, Color.white, transform, Vector4.zero, 0f, Vector4.zero))
                    throw new InvalidOperationException("冰面网格边界测试无法写入格子。");
            }
            MeshFilter Find(Texture texture)
            {
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.GetComponent<MeshRenderer>().sharedMaterial.mainTexture == texture) return filter;
                return null;
            }
            bool Matches(MeshFilter filter, float left, float bottom, float right, float top)
            {
                if (filter == null || !filter.gameObject.activeSelf) return false;
                Bounds bounds = filter.sharedMesh.bounds;
                return Mathf.Abs(bounds.min.x - left) < .001f && Mathf.Abs(bounds.min.y - bottom) < .001f &&
                       Mathf.Abs(bounds.max.x - right) < .001f && Mathf.Abs(bounds.max.y - top) < .001f;
            }

            mesh.BeginBulkUpdate();
            Put(1, 1, ice, iceTransform); Put(4, 1, ice, iceTransform);
            Put(2, 3, ice, iceTransform); Put(2, 2, ice, iceTransform);
            mesh.EndBulkUpdate();
            MeshFilter iceFilter = Find(ice.texture);
            if (iceFilter == null || iceFilter.sharedMesh.vertexCount != 4)
                throw new InvalidOperationException("支持 Compute 时冰面未使用单 Quad 网格。");
            bool initial = Matches(iceFilter, 1, 1, 5, 4);
            mesh.ClearCell(2, 2);
            bool interior = Matches(iceFilter, 1, 1, 5, 4);
            mesh.ClearCell(4, 1);
            bool shrinkX = Matches(iceFilter, 1, 1, 3, 4);
            mesh.ClearCell(2, 3);
            bool shrinkY = Matches(iceFilter, 1, 1, 2, 2);
            mesh.ClearCell(1, 1);
            bool empty = !iceFilter.gameObject.activeSelf && mesh.ActiveCellCount == 0;
            Put(5, 3, ice, iceTransform);
            bool reused = Matches(iceFilter, 5, 3, 6, 4);
            Put(5, 3, stone, stoneTransform);
            MeshFilter stoneFilter = Find(stone.texture);
            bool switched = !iceFilter.gameObject.activeSelf && stoneFilter != null && mesh.ActiveCellCount == 1;
            Put(5, 3, ice, iceTransform);
            switched = switched && Matches(iceFilter, 5, 3, 6, 4) && mesh.ActiveCellCount == 1 &&
                       (stoneFilter.sharedMesh.vertexCount != 4 || !stoneFilter.gameObject.activeSelf);
            mesh.BeginBulkUpdate();
            Put(0, 0, ice, iceTransform); Put(4, 2, ice, iceTransform); mesh.ClearCell(5, 3);
            mesh.EndBulkUpdate();
            return new JObject
            {
                ["available"] = true, ["initial"] = initial, ["interior"] = interior,
                ["shrinkX"] = shrinkX, ["shrinkY"] = shrinkY, ["empty"] = empty,
                ["reused"] = reused, ["switched"] = switched, ["bulk"] = Matches(iceFilter, 0, 0, 5, 3)
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    #endregion
}

public sealed partial class ChunkTilemapRenderer
{
    #region 冰面测试采样
    internal void ConfigureFrozenIcePresentationTests(ChunkTilePaletteSO testPalette, Material testMaterial)
    {
        palette = testPalette;
        groundMaterial = testMaterial;
    }

    internal void NotifyUnknownEnvironmentForFrozenIceTests() =>
        HandleTerrainChanged(new ChunkTerrainChanged(new Int2(0, 0), TerrainChangeKind.Environment, boundChunk.Terrain.Revision));

    internal JObject CaptureFrozenIcePresentationTestCell(int x, int expectedGroundId, int expectedIceId)
    {
        Texture groundTexture = null, iceTexture = null;
        Color groundTint = default, iceTint = default;
        Vector2 groundUv = default, iceUv = default;
        int groundQueue = 0, iceQueue = 0;
        bool ground = groundMesh != null && groundMesh.TryCaptureCellForTests(x, 0,
            out groundTexture, out groundTint, out groundUv, out groundQueue);
        bool ice = iceMesh != null && iceMesh.TryCaptureCellForTests(x, 0,
            out iceTexture, out iceTint, out iceUv, out iceQueue);
        bool Matches(Texture texture, Vector2 uv, int id)
        {
            if (!palette.TryGetVisual(id, out Sprite sprite, out _, out _) || sprite.texture != texture) return false;
            foreach (Vector2 expected in SharedSpriteMeshCache.GetGeometry(sprite).Uv)
                if ((expected - uv).sqrMagnitude < .0000000001f) return true;
            return false;
        }
        palette.TryGetVisual(expectedIceId, out _, out Color expectedIceTint, out _);
        FrozenSurfaceWaterLayer.TryReadSource(boundChunk.Terrain, x, 0, out FrozenSurfaceWaterSource source);
        return new JObject
        {
            ["ground"] = ground, ["groundMatches"] = ground && Matches(groundTexture, groundUv, expectedGroundId),
            ["groundAlpha"] = groundTint.a, ["groundQueue"] = groundQueue,
            ["ice"] = ice, ["iceMatches"] = ice && Matches(iceTexture, iceUv, expectedIceId),
            ["iceAlpha"] = iceTint.a, ["expectedIceAlpha"] = expectedIceTint.a * .5f, ["iceQueue"] = iceQueue,
            ["groundCells"] = groundMesh?.ActiveCellCount ?? 0, ["iceCells"] = iceMesh?.ActiveCellCount ?? 0,
            ["iceGroups"] = iceMesh?.GroupCountForTests ?? 0, ["waterCells"] = waterMesh?.ActiveCellCount ?? 0,
            ["sourceBed"] = source.Bed.GroundTileId, ["sourceLiquid"] = boundChunk.Terrain.LiquidTypes.GetId(source.LiquidTypeIndex),
            ["sourceLevel"] = source.LiquidLevel
        };
    }
    #endregion
}
#endif
