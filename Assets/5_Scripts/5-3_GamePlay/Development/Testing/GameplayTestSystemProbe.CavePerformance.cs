using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static partial class GameplayTestSystemProbe
{
    #region 洞穴采光与墙阴影规则登记

    // 夹具只创建独立存档与禁用物品，业务判断全部交给正式采光和阴影入口。
    private static void RegisterCavePerformanceOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("dimension/cave_exit_cache_natural", CaveExitCacheNatural);
        operations.Add("dimension/cave_exit_cache_manual", CaveExitCacheManual);
        operations.Add("dimension/cave_exit_cache_players", CaveExitCachePlayers);
        operations.Add("dimension/cave_exit_cache_sources", CaveExitCacheSources);
        operations.Add("dimension/cave_exit_cache_identity", CaveExitCacheIdentity);
        operations.Add("dimension/cave_exit_cache_distance", CaveExitCacheDistance);
        operations.Add("dimension/cave_runtime_time_registration", CaveRuntimeTimeRegistration);
        operations.Add("dimension/portal_world_ownership", PortalWorldOwnership);
        operations.Add("rendering/cave_shadow_spheres", CaveShadowSpheres);
        operations.Add("rendering/cave_shadow_lazy_creation", CaveShadowLazyCreation);
        operations.Add("ecology/capture_batch", EcologyCaptureBatch);
    }

    private static readonly global::WorldAddress CavePerformanceSurface = new("probe:cave-cache", "surface");
    private static global::WorldAddress CavePerformanceAddress =>
        CavePerformanceSurface.WithDimension(global::WorldAddress.CaveDimensionId);

    private static GameSaveData CavePerformanceSave()
    {
        var save = new GameSaveData();
        save.PlayerData_Dict.Add("first", new Data_Player());
        return save;
    }

    private static JObject CaptureCavePerformanceLight(CaveExitAmbientLightCache cache, GameSaveData save,
        Vector2 position, WorldTopologyDomain domain = default)
    {
        float factor = cache.Evaluate(save, CavePerformanceAddress, position, domain);
        var exits = new List<Vector2>();
        DimensionTravelProgressStore.CollectOpenCaveExitPositions(save, CavePerformanceAddress, exits);
        var positions = new JArray();
        foreach (Vector2 exit in exits) positions.Add(new JObject { ["x"] = exit.x, ["y"] = exit.y });
        return new JObject
        {
            ["factor"] = factor, ["positionsRefreshes"] = cache.PositionsRefreshCount,
            ["distanceEvaluations"] = cache.DistanceEvaluationCount,
            ["revision"] = DimensionTravelProgressStore.GetCaveExitLightingRevision(save),
            ["openCount"] = exits.Count, ["positions"] = positions
        };
    }

    private static DimensionPortalAnchor CreateCavePerformanceAnchor(GameSaveData save, int guid, Vector3 position)
    {
        DimensionPortalAnchor result = null;
        WithEntityProbeItem(item =>
        {
            item.BindData(new Data_GeneralItem { Guid = guid });
            item.transform.position = position;
            result = DimensionTravelProgressStore.GetOrCreateCaveAnchor(save, CavePerformanceSurface,
                item, CavePerformanceAddress, new DimensionDefinition());
            return new JObject();
        });
        return result;
    }

    #endregion

    #region 联机洞穴运行时时钟补登

    private static JObject CaveRuntimeTimeRegistration(JObject args)
    {
        string surface = CavePerformanceSurface.WorldKey;
        string cave = CavePerformanceAddress.WorldKey;
        var currentTime = new TimeData { CurrentTime = 720f };
        currentTime.EnsureTimeSystemDefaults();
        TimeData caveTime = currentTime.CreateRuntimeCopy();
        caveTime.ReferenceScene = surface;

        var saved = new DayTimeSaveData();
        saved.WorldTimeDict[surface] = new SerializableTimeData(currentTime);
        saved.WorldTimeDict[cave] = new SerializableTimeData(caveTime);
        saved.SceneLightingRateDict[surface] = 0.9f;
        saved.SceneLightingRateDict[cave] = 0.4f;
        var clocks = new Dictionary<string, TimeData>();
        var rates = new Dictionary<string, float>();

        DayTimeSystem.RegisterMissingRuntimeSceneTimeData(surface, saved, clocks, rates);
        TimeData liveSurface = clocks[surface];
        liveSurface.CurrentTime = 800f;
        DayTimeSystem.RegisterMissingRuntimeSceneTimeData(cave, saved, clocks, rates);
        TimeData registeredCave = clocks[cave];
        var result = new JObject
        {
            ["sourceClockAlive"] = ReferenceEquals(liveSurface, clocks[surface]) && liveSurface.CurrentTime == 800f,
            ["caveReferencesSurface"] = registeredCave.ReferenceScene == surface,
            ["caveHasLightCurve"] = registeredCave.LightParams.Evaluate(0.5f) > 0f,
            ["surfaceRate"] = rates[surface],
            ["caveRate"] = rates[cave],
            ["clockCount"] = clocks.Count
        };
        registeredCave.CurrentTime = 900f;
        DayTimeSystem.RegisterMissingRuntimeSceneTimeData(cave, saved, clocks, rates);
        result["repeatKeepsClock"] = ReferenceEquals(registeredCave, clocks[cave]) && registeredCave.CurrentTime == 900f;
        result["repeatKeepsRate"] = Mathf.Approximately(rates[cave], 0.4f);
        return result;
    }

    #endregion

    #region 天然与手工出口的真实进度写入

    private static JObject CaveExitCacheNatural(JObject args)
    {
        var save = CavePerformanceSave();
        var cache = new CaveExitAmbientLightCache();
        Vector2 position = new(0.5f, 0.5f);
        var samples = new JObject { ["empty"] = CaptureCavePerformanceLight(cache, save, position) };
        DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, position, false, true);
        samples["opened"] = CaptureCavePerformanceLight(cache, save, position);
        DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, position, true, false);
        samples["blocked"] = CaptureCavePerformanceLight(cache, save, position);
        samples["blockedFlag"] = DimensionTravelProgressStore.IsSurfaceEntranceBlocked(save, CavePerformanceSurface, position);
        samples["generatedWhileBlocked"] = DimensionTravelProgressStore.WasGeneratedSurfaceEntrance(save, CavePerformanceSurface, position);
        DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, position, false, false);
        samples["restored"] = CaptureCavePerformanceLight(cache, save, position);
        samples["restoredBlockedFlag"] = DimensionTravelProgressStore.IsSurfaceEntranceBlocked(save, CavePerformanceSurface, position);
        return samples;
    }

    private static JObject CaveExitCacheManual(JObject args)
    {
        var save = CavePerformanceSave();
        Data_Player player = save.PlayerData_Dict["first"];
        Vector3 entrancePosition = new(30.5f, 0.5f, 0f);
        DimensionPortalAnchor anchor = CreateCavePerformanceAnchor(save, 412, entrancePosition);
        return WithEntityProbeItem(exit =>
        {
            exit.BindData(new Data_GeneralItem { Guid = anchor.CaveExitGuid });
            exit.transform.position = anchor.CaveExitPosition;
            var cache = new CaveExitAmbientLightCache();
            var samples = new JObject
            {
                ["opened"] = CaptureCavePerformanceLight(cache, save, exit.transform.position),
                ["initialBlocked"] = DimensionTravelProgressStore.IsCaveExitBlocked(save, CavePerformanceAddress, exit, false)
            };
            DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, entrancePosition, true, false);
            samples["blocked"] = CaptureCavePerformanceLight(cache, save, exit.transform.position);
            samples["blockedFlag"] = DimensionTravelProgressStore.IsCaveExitBlocked(save, CavePerformanceAddress, exit, false);
            DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, entrancePosition, false, false);
            samples["restored"] = CaptureCavePerformanceLight(cache, save, exit.transform.position);
            samples["restoredBlockedFlag"] = DimensionTravelProgressStore.IsCaveExitBlocked(save, CavePerformanceAddress, exit, false);
            exit.transform.position = new Vector3(20.5f, 0.5f, 0f);
            DimensionTravelProgressStore.UpdateCaveExit(save, anchor, exit);
            samples["moved"] = CaptureCavePerformanceLight(cache, save, exit.transform.position);
            return samples;
        });
    }

    private static JObject CaveExitCachePlayers(JObject args)
    {
        var save = CavePerformanceSave();
        save.PlayerData_Dict.Add("second", new Data_Player());
        Vector2 naturalPosition = new(60.5f, 60.5f);
        DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, naturalPosition, false, true);
        var cache = new CaveExitAmbientLightCache();
        var samples = new JObject { ["natural"] = CaptureCavePerformanceLight(cache, save, naturalPosition) };
        Vector3 firstEntrance = new(30.5f, 0.5f, 0f);
        Vector3 secondEntrance = new(40.5f, 0.5f, 0f);
        DimensionPortalAnchor first = CreateCavePerformanceAnchor(save, 112, firstEntrance);
        DimensionPortalAnchor second = CreateCavePerformanceAnchor(save, 113, secondEntrance);
        second.CaveExitPosition = new Vector3(25.5f, 25.5f, 0f);
        WithEntityProbeItem(exit =>
        {
            exit.BindData(new Data_GeneralItem { Guid = second.CaveExitGuid });
            exit.transform.position = second.CaveExitPosition;
            DimensionTravelProgressStore.UpdateCaveExit(save, second, exit);
            return new JObject();
        });
        samples["all"] = CaptureCavePerformanceLight(cache, save, first.CaveExitPosition);
        DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, naturalPosition, true, false);
        DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, firstEntrance, true, false);
        samples["partlyBlocked"] = CaptureCavePerformanceLight(cache, save, second.CaveExitPosition);
        DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, naturalPosition, false, false);
        DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, firstEntrance, false, false);
        samples["restored"] = CaptureCavePerformanceLight(cache, save, first.CaveExitPosition);
        return samples;
    }

    #endregion

    #region 缓存源身份与距离失效边界

    private static JObject CaveExitCacheSources(JObject args)
    {
        var save = CavePerformanceSave();
        Data_Player player = save.PlayerData_Dict["first"];
        Vector2 position = new(0.5f, 0.5f);
        DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, position, false, true);
        var cache = new CaveExitAmbientLightCache();
        var samples = new JObject { ["before"] = CaptureCavePerformanceLight(cache, save, position) };
        DimensionTravelProgressStore.SetLastPosition(player, CavePerformanceAddress, new Vector3(9.5f, 7.5f, 0f));
        samples["lastPosition"] = CaptureCavePerformanceLight(cache, save, position);
        samples["hasLastPosition"] = DimensionTravelProgressStore.TryGetLastPosition(player, CavePerformanceAddress, out Vector3 last);
        samples["lastX"] = last.x;
        samples["lastY"] = last.y;
        ItemSpecialDataJsonStore.WriteNamespace(player, "probe.cave.other", new JObject { ["value"] = 5 });
        samples["otherNamespace"] = CaptureCavePerformanceLight(cache, save, position);
        player.ItemSpecialData = new string(player.ItemSpecialData.ToCharArray());
        save.DimensionStateJson = new string(save.DimensionStateJson.ToCharArray());
        samples["sameContent"] = CaptureCavePerformanceLight(cache, save, position);
        JObject external = ItemSpecialDataJsonStore.ReadRoot(save.DimensionStateJson);
        external["surfaceEntranceStates"][CavePerformanceSurface.WorldKey]["0,0"]["blocked"] = true;
        save.DimensionStateJson = external.ToString(Newtonsoft.Json.Formatting.None);
        samples["externalBlocked"] = CaptureCavePerformanceLight(cache, save, position);
        external["surfaceEntranceStates"][CavePerformanceSurface.WorldKey]["0,0"]["blocked"] = false;
        save.DimensionStateJson = external.ToString(Newtonsoft.Json.Formatting.None);
        samples["externalRestored"] = CaptureCavePerformanceLight(cache, save, position);
        return samples;
    }

    private static JObject CaveExitCacheIdentity(JObject args)
    {
        var firstSave = CavePerformanceSave();
        var otherSave = CavePerformanceSave();
        Vector2 position = new(0.5f, 0.5f);
        DimensionTravelProgressStore.SetSurfaceEntranceState(firstSave, CavePerformanceSurface, position, false, true);
        DimensionTravelProgressStore.SetSurfaceEntranceState(otherSave, CavePerformanceSurface, new Vector2(10.5f, 0.5f), false, true);
        var cache = new CaveExitAmbientLightCache();
        var samples = new JObject { ["firstSave"] = CaptureCavePerformanceLight(cache, firstSave, position) };
        samples["otherSave"] = CaptureCavePerformanceLight(cache, otherSave, position);
        samples["returnSave"] = CaptureCavePerformanceLight(cache, firstSave, position);
        string original = firstSave.PlayerData_Dict["first"].ItemSpecialData;
        firstSave.PlayerData_Dict["first"] = new Data_Player();
        samples["replaced"] = CaptureCavePerformanceLight(cache, firstSave, position);
        samples["playerCount"] = firstSave.PlayerData_Dict.Count;
        firstSave.PlayerData_Dict["first"] = new Data_Player { ItemSpecialData = original };
        samples["replacementRestored"] = CaptureCavePerformanceLight(cache, firstSave, position);
        firstSave.PlayerData_Dict.Add("second", new Data_Player());
        samples["addedPlayer"] = CaptureCavePerformanceLight(cache, firstSave, position);
        firstSave.PlayerData_Dict.Remove("first");
        samples["removedOwner"] = CaptureCavePerformanceLight(cache, firstSave, position);
        return samples;
    }

    private static JObject CaveExitCacheDistance(JObject args)
    {
        var save = CavePerformanceSave();
        Vector2 position = new(0.5f, 0.5f);
        DimensionTravelProgressStore.SetSurfaceEntranceState(save, CavePerformanceSurface, position, false, true);
        var cache = new CaveExitAmbientLightCache();
        var samples = new JObject { ["before"] = CaptureCavePerformanceLight(cache, save, position) };
        for (int index = 0; index < 100; index++) cache.Evaluate(save, CavePerformanceAddress, position, default);
        samples["stationary"] = CaptureCavePerformanceLight(cache, save, position);
        samples["moved"] = CaptureCavePerformanceLight(cache, save, new Vector2(20.5f, 0.5f));
        Vector2 seamPosition = new(255.5f, 0.5f);
        samples["unwrapped"] = CaptureCavePerformanceLight(cache, save, seamPosition);
        samples["wrapped"] = CaptureCavePerformanceLight(cache, save, seamPosition,
            new WorldTopologyDomain(new int2(0, 0), new int2(256, 256), true));
        samples["changedMin"] = CaptureCavePerformanceLight(cache, save, seamPosition,
            new WorldTopologyDomain(new int2(-128, -128), new int2(256, 256), true));
        samples["changedSpan"] = CaptureCavePerformanceLight(cache, save, seamPosition,
            new WorldTopologyDomain(new int2(-128, -128), new int2(512, 512), true));
        cache.Clear();
        samples["cleared"] = CaptureCavePerformanceLight(cache, save, position);
        return samples;
    }

    #endregion

    #region 墙阴影保守球形筛选采样

    private static JObject CaveShadowSpheres(JObject args)
    {
        var first = new BoundingSphere(new Vector3((float)Number(args, "ax", 0), (float)Number(args, "ay", 0),
            (float)Number(args, "az", 0)), (float)Number(args, "ar", 2));
        var second = new BoundingSphere(new Vector3((float)Number(args, "bx", 0), (float)Number(args, "by", 0),
            (float)Number(args, "bz", 0)), (float)Number(args, "br", 2));
        return new JObject
        {
            ["intersects"] = ChunkLightOccluderVisibilityScheduler.SpheresIntersect(first, second),
            ["reverseIntersects"] = ChunkLightOccluderVisibilityScheduler.SpheresIntersect(second, first)
        };
    }

    #endregion

    #region 墙阴影按需创建与重绑采样

    // 夹具走正式绑定和光照刷新，只返回对象、网格和变换状态给 Lua 断言。
    private static JObject CaveShadowLazyCreation(JObject args)
    {
        using var world = new FlatWorld.WorldModel.WorldRuntime("probe:shadow_lazy", 1);
        var address = new FlatWorld.WorldModel.WorldAddress("probe:shadow_lazy", new FlatWorld.WorldModel.Int2(0, 0));
        var profile = new FlatWorld.WorldModel.ChunkGenerationProfileSnapshot("probe:shadow_lazy", 1, 4, 2);
        var request = world.BeginChunkGeneration(address, 1, profile);
        using (var buffer = new FlatWorld.WorldModel.ChunkTerrainBuffer(4, 2))
        using (var generated = new FlatWorld.WorldModel.ChunkGenerationResult(request, buffer))
            if (!world.TryCommit(generated, out string reason)) throw new InvalidOperationException(reason);
        world.TryGetChunk(address, out var chunk);
        var wall = new FlatWorld.WorldModel.TerrainCell(1, 0, 2, 0, 1, FlatWorld.WorldModel.TerrainCellFlags.Blocking);
        var empty = new FlatWorld.WorldModel.TerrainCell(1, 0, 0, 0, 1, FlatWorld.WorldModel.TerrainCellFlags.Walkable);
        chunk.Terrain.SetCell(0, 0, wall);
        chunk.Terrain.SetCell(1, 0, wall);
        var root = new GameObject("probe:shadow_lazy");
        try
        {
            var renderer = root.AddComponent<ChunkLightOccluderRenderer>();
            renderer.Bind(chunk);
            var samples = new JObject
            {
                ["candidates"] = renderer.ActiveOccluderCount,
                ["createdAtBind"] = renderer.RetainedOccluderCount
            };
            var noLights = Array.Empty<BoundingSphere>();
            renderer.RefreshShadowVisibility(noLights, false, true);
            samples["createdWithoutLight"] = renderer.RetainedOccluderCount;
            var near = new[] { new BoundingSphere(new Vector3(1f, 0.5f, 0f), 1f) };
            renderer.RefreshShadowVisibility(near, false, true);
            var first = root.GetComponentInChildren<ShadowCaster2D>(true);
            samples["createdWhenLit"] = renderer.RetainedOccluderCount;
            samples["enabledWhenLit"] = renderer.EnabledOccluderCount;
            samples["hasMesh"] = first != null && first.mesh != null && first.mesh.vertexCount > 0;
            samples["selfShadows"] = first != null && first.selfShadows;
            if (first == null) return samples;
            samples["initialX"] = first.transform.localPosition.x;
            samples["initialWidth"] = first.transform.localScale.x;
            renderer.RefreshShadowVisibility(noLights, false, true);
            samples["disabledWithoutLight"] = renderer.EnabledOccluderCount;
            samples["retainedWithoutLight"] = renderer.RetainedOccluderCount;
            renderer.RefreshShadowVisibility(near, false, true);
            samples["sameAfterRelight"] = first == root.GetComponentInChildren<ShadowCaster2D>(true);
            var firstMesh = first.mesh;
            renderer.Unbind();
            chunk.Terrain.SetCell(0, 0, empty);
            chunk.Terrain.SetCell(1, 0, empty);
            for (int x = 1; x < 4; x++) chunk.Terrain.SetCell(x, 1, wall);
            root.transform.position = new Vector3(20f, 10f, 0f);
            root.transform.localScale = new Vector3(-1f, 2f, 1f);
            renderer.Bind(chunk);
            renderer.RefreshShadowVisibility(new[]
            {
                new BoundingSphere(root.transform.TransformPoint(new Vector3(2.5f, 1.5f, 0f)), 0.5f)
            }, false, true);
            var reused = root.GetComponentInChildren<ShadowCaster2D>(true);
            samples["sameAfterRebind"] = first == reused;
            if (reused == null) return samples;
            samples["sameMeshAfterRebind"] = firstMesh == reused.mesh;
            samples["reboundX"] = reused.transform.localPosition.x;
            samples["reboundY"] = reused.transform.localPosition.y;
            samples["reboundWidth"] = reused.transform.localScale.x;
            samples["reboundHeight"] = reused.transform.localScale.y;
            samples["enabledAfterRebind"] = renderer.EnabledOccluderCount;
            samples["worldX"] = reused.transform.position.x;
            samples["worldY"] = reused.transform.position.y;
            renderer.RefreshShadowVisibility(noLights, false, true);
            renderer.RefreshShadowVisibility(noLights, true, true);
            samples["fallbackEnabled"] = renderer.EnabledOccluderCount;
            samples["retainedAtEnd"] = renderer.RetainedOccluderCount;

            // 洞穴取消墙体遮黑后，地表阴影槽回池复用也不能在洞穴中重新启用。
            using var caveWorld = new FlatWorld.WorldModel.WorldRuntime(global::WorldAddress.CaveDimensionId, 1);
            var caveAddress = new FlatWorld.WorldModel.WorldAddress(
                global::WorldAddress.CaveDimensionId, new FlatWorld.WorldModel.Int2(0, 0));
            var caveProfile = new FlatWorld.WorldModel.ChunkGenerationProfileSnapshot("probe:cave_no_shadows", 1, 4, 2);
            var caveRequest = caveWorld.BeginChunkGeneration(caveAddress, 1, caveProfile);
            using (var buffer = new FlatWorld.WorldModel.ChunkTerrainBuffer(4, 2))
            using (var generated = new FlatWorld.WorldModel.ChunkGenerationResult(caveRequest, buffer))
                if (!caveWorld.TryCommit(generated, out string caveReason)) throw new InvalidOperationException(caveReason);
            caveWorld.TryGetChunk(caveAddress, out var caveChunk);
            caveChunk.Terrain.SetCell(0, 0, wall);
            renderer.Bind(caveChunk);
            renderer.RefreshShadowVisibility(near, true, true);
            samples["caveCandidates"] = renderer.ActiveOccluderCount;
            samples["caveEnabled"] = renderer.EnabledOccluderCount;
            samples["caveCasterActive"] = first.gameObject.activeSelf;

            renderer.Bind(chunk);
            renderer.RefreshShadowVisibility(noLights, true, true);
            samples["surfaceRecovered"] = renderer.EnabledOccluderCount;
            samples["surfaceCasterReused"] = first == root.GetComponentInChildren<ShadowCaster2D>(true);
            return samples;
        }
        finally
        {
            foreach (var caster in root.GetComponentsInChildren<ShadowCaster2D>(true))
                if (caster.mesh != null) UnityEngine.Object.DestroyImmediate(caster.mesh);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    #endregion

    #region 自然物批量差量写入采样

    private static JObject EcologyCaptureBatch(JObject args)
    {
        var world = new EcologyWorldSaveData();
        var unrelated = world.GetOrCreateChunk(9, 9);
        var chunk = world.GetOrCreateChunk(0, 0);
        var duplicate = new Data_GeneralItem { Guid = 5 };
        chunk.ChangedItems.AddRange(new ItemData[]
        {
            null, new Data_GeneralItem { Guid = 5 }, new Data_GeneralItem { Guid = 7 }, duplicate
        });
        chunk.RemovedGuids.AddRange(new[] { 5, 5, 11, 19 });
        var lastFive = new Data_GeneralItem { Guid = 5 };
        var lastEleven = new Data_GeneralItem { Guid = 11 };
        world.CaptureChangedItems(0, 0, new ItemData[]
        {
            null, new Data_GeneralItem { Guid = 0 }, new Data_GeneralItem { Guid = 5 },
            new Data_GeneralItem { Guid = 11 }, lastFive, lastEleven, new Data_GeneralItem { Guid = 13 }
        });
        var guids = new JArray();
        foreach (ItemData item in chunk.ChangedItems) guids.Add(item?.Guid ?? 0);
        var removed = new JArray();
        foreach (int guid in chunk.RemovedGuids) removed.Add(guid);
        chunk.TryGetChangedItem(5, out ItemData capturedFive);
        chunk.TryGetChangedItem(11, out ItemData capturedEleven);
        var samples = new JObject
        {
            ["guids"] = guids, ["removed"] = removed,
            ["lastFive"] = ReferenceEquals(capturedFive, lastFive),
            ["lastEleven"] = ReferenceEquals(capturedEleven, lastEleven),
            ["duplicatePreserved"] = ReferenceEquals(chunk.ChangedItems[3], duplicate),
            ["unrelatedPreserved"] = ReferenceEquals(world.Chunks[0], unrelated)
        };
        var nextThirteen = new Data_GeneralItem { Guid = 13 };
        world.CaptureChangedItems(0, 0, new ItemData[] { nextThirteen, new Data_GeneralItem { Guid = 17 } });
        samples["secondBatchReplaced"] = ReferenceEquals(chunk.ChangedItems[5], nextThirteen);
        samples["lastGuid"] = chunk.ChangedItems[6].Guid;
        samples["itemCount"] = chunk.ChangedItems.Count;
        world.CaptureChangedItems(0, 0, null);
        world.CaptureChangedItems(0, 0, Array.Empty<ItemData>());
        world.CaptureChangedItems(4, 4, new ItemData[] { null, new Data_GeneralItem { Guid = 0 } });
        samples["chunkCount"] = world.Chunks.Count;
        return samples;
    }

    #endregion

    #region 共有入口与角色独立落点
    private static JObject PortalWorldOwnership(JObject args)
    {
        var save = CavePerformanceSave();
        var first = save.PlayerData_Dict["first"];
        var second = new Data_Player();
        DimensionTravelProgressStore.SetLastPosition(first, CavePerformanceAddress, new Vector3(4, 5));
        DimensionTravelProgressStore.SetLastPosition(second, CavePerformanceAddress, new Vector3(9, 8));
        string before = save.DimensionStateJson;
        DimensionPortalAnchor preview = null;
        WithEntityProbeItem(entrance =>
        {
            entrance.BindData(new Data_GeneralItem { Guid = 713 });
            entrance.transform.position = new Vector3(30.5f, .5f);
            preview = DimensionTravelProgressStore.GetOrCreateCaveAnchor(save, CavePerformanceSurface,
                entrance, CavePerformanceAddress, new DimensionDefinition(), commit: false);
            return new JObject();
        });
        bool previewReadOnly = save.DimensionStateJson == before;
        DimensionPortalAnchor anchor = CreateCavePerformanceAnchor(save, 713, new Vector3(30.5f, .5f));
        save.PlayerData_Dict.Clear();
        return WithEntityProbeItem(exit =>
        {
            exit.BindData(new Data_GeneralItem { Guid = anchor.CaveExitGuid });
            exit.transform.position = anchor.CaveExitPosition;
            bool shared = DimensionTravelProgressStore.TryGetAnchorByCaveExit(save, CavePerformanceAddress, exit, out var paired);
            byte[] bytes = MemoryPack.MemoryPackSerializer.Serialize(save);
            GameSaveData restored = MemoryPack.MemoryPackSerializer.Deserialize<GameSaveData>(bytes);
            bool roundtrip = DimensionTravelProgressStore.TryGetAnchorByCaveExit(restored, CavePerformanceAddress, exit, out var saved);
            DimensionTravelProgressStore.TryGetLastPosition(first, CavePerformanceAddress, out Vector3 firstLast);
            DimensionTravelProgressStore.TryGetLastPosition(second, CavePerformanceAddress, out Vector3 secondLast);
            return new JObject
            {
                ["previewReadOnly"] = previewReadOnly,
                ["previewIdentityStable"] = preview.CaveExitGuid == anchor.CaveExitGuid,
                ["sharedFound"] = shared, ["sharedIdentity"] = shared && paired.SurfaceEntranceGuid == 713,
                ["playersEmpty"] = save.PlayerData_Dict.Count == 0,
                ["roundtripFound"] = roundtrip,
                ["roundtripIdentity"] = roundtrip && saved.CaveExitGuid == anchor.CaveExitGuid,
                ["firstLastX"] = firstLast.x, ["secondLastX"] = secondLast.x
            };
        });
    }
    #endregion
}
