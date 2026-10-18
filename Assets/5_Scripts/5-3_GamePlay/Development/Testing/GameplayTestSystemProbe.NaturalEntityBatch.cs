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
        operations.Add("ecology/spatial_geometry", NaturalEntitySpatialGeometryValues);
        operations.Add("ecology/soil_snapshot_reuse", NaturalEntitySoilSnapshotReuse);
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

    #endregion
}
