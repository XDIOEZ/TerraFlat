using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 营养与燃烧数值采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterNutritionAndCombustionConsumptionDataRules()
    {
        Register("survival", "nutrition_drain_batch", NutritionDrainBatch);
        Register("item_runtime", "combustion_fuel_batch", CombustionFuelBatch);
        Register("item_runtime", "material_fuel_state", MaterialFuelReferenceState);
    }

    private sealed class NutritionDrainProbeContext : IFoodRuntimeContext
    {
        public Item Item => null;
        public ItemData ItemData => null;
        public Food Data { get; set; }
        public ModData_FoodData PersistentData => null;
        public float EatingProgress { get; set; }
        public bool ConsumptionEnabled => true;
        public string ConsumeCompleteReplacementItemID => string.Empty;
        public FoodConsumeKind ConsumeKind => FoodConsumeKind.Solid;
        public bool IsPlayer { get; set; }
        public IFoodItemOperationGateway ItemOperations => null;
    }

    private static FoodNutritionConsumptionData.Sample NutritionDrainSample(float seconds, float nutritionSpeed, float waterSpeed)
    {
        return new FoodNutritionConsumptionData.Sample
        {
            State = new FoodNutritionConsumptionData.State
            {
                Carbohydrates = 10f, Fat = 5f, Protein = 3f, Water = 20f, Vitamins = 10f
            },
            Input = new FoodNutritionConsumptionData.Input
            {
                Seconds = seconds, NutritionSpeed = nutritionSpeed, WaterSpeed = waterSpeed,
                RuntimeNutritionMultiplier = 1f, MovementNutritionMultiplier = 1f, BuffNutritionMultiplier = 1f,
                MovementWaterMultiplier = 1f, BuffWaterMultiplier = 1f, PlayerHungerMultiplier = 1f
            }
        };
    }

    private static JObject NutritionDrainResult(FoodNutritionConsumptionData.Result result)
    {
        return new JObject
        {
            ["carbohydrates"] = result.State.Carbohydrates, ["fat"] = result.State.Fat,
            ["protein"] = result.State.Protein, ["water"] = result.State.Water,
            ["vitamins"] = result.State.Vitamins, ["energy"] = result.ConsumedEnergy
        };
    }

    private static bool SameNutritionDrainResult(FoodNutritionConsumptionData.Result left, FoodNutritionConsumptionData.Result right)
    {
        return left.ConsumedEnergy == right.ConsumedEnergy &&
               left.State.Carbohydrates == right.State.Carbohydrates && left.State.Fat == right.State.Fat &&
               left.State.Protein == right.State.Protein && left.State.Water == right.State.Water &&
               left.State.Vitamins == right.State.Vitamins;
    }

    private static JObject NutritionDrainServiceSample(bool isPlayer)
    {
        var context = new NutritionDrainProbeContext
        {
            IsPlayer = isPlayer,
            Data = new Food
            {
                nutrition = new Nutrition(100f, 100f, 100f, 100f, 100f),
                nutritionConsumeSpeed = new GameValue_float(1f), WaterConsumeSpeedRate = .5f
            }
        };
        Nutrition original = context.Data.nutrition;
        var service = new FoodNutritionService(context) { RuntimeNutritionConsumeMultiplier = 2f };
        service.SetMovementNutritionConsumeMultiplier(1.5f);
        service.SetMovementWaterConsumeMultiplier(.5f);
        service.MultiplyRuntimeNutritionConsumeSpeed(.5f);
        service.MultiplyRuntimeWaterConsumeSpeed(2f);
        float energy = service.ConsumeNutrition(4f);
        return new JObject
        {
            ["carbohydrates"] = context.Data.nutrition.Carbohydrates, ["fat"] = context.Data.nutrition.Fat,
            ["protein"] = context.Data.nutrition.Protein, ["water"] = context.Data.nutrition.Water,
            ["vitamins"] = context.Data.nutrition.Vitamins, ["energy"] = energy,
            ["sameNutrition"] = ReferenceEquals(original, context.Data.nutrition)
        };
    }

    // 只采集正式单次、连续数组和真实食物服务的结果，业务断言由 Lua 持有。
    private static JObject NutritionDrainBatch(JObject args)
    {
        var samples = new FoodNutritionConsumptionData.Sample[10];
        samples[0].Result.ConsumedEnergy = -1f;
        samples[9].Result.ConsumedEnergy = -1f;
        samples[1] = NutritionDrainSample(2f, 6f, .5f);
        samples[1].Input.MovementWaterMultiplier = 2f;
        samples[1].Input.BuffWaterMultiplier = 3f;
        samples[2] = NutritionDrainSample(4f, 1f, .5f);
        samples[2].Input.RuntimeNutritionMultiplier = 2f;
        samples[2].Input.MovementNutritionMultiplier = 1.5f;
        samples[2].Input.BuffNutritionMultiplier = .5f;
        samples[2].Input.MovementWaterMultiplier = .5f;
        samples[2].Input.BuffWaterMultiplier = 2f;
        samples[2].Input.IsPlayer = true;
        samples[2].Input.PlayerHungerMultiplier = 2f;
        samples[3] = samples[2];
        samples[3].Input.IsPlayer = false;
        samples[4] = NutritionDrainSample(60f, 2f, .5f);
        samples[5] = NutritionDrainSample(0f, 1f, 1f);
        samples[6] = NutritionDrainSample(-1f, 1f, 1f);
        samples[7] = NutritionDrainSample(2f, -2f, -.5f);
        samples[8] = NutritionDrainSample(1f, 1f, 1f);
        samples[8].Input.RuntimeNutritionMultiplier = 0f;
        samples[8].State.Water = -2f;
        samples[8].State.Vitamins = -1f;
        var singles = new FoodNutritionConsumptionData.Result[8];
        for (int i = 0; i < singles.Length; i++)
            singles[i] = FoodNutritionConsumptionData.CalculateNutritionConsumption(samples[i + 1].State, samples[i + 1].Input);
        FoodNutritionConsumptionData.CalculateNutritionConsumptionBatch(new Span<FoodNutritionConsumptionData.Sample>(samples, 1, 8));
        FoodNutritionConsumptionData.CalculateNutritionConsumptionBatch(Span<FoodNutritionConsumptionData.Sample>.Empty);
        bool matchesSingle = true;
        var results = new JArray();
        for (int i = 0; i < singles.Length; i++)
        {
            matchesSingle &= SameNutritionDrainResult(samples[i + 1].Result, singles[i]);
            results.Add(NutritionDrainResult(samples[i + 1].Result));
        }
        return new JObject
        {
            ["samples"] = results, ["matchesSingle"] = matchesSingle,
            ["sliceUntouched"] = samples[0].Result.ConsumedEnergy == -1f && samples[9].Result.ConsumedEnergy == -1f,
            ["player"] = NutritionDrainServiceSample(true), ["other"] = NutritionDrainServiceSample(false),
            ["hungerMultiplier"] = GameRuleConfiguration.Current.PlayerSurvival.HungerDrainMultiplier
        };
    }

    private static CombustionFuelDemandData.Sample CombustionFuelSample(float previous, float current, float multiplier)
    {
        return new CombustionFuelDemandData.Sample
        {
            State = new CombustionFuelDemandData.State { LastWorldTime = previous },
            Input = new CombustionFuelDemandData.Input { CurrentWorldTime = current, FuelConsumptionMultiplier = multiplier }
        };
    }

    private static JObject CombustionFuelResult(CombustionFuelDemandData.Result result)
    {
        return new JObject
        {
            ["last"] = result.State.LastWorldTime, ["elapsed"] = result.ElapsedWorldTime,
            ["demand"] = result.FuelDemand
        };
    }

    private static JObject CombustionFuelBatch(JObject args)
    {
        var samples = new CombustionFuelDemandData.Sample[8];
        samples[0].Result.FuelDemand = -1f;
        samples[7].Result.FuelDemand = -1f;
        samples[1] = CombustionFuelSample(100f, 105f, 2f);
        samples[2] = CombustionFuelSample(100f, 100f, 2f);
        samples[3] = CombustionFuelSample(100f, 90f, 2f);
        samples[4] = CombustionFuelSample(90f, 95f, 0f);
        samples[5] = CombustionFuelSample(20f, 30f, -1f);
        samples[6] = CombustionFuelSample(20f, 12000f, .5f);
        var singles = new CombustionFuelDemandData.Result[6];
        for (int i = 0; i < singles.Length; i++)
            singles[i] = CombustionFuelDemandData.CalculateWorldTimeFuelDemand(samples[i + 1].State, samples[i + 1].Input);
        CombustionFuelDemandData.CalculateWorldTimeFuelDemandBatch(new Span<CombustionFuelDemandData.Sample>(samples, 1, 6));
        CombustionFuelDemandData.CalculateWorldTimeFuelDemandBatch(Span<CombustionFuelDemandData.Sample>.Empty);
        bool matchesSingle = true;
        var results = new JArray();
        for (int i = 0; i < singles.Length; i++)
        {
            var result = samples[i + 1].Result;
            matchesSingle &= result.State.LastWorldTime == singles[i].State.LastWorldTime &&
                             result.ElapsedWorldTime == singles[i].ElapsedWorldTime && result.FuelDemand == singles[i].FuelDemand;
            results.Add(CombustionFuelResult(result));
        }

        var root = new GameObject("GameplayTest Combustion Fuel Probe") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            var fuel = root.AddComponent<GameplayTestFuelWithInitializedData>();
            fuel.Data.Fuel = new Vector2(1000f, 10f);
            fuel.burnSpeedMultiplier = 1.5f;
            bool consumed = fuel.ConsumeFuel(samples[1].Result.FuelDemand);
            return new JObject
            {
                ["samples"] = results, ["matchesSingle"] = matchesSingle,
                ["sliceUntouched"] = samples[0].Result.FuelDemand == -1f && samples[7].Result.FuelDemand == -1f,
                ["fuelConsumed"] = consumed, ["remainingFuel"] = fuel.Data.Fuel.x,
                ["fuelCapacity"] = fuel.Data.Fuel.y, ["hasFuel"] = fuel.HasFuel(),
                ["fuelRuleMultiplier"] = GameRuleConfiguration.Current.Production.FuelConsumptionMultiplier
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // 不加载世界资源，检查燃尽条件与冷热实例对同一燃料载荷的读写。
    private static JObject MaterialFuelReferenceState(JObject args)
    {
        var matter = ItemMatterReactionCompiler.CompileMatter(new ItemMatterDefinitionDto
        {
            IgnitionTemperature = 450f,
            Transitions = new() { new() { Id = "probe.burnout", OutputItemId = "WoodAsh", RequiresFuelExhaustion = true } }
        }, new[] { "Wood" }, "probe:reusable-fuel");
        var material = new ItemMatterState { Initialized = true, TemperatureCelsius = 500f };
        var storage = new Ex_ModData_MemoryPackable { StableName = "fuel", ModuleId = ModText.Fuel };
        storage.WriteRuntimeState(new FuelData { Fuel = new Vector2(50f, 50f), MaxTemperature = 500f });
        ItemData source = new Data_GeneralItem { IDName = "probe:reusable-fuel", Stack = new ItemStack { Amount = 3f } };
        source.ModuleDataDic[storage.StableName] = storage;
        Mod_Fuel.TryResolveItemData(source, out FuelData first);
        Mod_Fuel.TryResolveItemData(source, out FuelData second);
        Mod_Fuel.ConsumeItemDataFuel(source, 10f);
        var clone = FastCloner.FastCloner.DeepClone(storage);
        FuelData cloneFuel = clone.ReadRuntimeState<FuelData>();
        cloneFuel.Fuel.x -= 5f;
        clone.WriteRuntimeState(cloneFuel);
        ModuleInstanceSnapshot snapshot = ModuleInstanceSnapshot.Capture(storage);
        var restored = new Ex_ModData_MemoryPackable { StableName = "fuel", ModuleId = ModText.Fuel };
        snapshot.RestoreTo(restored);
        var root = new GameObject("GameplayTest Material Fuel State") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            var module = root.AddComponent<GameplayTestFuelWithInitializedData>();
            module.Data.Fuel = new Vector2(100.23f, 100.23f);
            module.ApplyResourceConfiguration("probe:reusable-fuel", "fuel",
                "{\"Data\":{\"Fuel\":{\"x\":50,\"y\":50},\"MaxTemperature\":500}}");
            float freshFuel = module.Data.Fuel.x;
            module.ApplyResourceConfiguration("probe:reused-fuel", "fuel",
                "{\"Data\":{\"Fuel\":{\"x\":20,\"y\":20},\"MaxTemperature\":300}}");
            float reusedFuel = module.Data.Fuel.x;
            module._Data = storage;
            module.ApplyResourceConfiguration("probe:reusable-fuel", "fuel",
                "{\"Data\":{\"Fuel\":{\"x\":50,\"y\":50},\"MaxTemperature\":500}}");
            float configuredSavedFuel = module.Data.Fuel.x;
            bool hotColdShare = ReferenceEquals(module.Data, first);
            var replacement = new Ex_ModData_MemoryPackable();
            replacement.WriteData(new FuelData { Fuel = new Vector2(7f, 50f) });
            storage.BitData = replacement.BitData;
            module._Data = storage;
            Mod_Fuel.TryResolveItemData(source, out FuelData replaced);
            return new JObject
            {
                ["configured"] = matter.HasFuelExhaustionTransition && matter.Transitions.Count == 1,
                ["freshFuel"] = freshFuel, ["reusedFuel"] = reusedFuel,
                ["configuredSavedFuel"] = configuredSavedFuel,
                ["blockedUntilExhausted"] = ItemMatterRuntime.SelectMatchedTransition(matter, material, false) == null,
                ["exhaustedOutput"] = ItemMatterRuntime.SelectMatchedTransition(matter, material, false, true)?.OutputItemId,
                ["sharedReference"] = ReferenceEquals(first, second) && hotColdShare,
                ["remaining"] = first.Fuel.x, ["amount"] = source.Stack.Amount,
                ["cloneRemaining"] = cloneFuel.Fuel.x, ["snapshotRemaining"] = restored.ReadRuntimeState<FuelData>().Fuel.x,
                ["replacementRemaining"] = replaced.Fuel.x, ["reboundReference"] = ReferenceEquals(module.Data, replaced)
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    #endregion
}

// 编辑态组件创建前准备燃料载荷，沿用正式燃料扣减方法。
[AddComponentMenu("")]
internal sealed class GameplayTestFuelWithInitializedData : Mod_Fuel
{
    #region 燃料探针初始化

    public GameplayTestFuelWithInitializedData()
    {
        ExData = new Ex_ModData_MemoryPackable { ModuleId = ModText.Fuel };
    }

    #endregion
}
