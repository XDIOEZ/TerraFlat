using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestRuntimeProbe
{
    #region 坩埚石油化工真实入口采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterPetrochemicalCrucibleRuntimeProbe()
    {
        Register("processing", "petrochemical_crucible", CapturePetrochemicalCrucible);
        Register("processing", "petrochemical_persist_rollback", CapturePetrochemicalPersistenceRollback);
    }

    // 夹具只设置坩埚初态，取油、反应、液化和满槽拒绝均调用正式加热入口。
    private static JObject CapturePetrochemicalCrucible(Player unusedPlayer, JObject args)
    {
        string stage = GameplayTestSystemProbe.Text(args, "stage", "extraction");
        float temperature = (float)GameplayTestSystemProbe.Number(args, "temperature", 100);
        bool full = GameplayTestSystemProbe.Flag(args, "full", false);
        ItemData crucible = CreateDragItem("Crucible");
        var solidStorage = (Ex_ModData_MemoryPackable)crucible.ModuleDataDic.Values
            .Single(module => module.ID == Mod_Mortar.ModuleId);
        var contents = new ItemData[6];
        if (full)
            for (int index = 0; index < contents.Length; index++) contents[index] = ThermalMaterial("StoneSlab", 1, temperature);
        else if (stage == "distillation") contents[0] = ThermalMaterial("CrudeOil", 4, temperature);
        else if (stage == "cracking") contents[0] = ThermalMaterial("Naphtha", 1, temperature);
        else if (stage == "polymerization")
        {
            contents[0] = ThermalMaterial("Ethylene", 7, temperature);
            if (!GameplayTestSystemProbe.Flag(args, "missingCatalyst", false))
                contents[1] = ThermalMaterial("PolymerizationCatalyst", 1, temperature);
        }
        else if (stage == "fuel") contents[0] = ThermalMaterial("FuelOil", 1, temperature);
        Inventory inventory = CreateDragInventory("lua.petrochemical.crucible", contents);
        solidStorage.WriteData(new MortarState { Bowl = inventory.Data });
        if (stage == "extraction" && !Mod_WaterVessel.TryAddLiquidToItemData(crucible, "core:crude_oil", 4, temperature))
            throw new InvalidOperationException("正式坩埚拒绝石油初态。");
        bool processed = Mod_Mortar.TryProcessCrucibleHeat(crucible, temperature, 3);
        MortarState state = solidStorage.GetData<MortarState>();
        Mod_WaterVessel.TryRead(crucible, out _, out LiquidContainerState liquid);
        return new JObject { ["processed"] = processed, ["solids"] = ThermalSlots(new Inventory { Data = state.Bowl }),
            ["oil"] = MixedLiquidContents.GetAmount(liquid, "core:crude_oil"),
            ["fuel"] = MixedLiquidContents.GetAmount(liquid, "core:fuel_oil") };
    }

    // 故障只注入持久化边界，产物提交、液体扣除和双侧回滚仍走生产实现。
    private static JObject CapturePetrochemicalPersistenceRollback(Player unusedPlayer, JObject args)
    {
        var liquid = new LiquidContainerState { Temperature = 100f };
        MixedLiquidContents.Add(liquid, "core:crude_oil", 4f, 100f);
        LiquidContainerState saved = MixedLiquidContents.Clone(liquid);
        Inventory output = CreateDragInventory("lua.petrochemical.persistence", new ItemData[1]);
        int calls = 0, failureCount = (int)GameplayTestSystemProbe.Number(args, "failures", 1);
        Exception firstFailure = null, caught = null;
        void Persist()
        {
            calls++;
            if (calls <= failureCount)
            {
                if (GameplayTestSystemProbe.Flag(args, "writeThenFail", false)) saved = MixedLiquidContents.Clone(liquid);
                var failure = new InvalidOperationException("petrochemical persist failure " + calls);
                firstFailure ??= failure;
                throw failure;
            }
            saved = MixedLiquidContents.Clone(liquid);
        }
        try { InventoryVesselHeating.ProcessContainedLiquidHeat(liquid, output, 100f, 3f, Persist); }
        catch (Exception error) { caught = error; }
        var result = new JObject { ["failed"] = caught != null, ["persistCalls"] = calls,
            ["originalPreserved"] = ReferenceEquals(caught, firstFailure) ||
                caught is AggregateException aggregate && aggregate.InnerExceptions.Contains(firstFailure),
            ["outputAfterFailure"] = ThermalSlots(output),
            ["liquidAfterFailure"] = MixedLiquidContents.GetAmount(liquid, "core:crude_oil"),
            ["savedAfterFailure"] = MixedLiquidContents.GetAmount(saved, "core:crude_oil") };
        InventoryVesselHeating.ProcessContainedLiquidHeat(liquid, output, 100f, 3f,
            () => saved = MixedLiquidContents.Clone(liquid));
        result["retryOutput"] = ThermalSlots(output);
        result["retryLiquid"] = liquid.Amount;
        result["retrySaved"] = saved.Amount;
        return result;
    }
    #endregion
}
