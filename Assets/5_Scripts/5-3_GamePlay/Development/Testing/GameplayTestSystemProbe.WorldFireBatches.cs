#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FlatWorld.DroppedItems;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class GameplayTestSystemProbe
{
    #region 世界火场真实网格批次采样

    private const int FireBatchProbeCellCount = 100;
    private const int FireBatchProbeChunkSize = 16;
    private const int FireBatchProbeAnimationMode = 5;
    private const int FireBatchProbeFrameCount = 8;
    private const float FireBatchProbeFramesPerSecond = 12f;
    private const int FireBatchProbeSortingOrder = 1;

    [InitializeOnLoadMethod]
    private static void RegisterWorldFireBatchesInEditor()
        => RegisterWorldFireBatchesRule();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void RegisterWorldFireBatchesAtRuntime()
        => RegisterWorldFireBatchesRule();

    private static void RegisterWorldFireBatchesRule()
        => Register("rendering", "world_fire_batches", CaptureWorldFireBatches);

    // 用正式 ChunkView 资源和真实 Mesh 上传路径构造一万格火场，并走轻量掉落物 Present 验证遮蔽恢复。
    private static JObject CaptureWorldFireBatches(JObject args)
    {
        const string chunkViewPath = "Assets/2_Prefabs/World/WorldModel/ChunkView.prefab";
        GameObject chunkViewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(chunkViewPath);
        ChunkTilemapRenderer chunkView = chunkViewPrefab != null
            ? chunkViewPrefab.GetComponent<ChunkTilemapRenderer>() : null;
        var chunkViewFields = chunkView != null ? new SerializedObject(chunkView) : null;
        Sprite fireSprite = chunkViewFields?.FindProperty("fireSprite")?.objectReferenceValue as Sprite;
        Material fireMaterial = chunkViewFields?.FindProperty("fireMaterial")?.objectReferenceValue as Material;
        Shader depthMeshShader = Resources.Load<Shader>("Shaders/ChunkDepthSpriteLit");
        if (fireSprite == null || fireMaterial == null || fireMaterial.shader == null || depthMeshShader == null)
            return new JObject
            {
                ["assetsReady"] = false,
                ["chunkViewPath"] = chunkViewPath,
                ["fireSpriteAssigned"] = fireSprite != null,
                ["fireMaterialAssigned"] = fireMaterial != null,
                ["fireMaterialShader"] = fireMaterial != null && fireMaterial.shader != null
                    ? fireMaterial.shader.name : null,
                ["depthMeshShaderAssigned"] = depthMeshShader != null,
                ["depthMeshShaderName"] = depthMeshShader != null ? depthMeshShader.name : null
            };

        WorldRenderingManager manager = WorldRenderingManager.GetInstance();
        int sortingLayerId, sortingOrder;
        if (manager != null)
            manager.GetSortingKey(WorldSortingManager.WorldEffectCategory, out sortingLayerId, out sortingOrder);
        else
            WorldSortingManager.GetResourceSortingKey(WorldSortingManager.WorldEffectCategory, out sortingLayerId, out sortingOrder);

        int chunksPerAxis = Mathf.CeilToInt((float)FireBatchProbeCellCount / FireBatchProbeChunkSize);
        var root = new GameObject("Lua World Fire Batch Probe") { hideFlags = HideFlags.HideAndDontSave };
        var owners = new List<Transform>(chunksPerAxis * chunksPerAxis);
        var renderers = new List<ChunkDepthMeshRenderer>(chunksPerAxis * chunksPerAxis);
        try
        {
            for (int chunkY = 0; chunkY < chunksPerAxis; chunkY++)
            for (int chunkX = 0; chunkX < chunksPerAxis; chunkX++)
            {
                var owner = new GameObject($"FireChunk_{chunkX}_{chunkY}");
                owner.transform.SetParent(root.transform, false);
                owner.transform.position = new Vector3(chunkX * FireBatchProbeChunkSize,
                    chunkY * FireBatchProbeChunkSize, 0f);
                owners.Add(owner.transform);
                renderers.Add(new ChunkDepthMeshRenderer(owner.transform, sortingLayerId, sortingOrder,
                    useParentSortingAnchor: true));
            }

            long started = Stopwatch.GetTimestamp();
            for (int y = 0; y < FireBatchProbeCellCount; y++)
            for (int x = 0; x < FireBatchProbeCellCount; x++)
            {
                int chunkX = x / FireBatchProbeChunkSize;
                int chunkY = y / FireBatchProbeChunkSize;
                int chunkIndex = chunkY * chunksPerAxis + chunkX;
                SubmitFireQuad(renderers[chunkIndex], x, y, fireSprite, fireMaterial, Color.white);
            }
            double buildMilliseconds = ElapsedMilliseconds(started);

            started = Stopwatch.GetTimestamp();
            FlushDiagnosticMeshes(renderers);
            double initialFlushMilliseconds = ElapsedMilliseconds(started);
            FireMeshMetrics initial = FireMeshMetrics.Capture(renderers, fireMaterial, depthMeshShader);
            int[] meshIdsBeforeUpdate = ReadMeshInstanceIds(owners);
            Mesh firstMesh = GetMesh(owners[0]);
            int[] firstIndicesBeforeUpdate = firstMesh != null && firstMesh.subMeshCount > 0
                ? firstMesh.GetIndices(0) : Array.Empty<int>();
            Color32[] firstColorsBeforeUpdate = firstMesh != null ? firstMesh.colors32 : Array.Empty<Color32>();

            started = Stopwatch.GetTimestamp();
            SubmitFireQuad(renderers[0], 0, 0, fireSprite, fireMaterial, Color.white);
            FlushDiagnosticMeshes(renderers);
            double unchangedFlushMilliseconds = ElapsedMilliseconds(started);
            FireMeshMetrics unchanged = FireMeshMetrics.Capture(renderers, fireMaterial, depthMeshShader);
            int[] meshIdsAfterUnchanged = ReadMeshInstanceIds(owners);
            int[] firstIndicesAfterUnchanged = firstMesh != null && firstMesh.subMeshCount > 0
                ? firstMesh.GetIndices(0) : Array.Empty<int>();

            started = Stopwatch.GetTimestamp();
            SubmitFireQuad(renderers[0], 0, 0, fireSprite, fireMaterial, new Color(1f, 1f, 1f, 0.72f));
            FlushDiagnosticMeshes(renderers);
            double incrementalFlushMilliseconds = ElapsedMilliseconds(started);
            FireMeshMetrics incremental = FireMeshMetrics.Capture(renderers, fireMaterial, depthMeshShader);
            int[] meshIdsAfterTint = ReadMeshInstanceIds(owners);
            int[] firstIndicesAfterTint = firstMesh != null && firstMesh.subMeshCount > 0
                ? firstMesh.GetIndices(0) : Array.Empty<int>();
            Color32[] firstColorsAfterTint = firstMesh != null ? firstMesh.colors32 : Array.Empty<Color32>();

            int actualMeshRendererCount = root.GetComponentsInChildren<MeshRenderer>(true).Count(renderer => renderer.enabled);
            int actualSortingGroupCount = root.GetComponentsInChildren<SortingGroup>(true).Length;
            bool noUploadForUnchangedVisual = initial.IndexBufferUploadCount == unchanged.IndexBufferUploadCount &&
                initial.IndexBufferUploadBytes == unchanged.IndexBufferUploadBytes &&
                initial.VertexBufferUploadCount == unchanged.VertexBufferUploadCount &&
                initial.VertexBufferUploadBytes == unchanged.VertexBufferUploadBytes &&
                initial.TopologyRebuildCount == unchanged.TopologyRebuildCount;
            bool tintWasIncrementalVertexUpload = incremental.VertexBufferUploadCount == unchanged.VertexBufferUploadCount + 1 &&
                incremental.VertexBufferUploadBytes > unchanged.VertexBufferUploadBytes &&
                incremental.IndexBufferUploadCount == unchanged.IndexBufferUploadCount &&
                incremental.IndexBufferUploadBytes == unchanged.IndexBufferUploadBytes &&
                incremental.TopologyRebuildCount == unchanged.TopologyRebuildCount;
            bool tintChangedUploadedVertexColor = !firstColorsBeforeUpdate.SequenceEqual(firstColorsAfterTint);

            return new JObject
            {
                ["assetsReady"] = true,
                ["fireSpriteName"] = fireSprite.name,
                ["fireMaterialShader"] = fireMaterial.shader.name,
                ["depthMeshShaderName"] = depthMeshShader.name,
                ["fireAnimationMode"] = FireBatchProbeAnimationMode,
                ["fireFrameCount"] = FireBatchProbeFrameCount,
                ["fireFramesPerSecond"] = FireBatchProbeFramesPerSecond,
                ["cellCount"] = FireBatchProbeCellCount * FireBatchProbeCellCount,
                ["cellsPerChunk"] = FireBatchProbeChunkSize,
                ["expectedChunkCount"] = chunksPerAxis * chunksPerAxis,
                ["expectedSortingLayerId"] = sortingLayerId,
                ["expectedWorldEffectSortingOrder"] = sortingOrder,
                ["fireDepthSortingOrder"] = FireBatchProbeSortingOrder,
                ["buildCpuMilliseconds"] = buildMilliseconds,
                ["initialFlushCpuMilliseconds"] = initialFlushMilliseconds,
                ["unchangedFlushCpuMilliseconds"] = unchangedFlushMilliseconds,
                ["incrementalFlushCpuMilliseconds"] = incrementalFlushMilliseconds,
                ["initial"] = initial.ToJson(actualMeshRendererCount, actualSortingGroupCount),
                ["unchanged"] = unchanged.ToJson(actualMeshRendererCount, actualSortingGroupCount),
                ["incremental"] = incremental.ToJson(actualMeshRendererCount, actualSortingGroupCount),
                ["meshInstancesStableAfterUnchanged"] = meshIdsBeforeUpdate.SequenceEqual(meshIdsAfterUnchanged),
                ["meshInstancesStableAfterTint"] = meshIdsBeforeUpdate.SequenceEqual(meshIdsAfterTint),
                ["indicesStableAfterUnchanged"] = firstIndicesBeforeUpdate.SequenceEqual(firstIndicesAfterUnchanged),
                ["indicesStableAfterTint"] = firstIndicesBeforeUpdate.SequenceEqual(firstIndicesAfterTint),
                ["noUploadForUnchangedVisual"] = noUploadForUnchangedVisual,
                ["tintWasIncrementalVertexUpload"] = tintWasIncrementalVertexUpload,
                ["tintChangedUploadedVertexColor"] = tintChangedUploadedVertexColor,
                ["displayEntry"] = CaptureDroppedItemFireMask(sprite: fireSprite)
            };
        }
        finally
        {
            foreach (ChunkDepthMeshRenderer renderer in renderers) renderer.Dispose();
            Object.DestroyImmediate(root);
        }
    }

    private static void SubmitFireQuad(ChunkDepthMeshRenderer renderer, int worldX, int worldY,
        Sprite sprite, Material material, Color tint)
    {
        int localX = worldX % FireBatchProbeChunkSize;
        int localY = worldY % FireBatchProbeChunkSize;
        Vector3 world = new(worldX + 0.5f, worldY, 0f);
        Matrix4x4 matrix = Matrix4x4.TRS(world, Quaternion.identity, Vector3.one);
        uint phaseSeed = unchecked((uint)worldX * 73856093u ^ (uint)worldY * 19349663u);
        Vector4 animation = new(FireBatchProbeAnimationMode, FireBatchProbeFramesPerSecond,
            phaseSeed % FireBatchProbeFrameCount, FireBatchProbeFrameCount);
        renderer.Set(ChunkDepthMeshRenderer.FireDomain, localY * FireBatchProbeChunkSize + localX, 0,
            sprite, material, matrix, world, FireBatchProbeSortingOrder, tint, animation: animation);
    }

    private static void FlushDiagnosticMeshes(List<ChunkDepthMeshRenderer> renderers)
    {
        for (int i = 0; i < renderers.Count; i++) renderers[i].FlushForDiagnostics();
    }

    private static double ElapsedMilliseconds(long started)
        => (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;

    private static Mesh GetMesh(Transform owner)
        => owner != null ? owner.GetComponentInChildren<MeshFilter>(true)?.sharedMesh : null;

    private static int[] ReadMeshInstanceIds(List<Transform> owners)
    {
        var ids = new int[owners.Count];
        for (int i = 0; i < owners.Count; i++) ids[i] = GetMesh(owners[i])?.GetInstanceID() ?? 0;
        return ids;
    }

    private sealed class FireMeshMetrics
    {
        public int PartCount, DrawGroupCount, RowCount, DrawVertexCount, DrawIndexCount;
        public int IndexBufferUploadCount, VertexBufferUploadCount, TopologyRebuildCount;
        public long IndexBufferUploadBytes, VertexBufferUploadBytes;
        public int ParentAnchorRowCount, SortingGroupMismatchCount, MeshRendererMismatchCount;
        public int SortingGroupLayerId, SortingGroupOrder, MeshRendererLayerId, MeshRendererOrder;
        public int SourceMaterialMismatchCount, RendererShaderMismatchCount;
        public string RendererShaderName;

        public static FireMeshMetrics Capture(List<ChunkDepthMeshRenderer> renderers, Material expectedMaterial,
            Shader expectedRendererShader)
        {
            var result = new FireMeshMetrics();
            bool hasSortingGroup = false, hasRenderer = false;
            foreach (ChunkDepthMeshRenderer renderer in renderers)
            {
                ChunkDepthMeshRenderer.EditorUploadDiagnostic value = renderer.CaptureEditorUploadDiagnostic(
                    expectedMaterial, expectedRendererShader);
                result.PartCount += value.PartCount;
                result.DrawGroupCount += value.DrawGroupCount;
                result.RowCount += value.RowCount;
                result.DrawVertexCount += value.DrawVertexCount;
                result.DrawIndexCount += value.DrawIndexCount;
                result.IndexBufferUploadCount += value.IndexBufferUploadCount;
                result.VertexBufferUploadCount += value.VertexBufferUploadCount;
                result.TopologyRebuildCount += value.TopologyRebuildCount;
                result.IndexBufferUploadBytes += value.IndexBufferUploadBytes;
                result.VertexBufferUploadBytes += value.VertexBufferUploadBytes;
                result.ParentAnchorRowCount += value.ParentAnchorRowCount;
                result.SortingGroupMismatchCount += value.SortingGroupMismatchCount;
                result.MeshRendererMismatchCount += value.MeshRendererMismatchCount;
                result.SourceMaterialMismatchCount += value.SourceMaterialMismatchCount;
                result.RendererShaderMismatchCount += value.RendererShaderMismatchCount;
                if (!hasSortingGroup)
                {
                    result.SortingGroupLayerId = value.SortingGroupLayerId;
                    result.SortingGroupOrder = value.SortingGroupOrder;
                    hasSortingGroup = true;
                }
                else if (result.SortingGroupLayerId != value.SortingGroupLayerId ||
                         result.SortingGroupOrder != value.SortingGroupOrder)
                    result.SortingGroupMismatchCount++;
                if (!hasRenderer)
                {
                    result.MeshRendererLayerId = value.MeshRendererLayerId;
                    result.MeshRendererOrder = value.MeshRendererOrder;
                    result.RendererShaderName = value.RendererShaderName;
                    hasRenderer = true;
                }
                else if (result.MeshRendererLayerId != value.MeshRendererLayerId ||
                         result.MeshRendererOrder != value.MeshRendererOrder)
                    result.MeshRendererMismatchCount++;
            }
            return result;
        }

        public JObject ToJson(int actualMeshRendererCount, int actualSortingGroupCount)
            => new()
            {
                ["partCount"] = PartCount,
                ["drawGroupCount"] = DrawGroupCount,
                ["rowCount"] = RowCount,
                ["actualEnabledMeshRendererCount"] = actualMeshRendererCount,
                ["actualSortingGroupCount"] = actualSortingGroupCount,
                ["drawVertexCount"] = DrawVertexCount,
                ["drawIndexCount"] = DrawIndexCount,
                ["indexBufferUploadCount"] = IndexBufferUploadCount,
                ["indexBufferUploadBytes"] = IndexBufferUploadBytes,
                ["vertexBufferUploadCount"] = VertexBufferUploadCount,
                ["vertexBufferUploadBytes"] = VertexBufferUploadBytes,
                ["topologyRebuildCount"] = TopologyRebuildCount,
                ["parentAnchorRowCount"] = ParentAnchorRowCount,
                ["sortingGroupMismatchCount"] = SortingGroupMismatchCount,
                ["meshRendererMismatchCount"] = MeshRendererMismatchCount,
                ["sortingGroupLayerId"] = SortingGroupLayerId,
                ["sortingGroupOrder"] = SortingGroupOrder,
                ["meshRendererLayerId"] = MeshRendererLayerId,
                ["meshRendererOrder"] = MeshRendererOrder,
                ["sourceMaterialMismatchCount"] = SourceMaterialMismatchCount,
                ["rendererShaderMismatchCount"] = RendererShaderMismatchCount,
                ["rendererShaderName"] = RendererShaderName
            };
    }

    #endregion

    #region 世界火场遮蔽后的掉落物表现恢复采样

    private static JObject CaptureDroppedItemFireMask(Sprite sprite)
    {
        string topologyWorldKey = GameplayWorldRegistry.LocalPresentationWorldKey;
        using GameplayWorldRegistry.WorldExecutionScope topologyScope =
            !string.IsNullOrEmpty(topologyWorldKey) && GameplayWorldRegistry.TryGet(topologyWorldKey, out _)
                ? GameplayWorldRegistry.UseWorld(topologyWorldKey) : default;
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        Vector2Int fireCell = WorldTopologyRuntime.NormalizeCell(Vector2Int.zero);
        Vector2Int fireChunkOrigin = new(
            Mathf.FloorToInt((float)fireCell.x / FireBatchProbeChunkSize) * FireBatchProbeChunkSize,
            Mathf.FloorToInt((float)fireCell.y / FireBatchProbeChunkSize) * FireBatchProbeChunkSize);
        Vector2 firePosition = (Vector2)fireCell + Vector2.one * 0.5f;
        Vector2Int movedCell = WorldTopologyRuntime.NormalizeCell(fireCell + new Vector2Int(4, 0));
        if (movedCell == fireCell)
            return new JObject { ["movementCellAvailable"] = false };

        string worldKey = "__lua_world_fire_visual_" + Guid.NewGuid().ToString("N");
        WorldRuntime world = null;
        Scene testScene = default;
        LightweightDroppedItemSimulation simulation = null;
        DroppedItemPresentation presentation = null;
        JObject result = null;
        bool worldModelReleased = false;
        try
        {
            world = GameplayWorldRegistry.GetOrCreateOwnedWorldModel(worldKey, 1);
            FlatWorld.WorldModel.WorldAddress address = new("surface",
                new Int2(fireChunkOrigin.x, fireChunkOrigin.y));
            var profile = new ChunkGenerationProfileSnapshot("lua_world_fire_visual", 1,
                FireBatchProbeChunkSize, FireBatchProbeChunkSize);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
            using (var generation = new ChunkGenerationResult(request,
                       new ChunkTerrainBuffer(FireBatchProbeChunkSize, FireBatchProbeChunkSize)))
            {
                if (!world.TryCommit(generation, out string rejection))
                    throw new InvalidOperationException("火焰遮蔽采样区块未能提交：" + rejection);
            }
            if (!world.TryGetChunkTerrain(address, out ChunkTerrainData terrain))
                throw new InvalidOperationException("火焰遮蔽采样区块未进入 Ready 状态。");
            int localX = fireCell.x - fireChunkOrigin.x;
            int localY = fireCell.y - fireChunkOrigin.y;
            terrain.SetFire(localX, localY, byte.MaxValue);
            bool firePresent = WorldFireSystem.TryGetWorldFireIntensity(worldKey, firePosition, out float fireIntensity) &&
                fireIntensity > 0f;

            testScene = SceneManager.CreateScene("LuaWorldFireVisual_" + worldKey.Substring(worldKey.Length - 8));
            var cameraRoot = new GameObject("Lua World Fire Camera");
            SceneManager.MoveGameObjectToScene(cameraRoot, testScene);
            Camera camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.aspect = 2f;
            camera.transform.position = new Vector3(firePosition.x, firePosition.y, -10f);

            const int id = 89173;
            var payload = new Data_GeneralItem
            {
                IDName = "lua_world_fire_visual_item",
                MatterState = new ItemMatterState { Initialized = true, IsBurning = true }
            };
            simulation = new LightweightDroppedItemSimulation();
            var body = new LightweightDroppedBody
            {
                Id = id,
                Position = new float2(firePosition.x, firePosition.y),
                Scale = new float2(1f, 1f),
                Amount = 1f,
                Pickable = 1
            };
            simulation.Create(body);
            var visuals = new Dictionary<int, DroppedItemVisual>
            {
                [id] = new DroppedItemVisual
                {
                    Sprite = sprite,
                    Color = Color.white,
                    Layer = SortingLayer.NameToID("Default"),
                    Order = 0,
                    LocalPosition = Vector3.zero,
                    LocalRotation = Quaternion.identity,
                    LocalScale = Vector3.one
                }
            };
            var payloads = new Dictionary<int, ItemData> { [id] = payload };
            var transportItems = new HashSet<int>();
            presentation = new DroppedItemPresentation(testScene, worldKey, simulation, visuals, payloads, transportItems);
            Transform presentationRoot = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(
                testScene, RuntimeSceneObjectCategory.DroppedItems);
            var spatial = new Dictionary<Vector2Int, HashSet<int>>
            {
                [new Vector2Int(Mathf.FloorToInt(firePosition.x), Mathf.FloorToInt(firePosition.y))] = new HashSet<int> { id }
            };
            presentation.Present(camera, topology, spatial, 1f, Matrix4x4.identity);
            bool suppressedInSameCell = presentationRoot.GetComponentsInChildren<ShaderFlameEffect>(true).Length == 0;

            Vector2 movedPosition = (Vector2)movedCell + Vector2.one * 0.5f;
            body.Position = new float2(movedPosition.x, movedPosition.y);
            simulation.Set(body);
            spatial.Clear();
            spatial[new Vector2Int(movedCell.x, movedCell.y)] = new HashSet<int> { id };
            presentation.Present(camera, topology, spatial, 1f, Matrix4x4.identity);
            ShaderFlameEffect movedFlame = presentationRoot.GetComponentInChildren<ShaderFlameEffect>(true);
            bool flameRestoredAfterMove = movedFlame != null && movedFlame.Renderer.enabled;

            terrain.SetFire(localX, localY, 0);
            bool fireExtinguished = !WorldFireSystem.TryGetWorldFireIntensity(worldKey, firePosition, out float remainingIntensity) &&
                remainingIntensity == 0f;
            body.Position = new float2(firePosition.x, firePosition.y);
            simulation.Set(body);
            spatial.Clear();
            spatial[new Vector2Int(fireCell.x, fireCell.y)] = new HashSet<int> { id };
            presentation.Present(camera, topology, spatial, 1f, Matrix4x4.identity);
            ShaderFlameEffect restoredAtExtinguishedCell = presentationRoot.GetComponentInChildren<ShaderFlameEffect>(true);
            bool flameRestoredAfterExtinguish = restoredAtExtinguishedCell != null &&
                restoredAtExtinguishedCell.Renderer.enabled;

            result = new JObject
            {
                ["movementCellAvailable"] = true,
                ["firePresentAtSameCell"] = firePresent,
                ["initialFireIntensity"] = fireIntensity,
                ["independentFlameSuppressedInSameCell"] = suppressedInSameCell,
                ["flameRestoredAfterMoveAway"] = flameRestoredAfterMove,
                ["fireExtinguished"] = fireExtinguished,
                ["remainingFireIntensity"] = remainingIntensity,
                ["flameRestoredAfterExtinguish"] = flameRestoredAfterExtinguish
            };
        }
        finally
        {
            presentation?.Dispose();
            simulation?.Dispose();
            if (world != null)
                worldModelReleased = GameplayWorldRegistry.ReleaseOwnedWorldModel(worldKey, world);
            if (testScene.IsValid() && testScene.isLoaded)
            {
                if (Application.isPlaying) SceneManager.UnloadSceneAsync(testScene);
                else EditorSceneManager.CloseScene(testScene, true);
            }
        }

        if (result != null) result["worldModelReleased"] = worldModelReleased;
        return result;
    }

    #endregion
}
#endif
