using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 自然物批次正式边界采样

    private static void RegisterNaturalEntityBatchOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ecology/body_read_batch", NaturalEntityBodyReadBatch);
        operations.Add("ecology/selective_body_read_batch", NaturalEntitySelectiveBodyReadBatch);
        operations.Add("ecology/spatial_geometry", NaturalEntitySpatialGeometryValues);
        operations.Add("ecology/soil_snapshot_reuse", NaturalEntitySoilSnapshotReuse);
        operations.Add("ecology/soil_liquid_prefilter", NaturalEntitySoilLiquidPrefilter);
        operations.Add("ecology/output_validation_cache", NaturalEntityOutputValidationCache);
    }

    // 只创建自有 ECS World，读取正式批次结果，不触碰玩家世界或重算业务规则。
    private static JObject NaturalEntityBodyReadBatch(JObject args)
    {
        using var world = new World("probe:natural-body-batch");
        using var simulation = new NaturalEntitySimulation(world);
        var profile = new NaturalEntityEcsProfile { ComponentTypes = new[]
        {
            ComponentType.ReadWrite<NaturalEntityLocation>(), ComponentType.ReadWrite<EntityModuleAppearance>(),
            ComponentType.ReadWrite<EntityModuleActive>(), ComponentType.ReadWrite<AiecsVital>()
        } };
        var plainProfile = new NaturalEntityEcsProfile { ComponentTypes = new[]
        {
            ComponentType.ReadWrite<NaturalEntityLocation>(), ComponentType.ReadWrite<EntityModuleAppearance>(),
            ComponentType.ReadWrite<EntityModuleActive>()
        } };
        var body = new NaturalEntityBody { RuntimeId = 1, NaturalGuid = 41, Position = new float2(2f, 3f),
            Scale = new float2(1f), VisualVersion = 5 };
        simulation.Create(profile, 1, body, new AiecsVital { Hp = 10f, MaxHp = 10f }, null, null, null);
        body.RuntimeId = 2; body.NaturalGuid = 42; body.Suspended = 1;
        simulation.Create(profile, 2, body, new AiecsVital { Hp = 3f, MaxHp = 10f, Dead = 1 }, null, null, null);
        var result = new JObject();
        using (simulation.BeginBodyReadBatch())
        {
            NaturalEntityBody first = simulation.GetBody(1), inactive = simulation.GetBody(2);
            result["initialGuid"] = first.NaturalGuid; result["initialRevision"] = first.VisualVersion;
            result["inactiveDead"] = inactive.Dead; result["inactiveSuspended"] = inactive.Suspended;
            simulation.Set(1, new AiecsVital { Hp = 0f, MaxHp = 10f, Dead = 1 });
            result["deadAfterWrite"] = simulation.GetBody(1).Dead;
            first.Position = new float2(8f, 9f); first.VisualVersion = 6;
            simulation.SetBody(1, first);
            result["movedX"] = simulation.GetBody(1).Position.x;
            result["movedRevision"] = simulation.GetBody(1).VisualVersion;
            simulation.Manager.SetComponentData(simulation.GetEntity(1),
                new EntityModuleAppearance { Scale = new float2(2f), Revision = 9 });
            simulation.InvalidateBodySnapshot(1);
            result["externalRevision"] = simulation.GetBody(1).VisualVersion;
            simulation.Remove(2);
            result["removed"] = !simulation.Contains(2);
            body.RuntimeId = 3; body.NaturalGuid = 43; body.Suspended = 0;
            simulation.Create(plainProfile, 3, body, null, null, null, null);
            result["createdGuid"] = simulation.GetBody(3).NaturalGuid;
        }
        simulation.Manager.SetComponentData(simulation.GetEntity(1),
            new EntityModuleAppearance { Scale = new float2(3f), Revision = 12 });
        result["afterBatchRevision"] = simulation.GetBody(1).VisualVersion;
        using (simulation.BeginBodyReadBatch())
        {
            result["nextBatchScale"] = simulation.GetBody(1).Scale.x;
            result["plainDead"] = simulation.GetBody(3).Dead;
        }
        return result;
    }

    // 真实产物校验入口：同一编译配置只执行一次，失败不缓存且新配置重新校验。
    private static JObject NaturalEntityOutputValidationCache(JObject args)
    {
        var profile = new NaturalEntityEcsProfile();
        const int batchSize = 4096;
        int fullValidations = 0;
        for (int i = 0; i < batchSize; i++)
            if (NaturalEntityEcsService.ValidateOutputDefinitionsOnce(profile)) fullValidations++;

        bool freshValidated = NaturalEntityEcsService.ValidateOutputDefinitionsOnce(new NaturalEntityEcsProfile());
        var invalid = new NaturalEntityEcsProfile { TreeSeedId = "probe:missing_output_definition" };
        bool rejected = false;
        try { NaturalEntityEcsService.ValidateOutputDefinitionsOnce(invalid); }
        catch (InvalidOperationException) { rejected = true; }
        bool failedNotCached = !invalid.OutputDefinitionsValidated;
        invalid.TreeSeedId = null;
        bool corrected = NaturalEntityEcsService.ValidateOutputDefinitionsOnce(invalid);
        bool correctedSkipped = !NaturalEntityEcsService.ValidateOutputDefinitionsOnce(invalid);
        return new JObject
        {
            ["calls"] = batchSize, ["fullValidations"] = fullValidations,
            ["freshValidated"] = freshValidated,
            ["rejected"] = rejected, ["failedNotCached"] = failedNotCached,
            ["corrected"] = corrected, ["correctedSkipped"] = correctedSkipped
        };
    }

    /// <summary>少量发布对象只冻结指定身份，并保持批内失效及新增实体语义。</summary>
    private static JObject NaturalEntitySelectiveBodyReadBatch(JObject args)
    {
        using var world = new World("probe:selective-natural-body");
        using var simulation = new NaturalEntitySimulation(world);
        var profile = new NaturalEntityEcsProfile { ComponentTypes = new[]
        {
            ComponentType.ReadWrite<NaturalEntityLocation>(), ComponentType.ReadWrite<EntityModuleAppearance>(),
            ComponentType.ReadWrite<EntityModuleActive>(), ComponentType.ReadWrite<AiecsVital>()
        } };
        for (int id = 1; id <= 24; id++)
        {
            var body = new NaturalEntityBody
            {
                RuntimeId = id, NaturalGuid = id + 1000, Position = new float2(id, 2f),
                Scale = new float2(1f), VisualVersion = (uint)id, Suspended = (byte)(id == 2 ? 1 : 0)
            };
            simulation.Create(profile, id, body,
                new AiecsVital { Hp = 10f, MaxHp = 10f, Dead = (byte)(id == 2 ? 1 : 0) },
                null, null, null);
        }

        var targets = new HashSet<int> { 1, 2, 999 };
        var result = new JObject();
        using (simulation.BeginBodyReadBatch(targets))
        {
            result["frozenCount"] = simulation.CapturedBodyCount;
            result["initialPosition"] = simulation.GetBody(1).Position.x;
            result["inactiveDead"] = simulation.GetBody(2).Dead;
            result["inactiveSuspended"] = simulation.GetBody(2).Suspended;
            simulation.Manager.SetComponentData(simulation.GetEntity(1),
                new NaturalEntityLocation { RuntimeId = 1, NaturalGuid = 1001, Position = new float2(77f, 2f) });
            result["frozenPosition"] = simulation.GetBody(1).Position.x;
            result["uncapturedBody"] = simulation.GetBody(3).Position.x;
            simulation.InvalidateBodySnapshot(1);
            result["refreshedPosition"] = simulation.GetBody(1).Position.x;
            simulation.Remove(2);
            result["removed"] = !simulation.Contains(2);
            simulation.Create(profile, 25, new NaturalEntityBody
            {
                RuntimeId = 25, NaturalGuid = 1025, Position = new float2(25f, 3f),
                Scale = new float2(1f), VisualVersion = 25
            }, new AiecsVital { Hp = 10f, MaxHp = 10f }, null, null, null);
            result["newEntity"] = simulation.GetBody(25).NaturalGuid;
        }
        result["afterBatch"] = simulation.GetBody(1).Position.x;
        using (simulation.BeginBodyReadBatch())
        {
            result["fullCount"] = simulation.CapturedBodyCount;
            result["fullPosition"] = simulation.GetBody(1).Position.x;
        }
        return result;
    }

    private static JObject NaturalEntitySpatialGeometryValues(JObject args)
    {
        var collider = new ItemColliderDefinitionDto { Enabled = true, IsTrigger = false,
            Offset = new Vector2(1f, 0f), Size = new Vector2(2f, 4f) };
        var parameters = JObject.Parse("{\"$collider2D\":{\"enabled\":true,\"offset\":{\"x\":0,\"y\":0},\"size\":{\"x\":1,\"y\":1}},\"$transform\":{\"localPosition\":{\"x\":0,\"y\":2,\"z\":0}}}");
        NaturalEntitySpatialGeometry normal = NaturalEntitySpatialGeometry.Compile(collider, parameters, false);
        NaturalEntitySpatialGeometry tree = NaturalEntitySpatialGeometry.Compile(collider, parameters, true);
        Matrix4x4 root = Matrix4x4.TRS(new Vector3(10f, 20f), Quaternion.identity, new Vector3(2f, 1f, 1f));
        normal.Resolve(root, out Bounds body, out Bounds hit);
        tree.Resolve(root, out _, out Bounds treeHit);
        parameters["$collider2D"]["size"]["x"] = 9f;
        parameters["$collider2D"]["enabled"] = false;
        normal.Resolve(root, out _, out Bounds frozenHit);
        NaturalEntitySpatialGeometry refreshed = NaturalEntitySpatialGeometry.Compile(collider, parameters, false);
        refreshed.Resolve(root, out _, out Bounds refreshedHit);
        return new JObject
        {
            ["bodyX"] = body.center.x, ["bodyY"] = body.center.y, ["bodyWidth"] = body.size.x,
            ["bodyHeight"] = body.size.y, ["hitX"] = hit.center.x, ["hitY"] = hit.center.y,
            ["hitWidth"] = hit.size.x, ["hitHeight"] = hit.size.y,
            ["treeContainsBodyMin"] = treeHit.Contains(body.min), ["treeContainsBodyMax"] = treeHit.Contains(body.max),
            ["compiledUnchanged"] = frozenHit.Equals(hit), ["compiledEnabled"] = normal.HitColliderEnabled,
            ["refreshedEnabled"] = refreshed.HitColliderEnabled, ["refreshedWidth"] = refreshedHit.size.x,
            ["blocking"] = normal.BlocksMovement
        };
    }

    private static JObject NaturalEntitySoilSnapshotReuse(JObject args)
    {
        using ChunkTerrainData terrain = EnvironmentBatchFixture(2, 1);
        var cell = new Vector2Int(0, 0);
        var sample = new RuntimeTerrainTileSample(default, terrain, cell, cell, terrain.GetCell(0, 0), 1);
        var soil = new TileData_Farmland();
        var result = new JObject();
        terrain.SetEnvironmentValue("moisture", 0, 0, .6f);
        terrain.SetEnvironmentValue("fertility", 0, 0, .7f);
        FarmlandSystem.ReadSoilSnapshot(sample, soil);
        result["generatedWater"] = soil.waterValue; result["generatedFertility"] = soil.Fertility;
        terrain.SetEnvironmentValue(FarmlandSystem.SourceLayer, 0, 0, 1f);
        terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, 0, 0, 3f);
        terrain.SetEnvironmentValue(FarmlandSystem.FertilityLayer, 0, 0, .4f);
        terrain.SetEnvironmentValue(FarmlandSystem.WaterConsumptionRemainderLayer, 0, 0, .25f);
        FarmlandSystem.ReadSoilSnapshot(sample, soil);
        result["latestWater"] = soil.waterValue; result["latestFertility"] = soil.Fertility;
        result["latestRemainder"] = soil.waterConsumptionRemainder;
        terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, 0, 0, 0f);
        FarmlandSystem.ReadSoilSnapshot(sample, soil);
        result["emptyWater"] = soil.waterValue; result["emptyRemainder"] = soil.waterConsumptionRemainder;
        return result;
    }

    /// <summary>预筛水格与完整采样保持一致，支撑面遮水、抽干及跨周仍读取当前权威数据。</summary>
    private static JObject NaturalEntitySoilLiquidPrefilter(JObject args)
    {
        using var world = new WorldRuntime("soil-liquid-prefilter", 1);
        var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
        var profile = new ChunkGenerationProfileSnapshot("soil-liquid-prefilter", 1, 4, 2);
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
        using var buffer = new ChunkTerrainBuffer(4, 2);
        int water = buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 4; x++)
            buffer.SetCell(x, y, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        buffer.SetLiquid(1, 0, water, 4);
        buffer.SetLiquid(2, 0, water, 6);
        buffer.SetEnvironmentValue(TerrainSupportLayer.TileIdLayer, 2, 0, 90f);
        using (var result = new ChunkGenerationResult(request, buffer))
            if (!world.TryCommit(result, out string reason))
                throw new InvalidOperationException("土壤液体预筛夹具提交失败：" + reason);

        var batch = new ChunkMgr.RuntimeTerrainPresenceQuery(world, default, "surface", 4, 2);
        bool dry = batch.TrySample(new Vector2(0.5f, 0.5f), out _) &&
            !batch.TrySampleSurfaceLiquid(new Vector2(0.5f, 0.5f), out _);
        bool waterMatches = batch.TrySample(new Vector2(1.5f, 0.5f), out RuntimeTerrainTileSample regular) &&
            batch.TrySampleSurfaceLiquid(new Vector2(1.5f, 0.5f), out RuntimeTerrainTileSample quick) &&
            quick.WorldCell == regular.WorldCell && quick.LocalCell == regular.LocalCell &&
            quick.LiquidLevel == regular.LiquidLevel && quick.LiquidLevel == 4 &&
            quick.LiquidId == regular.LiquidId;
        bool covered = batch.TrySample(new Vector2(2.5f, 0.5f), out RuntimeTerrainTileSample underSupport) &&
            underSupport.LiquidLevel == 0 && underSupport.Terrain.GetLiquidLevel(2, 0) == 6 &&
            !batch.TrySampleSurfaceLiquid(new Vector2(2.5f, 0.5f), out _);
        bool missing = !batch.TrySampleSurfaceLiquid(new Vector2(4.5f, 0.5f), out _);

        if (!world.TryGetChunk(address, out ChunkRuntime chunk))
            throw new InvalidOperationException("土壤液体夹具区块丢失。");
        chunk.Terrain.SetEnvironmentValue(TerrainSupportLayer.TileIdLayer, 2, 0, 0f);
        bool uncovered = batch.TrySampleSurfaceLiquid(new Vector2(2.5f, 0.5f), out RuntimeTerrainTileSample released) &&
            released.LiquidLevel == 6;
        chunk.Terrain.SetLiquid(1, 0, 0, 0);
        bool drained = !batch.TrySampleSurfaceLiquid(new Vector2(1.5f, 0.5f), out _);
        var wrapped = new ChunkMgr.RuntimeTerrainPresenceQuery(world,
            new WorldTopologyDomain(new int2(0, 0), new int2(16, 16), true), "surface", 4, 2);
        bool seam = wrapped.TrySampleSurfaceLiquid(new Vector2(18.5f, 0.5f), out RuntimeTerrainTileSample across) &&
            across.WorldCell == new Vector2Int(2, 0) && across.LiquidLevel == 6;
        return new JObject
        {
            ["dry"] = dry, ["waterMatches"] = waterMatches, ["covered"] = covered,
            ["missing"] = missing, ["uncovered"] = uncovered, ["drained"] = drained, ["seam"] = seam
        };
    }

    #endregion
}
