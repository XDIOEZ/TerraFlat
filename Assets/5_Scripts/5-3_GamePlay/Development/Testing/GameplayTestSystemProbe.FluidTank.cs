using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 便携气罐缓存规则采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterPortableFluidTankRules()
    {
        Register("industrial_fluids", "portable_tank_cache", WorldPortableTankCache);
        Register("industrial_fluids", "portable_tank_payload", WorldPortableTankPayload);
        Register("industrial_fluids", "cold_temperature_path", WorldColdTemperaturePath);
    }

    // 独立物品使用不存在于目录的身份，规则采样不加载或改写当前资源会话。
    private static ItemData WorldPortableTankItem(out Ex_ModData_MemoryPackable binary)
    {
        binary = new Ex_ModData_MemoryPackable
        {
            ModuleId = Mod_FluidTank.ModuleId,
            StableName = "system-probe:portable_tank"
        };
        return new Data_GeneralItem
        {
            IDName = "system-probe:portable_tank",
            ModuleDataDic = new Dictionary<string, ModuleData>(StringComparer.Ordinal)
            {
                [binary.StableName] = binary
            }
        };
    }

    private static JObject WorldPortableTankCache(JObject args)
    {
        ItemData item = WorldPortableTankItem(out Ex_ModData_MemoryPackable binary);
        bool found = FluidTankStorage.TryGet(item, out FluidInventory first, out FluidTankConfiguration firstConfig);
        bool foundAgain = FluidTankStorage.TryGet(item, out FluidInventory second, out FluidTankConfiguration secondConfig);
        var result = new JObject
        {
            ["found"] = found && foundAgain,
            ["wrapperReused"] = ReferenceEquals(first, second),
            ["wrapsOwnedContents"] = ReferenceEquals(first?.State, FluidTankStorage.Ensure(item).Contents),
            ["configCopies"] = !ReferenceEquals(firstConfig, secondConfig)
        };
        firstConfig.VolumeLiters = 99d;
        firstConfig.SupportsSpacesuit = false;
        FluidTankStorage.TryGet(item, out _, out FluidTankConfiguration afterExternalEdit);
        ItemData other = WorldPortableTankItem(out _);
        FluidTankStorage.TryGet(other, out FluidInventory otherInventory, out FluidTankConfiguration otherConfig);
        result["volumeAfterExternalEdit"] = afterExternalEdit.VolumeLiters;
        result["supportsSpacesuitAfterExternalEdit"] = afterExternalEdit.SupportsSpacesuit;
        result["otherVolume"] = otherConfig.VolumeLiters;
        result["otherSupportsSpacesuit"] = otherConfig.SupportsSpacesuit;
        result["itemsIsolated"] = !ReferenceEquals(first, otherInventory) &&
                                  !ReferenceEquals(afterExternalEdit, otherConfig);

        FluidTankState owned = FluidTankStorage.Ensure(item);
        owned.Contents = new FluidInventoryState();
        FluidTankStorage.TryGet(item, out FluidInventory afterContentsReplacement, out _);
        result["contentsReplacementRebound"] = !ReferenceEquals(first, afterContentsReplacement) &&
                                               ReferenceEquals(owned.Contents, afterContentsReplacement.State);
        result["readContentsUsesOwnedState"] = FluidTankStorage.TryReadContents(item, out FluidInventoryState contents) &&
                                               ReferenceEquals(contents, owned.Contents);
        FluidTankStorage.Flush(item);
        byte[] stableBytes = binary.BitData;
        FluidTankStorage.Flush(item);
        result["unchangedFlushReusesBytes"] = ReferenceEquals(stableBytes, binary.BitData);

        owned.OverpressureClock = .25f;
        FluidTankStorage.Flush(item);
        byte[] clockBytes = binary.BitData;
        result["clockChangeWritesBytes"] = !ReferenceEquals(stableBytes, clockBytes);
        result["persistedClock"] = binary.GetData<FluidTankState>().OverpressureClock;
        FluidTankStorage.Flush(item);
        result["unchangedClockFlushReusesBytes"] = ReferenceEquals(clockBytes, binary.BitData);
        owned.Ruptured = true;
        FluidTankStorage.Flush(item);
        result["persistedRuptured"] = binary.GetData<FluidTankState>().Ruptured;
        result["rupturedTankRejected"] = !FluidTankStorage.TryGet(item, out _, out _);

        binary.WriteData(new FluidTankState());
        FluidTankStorage.TryGet(item, out FluidInventory afterPayloadReplacement, out _);
        result["payloadReplacementRebound"] = !ReferenceEquals(afterContentsReplacement, afterPayloadReplacement) &&
                                              ReferenceEquals(afterPayloadReplacement.State, FluidTankStorage.Ensure(item).Contents);
        result["payloadReplacementClock"] = FluidTankStorage.Ensure(item).OverpressureClock;
        result["payloadReplacementRuptured"] = FluidTankStorage.Ensure(item).Ruptured;
        byte[] replacementBytes = binary.BitData;
        FluidTankStorage.Flush(item);
        result["loadedPayloadFlushReusesBytes"] = ReferenceEquals(replacementBytes, binary.BitData);

        var replacementBinary = new Ex_ModData_MemoryPackable
        {
            ModuleId = Mod_FluidTank.ModuleId,
            StableName = binary.StableName
        };
        replacementBinary.WriteData(new FluidTankState());
        item.ModuleDataDic[binary.StableName] = replacementBinary;
        FluidTankStorage.TryGet(item, out FluidInventory afterBinaryReplacement, out _);
        result["binaryReplacementRebound"] = !ReferenceEquals(afterPayloadReplacement, afterBinaryReplacement) &&
                                             ReferenceEquals(afterBinaryReplacement.State, FluidTankStorage.Ensure(item).Contents);
        return result;
    }

    private static JObject WorldPortableTankPayload(JObject args)
    {
        FluidCatalog catalog = WorldFluidCatalog();
        FluidInventory inventory = WorldFilledFluid(catalog, 2m, 1m);
        ItemData item = WorldPortableTankItem(out Ex_ModData_MemoryPackable binary);
        double pressureBefore = inventory.GetPressureKPa(4d, .04d);
        FluidTankStorage.Write(item, inventory);
        FluidInventory decoded = new(binary.GetData<FluidTankState>().Contents, catalog);
        byte[] firstBytes = binary.BitData;
        var result = new JObject
        {
            ["writesOwnedState"] = ReferenceEquals(inventory.State, FluidTankStorage.Ensure(item).Contents),
            ["initialGasMoles"] = (double)decoded.GetGasMoles("system-probe:fluid"),
            ["initialLiquidMoles"] = (double)decoded.GetLiquidMoles("system-probe:fluid"),
            ["initialKelvin"] = decoded.GetTemperatureKelvin(),
            ["pressureBefore"] = pressureBefore,
            ["initialPressure"] = decoded.GetPressureKPa(4d, .04d)
        };
        FluidTankStorage.Write(item, inventory);
        result["unchangedWriteReusesBytes"] = ReferenceEquals(firstBytes, binary.BitData);
        result["taken"] = inventory.TryTakeExact("system-probe:fluid", FluidPhase.Gas, .5m, out FluidBatch batch);
        result["takenGasMoles"] = (double)batch.GasMoles;
        FluidTankStorage.Flush(item);
        byte[] changedBytes = binary.BitData;
        result["contentChangeWritesBytes"] = !ReferenceEquals(firstBytes, changedBytes);
        decoded = new FluidInventory(binary.GetData<FluidTankState>().Contents, catalog);
        result["remainingGasMoles"] = (double)decoded.GetGasMoles("system-probe:fluid");
        result["remainingLiquidMoles"] = (double)decoded.GetLiquidMoles("system-probe:fluid");
        result["remainingKelvin"] = decoded.GetTemperatureKelvin();
        result["remainingPressure"] = decoded.GetPressureKPa(4d, .04d);
        result["expectedPressure"] = inventory.GetPressureKPa(4d, .04d);
        FluidTankStorage.Flush(item);
        result["unchangedContentFlushReusesBytes"] = ReferenceEquals(changedBytes, binary.BitData);
        return result;
    }

    #endregion

    #region 冷机器地理温度的正式点采样
    // 点读与完整生成都调用生产入口，只返回真实环境层及查询值给 Lua 对照。
    private static JObject WorldColdTemperaturePath(JObject args)
    {
        var groups = new JArray();
        foreach (string mode in new[] { "simple", "legacy", "space", "uniform" })
        {
            var numbers = new Dictionary<string, double>
            {
                ["river.enabled"] = 0d, ["airHumidity.radiusTiles"] = 1d,
                ["airHumidity.waterContribution"] = 0d, ["climate.temperature.blendRadius"] = 2d
            };
            var texts = new Dictionary<string, string> { ["climate.algorithm"] = mode == "legacy" ? "legacy" : "simple" };
            if (mode is "space" or "uniform")
            {
                texts["space.bodyId"] = "system-probe:cold-body";
                numbers[mode == "space" ? "space.climateOverride" : "space.uniformSurface"] = 1d;
                numbers["space.surfaceTemperatureCelsius"] = -40d;
                numbers["space.temperatureVariationCelsius"] = 17d;
            }
            var profile = new ChunkGenerationProfileSnapshot("system-probe:cold-temperature-" + mode, 1, 4, 4,
                numbers, texts, ecologyGlobalMultiplier: 0d);
            var topology = new ChunkGenerationTopologySnapshot(new Int2(-16, -16), new Int2(32, 32));
            var address = new FlatWorld.WorldModel.WorldAddress(global::WorldAddress.SurfaceDimensionId, new Int2(12, -16));
            using var world = new WorldRuntime("system-probe:cold-temperature-" + mode, 1);
            var request = world.BeginChunkGeneration(address, 17413, profile, topology);
            var generator = new DeterministicChunkGenerator(terrainDecorator: SpaceSurfaceTerrain.Apply);
            using var generated = generator.Generate(request, CancellationToken.None);
            bool committed = world.TryCommit(generated, out string reason);
            var samples = new JArray();
            if (committed && world.TryGetChunkTerrain(address, out ChunkTerrainData terrain))
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int worldX = address.ChunkOrigin.X + x, worldY = address.ChunkOrigin.Y + y;
                    bool found = terrain.TryGetEnvironmentValue("temperature.celsius", x, y, out float full);
                    samples.Add(new JObject
                    {
                        ["found"] = found, ["full"] = full,
                        ["point"] = SpaceSurfaceQuery.SampleGeneratedTemperature(request, worldX, worldY),
                        ["wrapped"] = SpaceSurfaceQuery.SampleGeneratedTemperature(request, worldX + 32, worldY - 32)
                    });
                }
            SpaceSurfaceQuery.SampleGeneratedTemperature(request, 12, -16);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++) SpaceSurfaceQuery.SampleGeneratedTemperature(request, 12, -16);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            groups.Add(new JObject { ["mode"] = mode, ["committed"] = committed, ["reason"] = reason,
                ["samples"] = samples, ["pointReadBytes"] = allocated });
            generator.BeginNewWorldHydrologyEpoch(1);
        }
        return new JObject { ["groups"] = groups };
    }
    #endregion
}
