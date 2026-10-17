using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

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
        operations.Add("rendering/cave_shadow_spheres", CaveShadowSpheres);
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

    private static DimensionPortalAnchor CreateCavePerformanceAnchor(Data_Player player, int guid, Vector3 position)
    {
        DimensionPortalAnchor result = null;
        WithEntityProbeItem(item =>
        {
            item.BindData(new Data_GeneralItem { Guid = guid });
            item.transform.position = position;
            result = DimensionTravelProgressStore.GetOrCreateCaveAnchor(player, CavePerformanceSurface,
                item, CavePerformanceAddress, new DimensionDefinition());
            return new JObject();
        });
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
        DimensionPortalAnchor anchor = CreateCavePerformanceAnchor(player, 412, entrancePosition);
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
            DimensionTravelProgressStore.UpdateCaveExit(player, anchor, exit);
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
        DimensionPortalAnchor first = CreateCavePerformanceAnchor(save.PlayerData_Dict["first"], 112, firstEntrance);
        DimensionPortalAnchor second = CreateCavePerformanceAnchor(save.PlayerData_Dict["second"], 113, secondEntrance);
        second.CaveExitPosition = new Vector3(25.5f, 25.5f, 0f);
        WithEntityProbeItem(exit =>
        {
            exit.BindData(new Data_GeneralItem { Guid = second.CaveExitGuid });
            exit.transform.position = second.CaveExitPosition;
            DimensionTravelProgressStore.UpdateCaveExit(save.PlayerData_Dict["second"], second, exit);
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
        samples["sameContent"] = CaptureCavePerformanceLight(cache, save, position);
        JObject external = ItemSpecialDataJsonStore.ReadNamespace(player, "flatworld.dimensions");
        external["surfaceEntranceStates"][CavePerformanceSurface.WorldKey]["0,0"]["blocked"] = true;
        ItemSpecialDataJsonStore.WriteNamespace(player, "flatworld.dimensions", external);
        samples["externalBlocked"] = CaptureCavePerformanceLight(cache, save, position);
        external["surfaceEntranceStates"][CavePerformanceSurface.WorldKey]["0,0"]["blocked"] = false;
        ItemSpecialDataJsonStore.WriteNamespace(player, "flatworld.dimensions", external);
        samples["externalRestored"] = CaptureCavePerformanceLight(cache, save, position);
        return samples;
    }

    private static JObject CaveExitCacheIdentity(JObject args)
    {
        var firstSave = CavePerformanceSave();
        var otherSave = CavePerformanceSave();
        Vector2 position = new(0.5f, 0.5f);
        DimensionTravelProgressStore.SetSurfaceEntranceState(firstSave, CavePerformanceSurface, position, false, true);
        DimensionTravelProgressStore.SetSurfaceEntranceState(otherSave, CavePerformanceSurface, new Vector2(50.5f, 0.5f), false, true);
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
}
