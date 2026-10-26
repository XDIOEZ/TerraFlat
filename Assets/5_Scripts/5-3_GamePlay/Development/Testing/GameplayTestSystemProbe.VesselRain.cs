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
        Register("containers", "contents_open_mouth", EntityContentsOpenMouth);
        Register("containers", "capacity_liters", EntityContainerCapacityLiters);
    }

    // 从正式继承目录读取容器容量，避免手持与落地配置换算后分叉。
    private static JObject EntityContainerCapacityLiters(JObject args)
    {
        List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        var rows = new JArray();
        foreach (string carrier in new[] { "Coconut_Shell", "IronBucket", "WoodenBarrel", "ClayJar", "Crucible", "WaterTrough" })
        foreach (string id in new[] { carrier, carrier + "_Building" })
        {
            ItemDefinitionDto definition = definitions.Find(value => value.Id == id)
                ?? throw new InvalidOperationException("缺少液体容器定义：" + id);
            ItemModuleDefinitionDto vessel = null;
            foreach (ItemModuleDefinitionDto module in definition.Modules.Values)
                if (module.Id == Mod_WaterVessel.ModuleId && module.Enabled != false) { vessel = module; break; }
            if (vessel == null) throw new InvalidOperationException("容器缺少正式液体模块：" + id);
            int capacity = vessel.Parameters.Value<int>("capacity");
            rows.Add(new JObject { ["id"] = id, ["capacity"] = capacity,
                ["liters"] = WorldLiquidVolumeConversion.GetContainerCapacityLiters(capacity) });
        }
        return new JObject { ["containers"] = rows };
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
            "Crucible" => "crucible.json",
            _ => throw new ArgumentException("便携容器配置采样不支持该容器。")
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
                ["rainEnabled"] = definition["modules"]?["vessel"]?["parameters"]?["CollectsRainwater"],
                ["gameObjectBackend"] = module?["parameters"]?["UseGameObjectBackend"],
                ["mortarEnabled"] = definition["modules"]?["mortar"]?["enabled"]
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
        string[] shared = { Mod_WaterVessel.ModuleId };
        if (id == "Crucible")
        {
            shared = new[] { Mod_WaterVessel.ModuleId, Mod_Mortar.ModuleId };
            foreach (ItemData itemData in new[] { source, placed, returned })
                itemData.ModuleDataDic.Add("mortar", new Ex_ModData_MemoryPackable
                    { ID = Mod_Mortar.ModuleId, Name = "mortar", isRunning = true });
            ItemData sand = EntityProbeData("Sand", 3f);
            sand.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 700f };
            var contents = new MortarState { Bowl = new Inventory_Data(new List<ItemSlot>
                { new ItemSlot(0) { itemData = sand } }, "坩埚") };
            ((Ex_ModData_MemoryPackable)source.ModuleDataDic["mortar"]).WriteData(contents);
        }
        BuildingModuleStateTransfer.Copy(source, placed, shared);
        BuildingModuleStateTransfer.Copy(placed, returned, shared);
        var restored = new LiquidContainerState();
        ((Ex_ModData_MemoryPackable)returned.ModuleDataDic["vessel"]).ReadData(ref restored);
        var result = new JObject
        {
            ["definitions"] = definitions, ["amount"] = restored.Amount,
            ["water"] = MixedLiquidContents.GetAmount(restored, "test:water"),
            ["oil"] = MixedLiquidContents.GetAmount(restored, "test:oil"),
            ["temperature"] = restored.Temperature, ["progress"] = restored.RainwaterProgress,
            ["distinctPayload"] = !ReferenceEquals(
                ((Ex_ModData_MemoryPackable)source.ModuleDataDic["vessel"]).BitData,
                ((Ex_ModData_MemoryPackable)returned.ModuleDataDic["vessel"]).BitData)
        };
        if (id == "Crucible")
        {
            var restoredContents = ((Ex_ModData_MemoryPackable)returned.ModuleDataDic["mortar"]).GetData<MortarState>();
            ItemData restoredSand = restoredContents.Bowl.itemSlots[0].itemData;
            result["solidAmount"] = restoredSand.Stack.Amount;
            result["solidTemperature"] = restoredSand.MatterState.TemperatureCelsius;
            result["solidIndependent"] = !ReferenceEquals(
                ((Ex_ModData_MemoryPackable)source.ModuleDataDic["mortar"]).BitData,
                ((Ex_ModData_MemoryPackable)returned.ModuleDataDic["mortar"]).BitData);
        }
        return result;
    }

    // 采样生产开口判定，Lua 覆盖直立、横放、左右倒置与尚未到口的边界。
    private static JObject EntityContentsOpenMouth(JObject args)
        => new JObject { ["canSpill"] = VesselContentsOperations.ReachesOpenMouth(
            (float)Number(args, "tilt", 0), (float)Number(args, "center", 100),
            (float)Number(args, "mouth", 100)) };

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
