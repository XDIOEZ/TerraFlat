using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 食物计时与库存调度采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterFoodTimerStateRules()
        => Register("survival", "food_timer_state", CaptureFoodTimerState);

    // 只使用独立库存与正式调度器，记录计时、事务和快照的实际结果。
    private static JObject CaptureFoodTimerState(JObject args)
    {
        ItemData item = CreateFoodTimerFixture("probe:food_timer", 5f, 3f, out ModData_FoodData food);
        var firstSlot = new ItemSlot(0) { itemData = item };
        var otherSlot = new ItemSlot(0) { itemData = item };
        var first = new Inventory { Data = new Inventory_Data(new List<ItemSlot> { firstSlot }, "食物计时一") };
        var other = new Inventory { Data = new Inventory_Data(new List<ItemSlot> { otherSlot }, "食物计时二") };
        using var firstScheduler = new InventoryModuleDataScheduler(first);
        using var otherScheduler = new InventoryModuleDataScheduler(other);
        firstScheduler.Tick(2f);
        float initialSpoilage = FoodSpoilageObserverData.Load(food).SpoilageElapsedSeconds;
        float initialMelting = FoodMeltingObserverData.Load(food).MeltingElapsedSeconds;
        FoodMechanicStateData spoilageState = food.FindSpoilageState();
        FoodMechanicStateData meltingState = food.FindMeltingState();
        bool converted = spoilageState.Timer.Initialized && meltingState.Timer.Initialized &&
            !spoilageState.Data.ContainsKey("SpoilageElapsedSeconds") &&
            !meltingState.Data.ContainsKey("MeltingElapsedSeconds") && spoilageState.Data["extension"] == "kept";

        ModuleInstanceSnapshot snapshot = ModuleInstanceSnapshot.Capture(food);
        var restored = new ModData_FoodData { StableName = "food", ModuleId = ModText.Food };
        snapshot.RestoreTo(restored);
        var clone = (ModData_FoodData)ModuleRuntimeDataFactories.Compile(food)();
        FoodSpoilageObserverData cloneSpoilage = FoodSpoilageObserverData.Load(clone);
        cloneSpoilage.Advance(1f, clone);
        bool independentClone = FoodSpoilageObserverData.Load(food).SpoilageElapsedSeconds == initialSpoilage &&
            FoodSpoilageObserverData.Load(clone).SpoilageElapsedSeconds == initialSpoilage + 1f;
        bool fingerprintBeforeValid = ItemRuntimeStateFingerprint.TryCalculateModule(food, false,
            new HashSet<ItemData>(), out ulong fingerprintBefore);

        otherScheduler.Tick(2f);
        float sharedSpoilage = FoodSpoilageObserverData.Load(food).SpoilageElapsedSeconds;
        float sharedMelting = FoodMeltingObserverData.Load(food).MeltingElapsedSeconds;
        firstScheduler.Tick(2f);
        float afterSpoilage = FoodSpoilageObserverData.Load(food).SpoilageElapsedSeconds;
        float afterMelting = FoodMeltingObserverData.Load(food).MeltingElapsedSeconds;
        float afterAmount = item.Stack.Amount;
        bool fingerprintAfterValid = ItemRuntimeStateFingerprint.TryCalculateModule(food, false,
            new HashSet<ItemData>(), out ulong fingerprintAfter);
        food.Enabled = false;
        firstScheduler.Tick(1f);
        float disabledSpoilage = FoodSpoilageObserverData.Load(food).SpoilageElapsedSeconds;
        food.Enabled = true;
        firstScheduler.Tick(1f);
        float resumedSpoilage = FoodSpoilageObserverData.Load(food).SpoilageElapsedSeconds;

        ItemData disabledItem = CreateFoodTimerFixture("probe:disabled_food", 2f, 3f, out ModData_FoodData disabledFood);
        disabledFood.FindSpoilageState().Data["EnableSpoilage"] = "false";
        disabledFood.FindMeltingState().Data["EnableMelting"] = "false";
        var disabled = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
            { new(0) { itemData = disabledItem } }, "关闭食物规则") };
        using var disabledScheduler = new InventoryModuleDataScheduler(disabled);
        disabledScheduler.Tick(1f);
        bool disabledTimersConverted = disabledFood.FindSpoilageState().Timer.Initialized &&
            disabledFood.FindMeltingState().Timer.Initialized && disabledItem.Stack.Amount == 2f &&
            FoodSpoilageObserverData.Load(disabledFood).SpoilageElapsedSeconds == 0f &&
            FoodMeltingObserverData.Load(disabledFood).MeltingElapsedSeconds == 0f;

        ItemData worldDisabled = CreateFoodTimerFixture("probe:disabled_world_food", 1f, 3f, out ModData_FoodData worldDisabledFood);
        worldDisabledFood.FindSpoilageState().Data["EnableSpoilage"] = "false";
        bool worldDisabledEligible = FoodSpoilageObserverData.TryLoad(worldDisabled, out _, out _);
        bool worldDisabledConverted = worldDisabledFood.FindSpoilageState().Timer.Initialized && !worldDisabledEligible;

        ItemData firstItem = CreateFoodTimerFixture("probe:ordered_first", 2f, 1f, out ModData_FoodData firstFood);
        ItemData oldSecondItem = CreateFoodTimerFixture("probe:ordered_second", 2f, 3f, out ModData_FoodData oldSecondFood);
        var orderedFirstSlot = new ItemSlot(0) { itemData = firstItem };
        var orderedSecondSlot = new ItemSlot(1) { itemData = oldSecondItem };
        ItemData replacement = EntityProbeData("probe:ordered_replacement", 1f);
        var ordered = new Inventory { Data = new Inventory_Data(new List<ItemSlot> { orderedFirstSlot, orderedSecondSlot }, "食物事务顺序") };
        float spoilageAtMelt = -1f;
        ordered.Data.Event_OnBeforeDataChanged += slot =>
        {
            if (!ReferenceEquals(slot, orderedFirstSlot)) return;
            spoilageAtMelt = FoodSpoilageObserverData.Load(firstFood).SpoilageElapsedSeconds;
            orderedSecondSlot.itemData = replacement;
        };
        using var orderedScheduler = new InventoryModuleDataScheduler(ordered);
        orderedScheduler.Tick(1f);
        float staleSecondElapsed = FoodSpoilageObserverData.Load(oldSecondFood).SpoilageElapsedSeconds;

        ItemData retryItem = CreateFoodTimerFixture("probe:melting_retry", 2f, 1f, out ModData_FoodData retryFood);
        var foreignSlot = new ItemSlot(0) { itemData = retryItem };
        var wrongInventory = new Inventory_Data(new List<ItemSlot>(), "不含来源槽");
        new FoodMeltingModuleDataObserver().Step(new ModuleDataTickContext(retryFood, retryItem, wrongInventory,
            foreignSlot, 0, 1.5f));

        var indexFood = new ModData_FoodData();
        indexFood.MechanicStates.Add(new FoodMechanicStateData { StateKey = "probe.extension" });
        bool absentIndex = indexFood.FindMeltingState() == null;
        indexFood.MechanicStates[0] = new FoodMechanicStateData { StateKey = ModData_FoodData.MeltingStateKey };
        bool replacedIndex = indexFood.FindMeltingState() == indexFood.MechanicStates[0];
        indexFood.MechanicStates[0].StateKey = ModData_FoodData.SpoilageStateKey;
        bool renamedIndex = indexFood.FindMeltingState() == null && indexFood.FindSpoilageState() == indexFood.MechanicStates[0];

        var renamedFood = new ModData_FoodData();
        new FoodSpoilageObserverData { EnableSpoilage = true, SpoilageElapsedSeconds = 8f,
            SpoilageIntervalSeconds = 100f, SpoilageTargetItemID = "probe:rotten" }.Save(renamedFood);
        FoodMechanicStateData renamedState = renamedFood.FindSpoilageState();
        renamedState.StateKey = ModData_FoodData.MeltingStateKey;
        FoodMeltingObserverData renamedMelting = FoodMeltingObserverData.Load(renamedFood);
        renamedState.StateKey = ModData_FoodData.SpoilageStateKey;
        bool renamedTimerKind = !renamedMelting.EnableMelting && renamedMelting.MeltingIntervalSeconds == 300f &&
            FoodSpoilageObserverData.Load(renamedFood).SpoilageElapsedSeconds == 8f;

        FoodMechanicStateData cloneState = clone.FindSpoilageState();
        FoodObserverStateStore.WriteFloat(cloneState, "SpoilageElapsedSeconds", 9f);
        bool typedGenericWrite = FoodSpoilageObserverData.Load(clone).SpoilageElapsedSeconds == 9f &&
            FoodObserverStateStore.ReadFloat(cloneState, "SpoilageElapsedSeconds", 0f) == 9f;
        FoodObserverStateStore.WriteString(cloneState, "SpoilageElapsedSeconds", "7.5");
        bool genericStringWrite = FoodSpoilageObserverData.Load(clone).SpoilageElapsedSeconds == 7.5f;

        return new JObject
        {
            ["converted"] = converted, ["initialSpoilage"] = initialSpoilage, ["initialMelting"] = initialMelting,
            ["sharedSpoilage"] = sharedSpoilage, ["sharedMelting"] = sharedMelting,
            ["afterSpoilage"] = afterSpoilage, ["afterMelting"] = afterMelting, ["afterAmount"] = afterAmount,
            ["restoredSpoilage"] = FoodSpoilageObserverData.Load(restored).SpoilageElapsedSeconds,
            ["restoredMelting"] = FoodMeltingObserverData.Load(restored).MeltingElapsedSeconds,
            ["independentClone"] = independentClone,
            ["fingerprintChanged"] = fingerprintBeforeValid && fingerprintAfterValid && fingerprintBefore != fingerprintAfter,
            ["disabledSpoilage"] = disabledSpoilage, ["resumedSpoilage"] = resumedSpoilage,
            ["disabledTimersConverted"] = disabledTimersConverted, ["worldDisabledConverted"] = worldDisabledConverted,
            ["spoilageAtMelt"] = spoilageAtMelt, ["firstAmount"] = firstItem.Stack.Amount,
            ["replacementStayed"] = ReferenceEquals(orderedSecondSlot.itemData, replacement),
            ["staleSecondElapsed"] = staleSecondElapsed,
            ["retryElapsed"] = FoodMeltingObserverData.Load(retryFood).MeltingElapsedSeconds,
            ["retryAmount"] = retryItem.Stack.Amount,
            ["indexUpdated"] = absentIndex && replacedIndex && renamedIndex && renamedTimerKind,
            ["typedGenericWrite"] = typedGenericWrite, ["genericStringWrite"] = genericStringWrite
        };
    }

    private static ItemData CreateFoodTimerFixture(string id, float amount, float meltInterval, out ModData_FoodData food)
    {
        ItemData item = EntityProbeData(id, amount);
        food = new ModData_FoodData { StableName = "food", ModuleId = ModText.Food };
        food.MechanicStates.Add(new FoodMechanicStateData
        {
            StateKey = ModData_FoodData.SpoilageStateKey,
            Data = new Dictionary<string, string>
            {
                ["EnableSpoilage"] = "true", ["SpoilageElapsedSeconds"] = "0",
                ["SpoilageIntervalSeconds"] = "100", ["SpoilageTargetItemID"] = "probe:rotten", ["extension"] = "kept"
            }
        });
        food.MechanicStates.Add(new FoodMechanicStateData
        {
            StateKey = ModData_FoodData.MeltingStateKey,
            Data = new Dictionary<string, string>
            {
                ["EnableMelting"] = "true", ["MeltingElapsedSeconds"] = "0",
                ["MeltingIntervalSeconds"] = meltInterval.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        });
        item.ModuleDataDic.Add(food.StableName, food);
        return item;
    }

    #endregion
}
