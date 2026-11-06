using System;
using System.Collections.Generic;
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
        Register("processing", "soybean_press", CaptureSoybeanPress);
        Register("processing", "potato_oil_frying", CapturePotatoOilFrying);
    }

    // 夹具只设置坩埚初态，取油、反应、液化和满槽拒绝均调用正式加热入口。
    private static JObject CapturePetrochemicalCrucible(Item unusedPlayer, JObject args)
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

    // 坩埚场景走正式加热入口；效果异常单独覆盖通用事务回滚。
    private static JObject CaptureSoybeanPress(Item unusedPlayer, JObject args)
    {
        string mode = GameplayTestSystemProbe.Text(args, "mode", "normal");
        float temperature = (float)GameplayTestSystemProbe.Number(args, "temperature", 120d);
        if (mode == "effect_failure")
            return CaptureSoybeanPressTransaction(mode, temperature);

        ItemData crucible = CreateDragItem("Crucible");
        var solidStorage = (Ex_ModData_MemoryPackable)crucible.ModuleDataDic.Values
            .Single(module => module.ID == Mod_Mortar.ModuleId);
        var contents = new ItemData[6];
        contents[0] = ThermalMaterial("Soybean", mode == "solid_full" ? 5f : 4f, temperature);
        if (mode == "solid_full")
            for (int index = 1; index < contents.Length; index++)
                contents[index] = ThermalMaterial("StoneSlab", 1f, temperature);
        Inventory inventory = CreateDragInventory("lua.soybean.press.bowl", contents);
        solidStorage.WriteData(new MortarState { Bowl = inventory.Data });
        bool exactSoybeanInput = ItemReactionResolver.TryResolve(inventory,
            new CraftingCapabilities { RecipeType = RecipeType.Smelting }, out _,
            out CraftingRecipeMatch reactionMatch, requireLiquidOutput: true) &&
            reactionMatch.Recipe.inputs.RowItems_List.Count == 1 &&
            reactionMatch.Recipe.inputs.RowItems_List[0].matchMode == MatchMode.ExactItem &&
            reactionMatch.Recipe.inputs.RowItems_List[0].ItemName == "Soybean";
        GameRes.ExistingInstance.TryGetItemDefinition("SoybeanMeal", out RuntimeItemDefinition mealDefinition);
        bool mealHasPressReaction = mealDefinition?.Reactions.Any(candidate =>
            candidate?.Id == "core:soybean_oil_pressing") == true;
        if (mode == "liquid_full")
        {
            Mod_WaterVessel.TryRead(crucible, out _, out LiquidContainerState initialLiquid);
            float waterSpace = MixedLiquidContents.GetReceivableServings(initialLiquid,
                Mod_WaterVessel.ResolveContainerCapacity(crucible), "core:pure_water", float.MaxValue);
            if (!Mod_WaterVessel.TryAddLiquidToItemData(crucible, "core:pure_water", waterSpace, 20f))
                throw new InvalidOperationException("无法构造满液坩埚豆油测试状态。");
        }

        bool processed = Mod_Mortar.TryProcessCrucibleHeat(crucible, temperature, 3f);
        MortarState state = solidStorage.GetData<MortarState>();
        Mod_WaterVessel.TryRead(crucible, out _, out LiquidContainerState liquid);
        JArray solidsAfterFirstAttempt = ThermalSlots(new Inventory { Data = state.Bowl });
        float soybeanOilAfterFirstAttempt = MixedLiquidContents.GetAmount(liquid, "core:soybean_oil");
        bool blockedWithoutMutation = mode != "solid_full" ||
            solidsAfterFirstAttempt.Sum(slot => slot["id"]?.Value<string>() == "Soybean" ? slot["amount"].Value<float>() : 0f) == 5f &&
            solidsAfterFirstAttempt.All(slot => slot["id"]?.Value<string>() != "SoybeanMeal") &&
            soybeanOilAfterFirstAttempt == 0f;
        bool recovered = false;
        JArray recoverySolids = solidsAfterFirstAttempt;
        float recoveryOil = soybeanOilAfterFirstAttempt;
        if (mode == "solid_full")
        {
            state.Bowl.itemSlots[1].itemData = null;
            solidStorage.WriteData(state);
            recovered = Mod_Mortar.TryProcessCrucibleHeat(crucible, temperature, 3f);
            state = solidStorage.GetData<MortarState>();
            Mod_WaterVessel.TryRead(crucible, out _, out liquid);
            recoverySolids = ThermalSlots(new Inventory { Data = state.Bowl });
            recoveryOil = MixedLiquidContents.GetAmount(liquid, "core:soybean_oil");
        }
        return new JObject
        {
            ["mode"] = mode,
            ["processed"] = processed,
            ["solids"] = solidsAfterFirstAttempt,
            ["soybeanOil"] = soybeanOilAfterFirstAttempt,
            ["water"] = MixedLiquidContents.GetAmount(liquid, "core:pure_water"),
            ["blockedWithoutMutation"] = blockedWithoutMutation,
            ["recovered"] = recovered,
            ["recoverySolids"] = recoverySolids,
            ["recoverySoybeanOil"] = recoveryOil,
            ["exactSoybeanInput"] = exactSoybeanInput,
            ["mealHasPressReaction"] = mealHasPressReaction
        };
    }

    private static JObject CaptureSoybeanPressTransaction(string mode, float temperature)
    {
        Inventory input = CreateDragInventory("lua.soybean.press.transaction",
            ThermalMaterial("Soybean", 4f, temperature));
        if (!ItemReactionResolver.TryResolve(input, new CraftingCapabilities { RecipeType = RecipeType.Smelting },
                out RuntimeItemReactionDefinition reaction, out CraftingRecipeMatch match, requireLiquidOutput: true))
            throw new InvalidOperationException("正式大豆豆油反应无法匹配。");

        List<ItemData> solids = MaterialThermalProcessor.CreateThermalReactionSolidOutputs(input, match, 1, temperature);
        var liquid = new LiquidContainerState { Temperature = temperature };
        LiquidContainerState before = MixedLiquidContents.Clone(liquid);
        bool effectRan = false, failed = false;
        string failureMessage = string.Empty;
        CraftingResult result = null;
        try
        {
            result = CraftingService.ApplyInputEffect(input, match, solids,
                () =>
                {
                    effectRan = true;
                    MixedLiquidContents.Add(liquid, reaction.LiquidOutput.LiquidId,
                        reaction.LiquidOutput.Amount, temperature);
                    if (mode == "effect_failure") throw new InvalidOperationException("注入的豆油保存失败。");
                },
                () => MixedLiquidContents.Restore(liquid, before));
        }
        catch (System.Exception exception)
        {
            failed = true;
            failureMessage = exception.Message;
        }

        GameRes.ExistingInstance.TryGetItemDefinition("SoybeanMeal", out RuntimeItemDefinition mealDefinition);
        bool mealHasPressReaction = mealDefinition?.Reactions.Any(candidate =>
            candidate?.Id == "core:soybean_oil_pressing") == true;
        bool exactSoybeanInput = match.Recipe.inputs.RowItems_List.Count == 1 &&
            match.Recipe.inputs.RowItems_List[0].matchMode == MatchMode.ExactItem &&
            match.Recipe.inputs.RowItems_List[0].ItemName == "Soybean";
        float beans = input.Data.itemSlots.Sum(slot => slot?.itemData?.IDName == "Soybean"
            ? slot.itemData.Stack.Amount : 0f);
        float meal = input.Data.itemSlots.Sum(slot => slot?.itemData?.IDName == "SoybeanMeal"
            ? slot.itemData.Stack.Amount : 0f);
        return new JObject
        {
            ["mode"] = mode,
            ["committed"] = result?.Success == true,
            ["failureReason"] = result?.FailureReason.ToString() ?? string.Empty,
            ["failed"] = failed,
            ["effectRan"] = effectRan,
            ["soybeans"] = beans,
            ["meal"] = meal,
            ["soybeanOil"] = MixedLiquidContents.GetAmount(liquid, reaction.LiquidOutput.LiquidId),
            ["unchanged"] = beans == 4f && meal == 0f &&
                MixedLiquidContents.GetAmount(liquid, reaction.LiquidOutput.LiquidId) == 0f,
            ["failureMessage"] = failureMessage,
            ["exactSoybeanInput"] = exactSoybeanInput,
            ["mealHasPressReaction"] = mealHasPressReaction
        };
    }

    // 夹具设置坩埚液体和土豆，所有加热、扣油及烹饪都走正式坩埚入口。
    private static JObject CapturePotatoOilFrying(Item unusedPlayer, JObject args)
    {
        string mode = GameplayTestSystemProbe.Text(args, "mode", "normal");
        float temperature = (float)GameplayTestSystemProbe.Number(args, "temperature", 100d);
        ItemData crucible = CreateDragItem("Crucible");
        var solidStorage = (Ex_ModData_MemoryPackable)crucible.ModuleDataDic.Values
            .Single(module => module.ID == Mod_Mortar.ModuleId);
        float potatoCount = mode == "partial_oil" ? 2f : mode == "solid_full" ? 5f : 1f;
        var contents = new ItemData[6];
        contents[0] = ThermalMaterial("Potato", potatoCount, temperature);
        if (mode == "solid_full")
            for (int index = 1; index < contents.Length; index++)
                contents[index] = ThermalMaterial("StoneSlab", 1f, temperature);
        Inventory inventory = CreateDragInventory("lua.potato.oil.frying", contents);
        solidStorage.WriteData(new MortarState { Bowl = inventory.Data });

        if (mode != "no_oil")
        {
            if (!Mod_WaterVessel.TryAddLiquidToItemData(crucible, "core:pure_water", 2f, temperature))
                throw new InvalidOperationException("无法构造混液坩埚初态。");
            if (!Mod_WaterVessel.TryAddLiquidToItemData(crucible, "core:soybean_oil", 1f, temperature))
                throw new InvalidOperationException("无法构造豆油坩埚初态。");
        }
        if (mode == "reserved_oil")
        {
            if (!Mod_WaterVessel.TryRead(crucible, out Ex_ModData_MemoryPackable liquidStorage,
                    out LiquidContainerState reservedState))
                throw new InvalidOperationException("坩埚液体状态不可读取。");
            reservedState.PendingOutputs["lua.potato-oil.reservation"] = new LiquidPendingBatch
            { LiquidId = "core:soybean_oil", Servings = 1f };
            liquidStorage.WriteData(reservedState);
        }

        RuntimeItemReactionDefinition frying = ItemReactionResolver.GetCandidates(inventory, liquid: true)
            .FirstOrDefault(reaction => reaction.Id == "core:potato_oil_frying");
        bool exactFryingInput = frying?.LiquidInput?.LiquidId == "core:soybean_oil" &&
            frying.LiquidInput.Amount == 1f && frying.Recipe.inputs.RowItems_List.Count == 1 &&
            frying.Recipe.inputs.RowItems_List[0].matchMode == MatchMode.ExactItem &&
            frying.Recipe.inputs.RowItems_List[0].ItemName == "Potato" &&
            frying.Recipe.inputs.RowItems_List[0].amount == 1 &&
            frying.Recipe.outputs.results.Count == 1 &&
            frying.Recipe.outputs.results[0].ItemName == "FriedPotato" &&
            frying.Recipe.outputs.results[0].amount == 1 &&
            frying.MinTemperature == 100f && temperature == 100f;

        bool processed = Mod_Mortar.TryProcessCrucibleHeat(crucible, temperature, 3f);
        MortarState state = solidStorage.GetData<MortarState>();
        Mod_WaterVessel.TryRead(crucible, out _, out LiquidContainerState liquid);
        JArray firstSolids = ThermalSlots(new Inventory { Data = state.Bowl });
        float firstOil = MixedLiquidContents.GetAmount(liquid, "core:soybean_oil");
        float firstWater = MixedLiquidContents.GetAmount(liquid, "core:pure_water");
        float reservedOil = MixedLiquidContents.Reserved(liquid, "core:soybean_oil");

        bool blockedWithoutMutation = mode != "solid_full" ||
            SlotAmount(firstSolids, "Potato") == 5f && SlotAmount(firstSolids, "FriedPotato") == 0f &&
            SlotAmount(firstSolids, "BakedPotato") == 0f && firstOil == 1f && firstWater == 2f;
        bool recovered = false;
        JArray recoverySolids = firstSolids;
        float recoveryOil = firstOil;
        if (mode == "solid_full")
        {
            state.Bowl.itemSlots[1].itemData = null;
            solidStorage.WriteData(state);
            recovered = Mod_Mortar.TryProcessCrucibleHeat(crucible, temperature, 3f);
            state = solidStorage.GetData<MortarState>();
            Mod_WaterVessel.TryRead(crucible, out _, out liquid);
            recoverySolids = ThermalSlots(new Inventory { Data = state.Bowl });
            recoveryOil = MixedLiquidContents.GetAmount(liquid, "core:soybean_oil");
        }

        bool secondProcessed = false;
        JArray secondSolids = firstSolids;
        float secondOil = firstOil;
        float secondWater = firstWater;
        if (mode == "partial_oil")
        {
            secondProcessed = Mod_Mortar.TryProcessCrucibleHeat(crucible, temperature, 3f);
            state = solidStorage.GetData<MortarState>();
            Mod_WaterVessel.TryRead(crucible, out _, out liquid);
            secondSolids = ThermalSlots(new Inventory { Data = state.Bowl });
            secondOil = MixedLiquidContents.GetAmount(liquid, "core:soybean_oil");
            secondWater = MixedLiquidContents.GetAmount(liquid, "core:pure_water");
        }

        return new JObject
        {
            ["mode"] = mode,
            ["temperature"] = temperature,
            ["processed"] = processed,
            ["solids"] = firstSolids,
            ["soybeanOil"] = firstOil,
            ["water"] = firstWater,
            ["reservedSoybeanOil"] = reservedOil,
            ["blockedWithoutMutation"] = blockedWithoutMutation,
            ["recovered"] = recovered,
            ["recoverySolids"] = recoverySolids,
            ["recoverySoybeanOil"] = recoveryOil,
            ["secondProcessed"] = secondProcessed,
            ["secondSolids"] = secondSolids,
            ["secondSoybeanOil"] = secondOil,
            ["secondWater"] = secondWater,
            ["exactFryingInput"] = exactFryingInput
        };
    }

    private static float SlotAmount(JArray slots, string itemId)
        => slots.Where(slot => slot["id"]?.Value<string>() == itemId).Sum(slot => slot["amount"]?.Value<float>() ?? 0f);

    // 故障只注入持久化边界，产物提交、液体扣除和双侧回滚仍走生产实现。
    private static JObject CapturePetrochemicalPersistenceRollback(Item unusedPlayer, JObject args)
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
