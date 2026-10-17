using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 容器接雨与便携建筑规则采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterVesselRainRules()
    {
        Register("containers", "rainwater", EntityVesselRainwater);
        Register("containers", "portable_vessels", EntityPortableVessels);
        Register("containers", "rainwater_quality", EntityRainwaterQuality);
    }

    // 使用独立容器状态采样正式接雨时钟，Lua 决定水量和时间边界是否正确。
    private static JObject EntityVesselRainwater(JObject args)
    {
        var state = new LiquidContainerState { RainwaterProgress = Number(args, "progress", 0d) };
        var samples = new JArray();
        if (args["steps"] is not JArray steps)
            throw new ArgumentException("容器接雨采样必须提供 steps。");
        foreach (JObject step in steps)
        {
            float seconds = (float)Number(step, "seconds", 60d);
            float intensity = (float)Number(step, "intensity", .5d);
            switch (Text(step, "invalid", string.Empty))
            {
                case "nan_seconds": seconds = float.NaN; break;
                case "infinite_intensity": intensity = float.PositiveInfinity; break;
            }
            int collected = VesselRainwaterCollection.Advance(ref state.RainwaterProgress, seconds, intensity,
                (float)Number(step, "space", 8d));
            samples.Add(new JObject { ["collected"] = collected, ["progress"] = state.RainwaterProgress });
        }

        LiquidContainerState saved = MixedLiquidContents.Clone(state);
        state.RainwaterProgress = 0d;
        MixedLiquidContents.Restore(state, saved);
        return new JObject
        {
            ["samples"] = samples, ["restoredProgress"] = state.RainwaterProgress,
            ["defaultInterval"] = VesselRainwaterCollection.DefaultIntervalSeconds
        };
    }

    // 读取正式内容声明，并通过正式共享模块事务往返真实序列化载荷。
    private static JObject EntityPortableVessels(JObject args)
    {
        string id = Text(args, "id", "IronBucket");
        string file = id switch
        {
            "IronBucket" => "iron_bucket.json",
            "WoodenBarrel" => "wooden_barrel.json",
            _ => throw new ArgumentException("便携容器配置采样仅支持铁桶和木桶。")
        };
        var document = JObject.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,
            "GameConfig", "Items", "shells", file)));
        var definitions = new JArray();
        foreach (JObject definition in (JArray)document["items"])
        {
            if (definition.Value<string>("id") != id && definition.Value<string>("id") != id + "_Building")
                continue;
            JObject module = (JObject)definition["modules"]?["建筑模块"];
            definitions.Add(new JObject
            {
                ["id"] = definition["id"], ["parent"] = definition["parent"],
                ["buildingEnabled"] = module?["enabled"],
                ["building"] = module?["data"]?["BitData"] is JValue data ? JObject.Parse(data.Value<string>()) : null,
                ["requiresPlacement"] = module?["parameters"]?["RequiresPlacementRequest"],
                ["rainEnabled"] = definition["modules"]?["vessel"]?["parameters"]?["CollectsRainwater"]
            });
        }

        var source = EntityProbeData(id, 1f);
        var placed = EntityProbeData(id + "_Building", 1f);
        var returned = EntityProbeData(id, 1f);
        foreach (ItemData itemData in new[] { source, placed, returned })
            itemData.ModuleDataDic.Add("vessel", new Ex_ModData_MemoryPackable
                { ID = Mod_WaterVessel.ModuleId, Name = "vessel", isRunning = true });
        var original = new LiquidContainerState { RainwaterProgress = .75d, Temperature = 18f };
        original.Composition["test:water"] = 3f;
        original.Composition["test:oil"] = .5f;
        MixedLiquidContents.SynchronizeAmounts(original);
        ((Ex_ModData_MemoryPackable)source.ModuleDataDic["vessel"]).WriteData(original);
        BuildingModuleStateTransfer.Copy(source, placed, new[] { Mod_WaterVessel.ModuleId });
        BuildingModuleStateTransfer.Copy(placed, returned, new[] { Mod_WaterVessel.ModuleId });
        var restored = new LiquidContainerState();
        ((Ex_ModData_MemoryPackable)returned.ModuleDataDic["vessel"]).ReadData(ref restored);
        return new JObject
        {
            ["definitions"] = definitions, ["amount"] = restored.Amount,
            ["water"] = MixedLiquidContents.GetAmount(restored, "test:water"),
            ["oil"] = MixedLiquidContents.GetAmount(restored, "test:oil"),
            ["temperature"] = restored.Temperature, ["progress"] = restored.RainwaterProgress,
            ["distinctPayload"] = !ReferenceEquals(
                ((Ex_ModData_MemoryPackable)source.ModuleDataDic["vessel"]).BitData,
                ((Ex_ModData_MemoryPackable)returned.ModuleDataDic["vessel"]).BitData)
        };
    }

    // 正式污染定义与独立地形共用生产读取入口，邻格保持干净以检查取样范围。
    private static JObject EntityRainwaterQuality(JObject args)
    {
        string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath,
            "GameConfig", "Contamination", "core-contamination.json"));
        var definitions = new Dictionary<string, ContaminationDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (ContaminationDefinition definition in ContaminationDefinitionFactory.BuildCatalog(
                     ContaminationDefinitionFactory.DeserializeCatalog(json)))
            definitions.Add(definition.Id, definition);
        if (Flag(args, "includeMod", false))
            definitions.Add("test:mod", new ContaminationDefinition("test:mod", "测试污染", string.Empty, "chemical", 0f, 100f, 0f));
        using var buffer = new ChunkTerrainBuffer(2, 1);
        for (int x = 0; x < 2; x++)
            buffer.SetCell(x, 0, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        using ChunkTerrainData terrain = buffer.Seal();
        if (args["values"] is JObject values)
            foreach (ContaminationDefinition definition in definitions.Values)
                if (values[definition.Id] != null)
                    terrain.SetEnvironmentValue(definition.EnvironmentLayerId, 0, 0,
                        (float)Number(values, definition.Id, 0d));
        float load = ContaminationSystem.ReadNormalizedLoad(terrain, Vector2Int.zero, definitions);
        float neighbor = ContaminationSystem.ReadNormalizedLoad(terrain, Vector2Int.right, definitions);
        return new JObject
        {
            ["load"] = load, ["liquidId"] = VesselRainwaterCollection.ResolveLiquidId(load),
            ["neighborLiquidId"] = VesselRainwaterCollection.ResolveLiquidId(neighbor),
            ["invalidLiquidId"] = VesselRainwaterCollection.ResolveLiquidId(float.NaN)
        };
    }

    #endregion
}
