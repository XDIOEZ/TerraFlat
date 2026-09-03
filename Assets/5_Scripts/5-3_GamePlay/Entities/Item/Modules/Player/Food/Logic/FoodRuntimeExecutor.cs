using DG.Tweening;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 食物模块的运行时上下文。它把持久化数据、运行时物品和外部操作网关组合起来，
/// 但不把库存实现细节泄漏给 Food 数据结构。
/// </summary>
public sealed class FoodRuntimeContext : IFoodRuntimeContext
{
    private readonly Func<bool> readConsumptionEnabled;
    private readonly Func<string> readReplacementItemID;
    private readonly Func<FoodConsumeKind> readConsumeKind;
    private readonly Action stateChanged;
    private readonly Action<FoodConsumeResult> consumed;

    public FoodRuntimeContext(
        Item item,
        ModData_FoodData persistentData,
        Func<bool> readConsumptionEnabled,
        Func<string> readReplacementItemID,
        Func<FoodConsumeKind> readConsumeKind,
        Action stateChanged,
        Action<FoodConsumeResult> consumed,
        IFoodItemOperationGateway itemOperations = null)
    {
        Item = item;
        PersistentData = persistentData ?? new ModData_FoodData();
        this.readConsumptionEnabled = readConsumptionEnabled;
        this.readReplacementItemID = readReplacementItemID;
        this.readConsumeKind = readConsumeKind;
        this.stateChanged = stateChanged;
        this.consumed = consumed;
        ItemOperations = itemOperations ?? new InventoryFoodItemOperationGateway();
    }

    public Item Item { get; }
    public ItemData ItemData => Item?.itemData;
    public Food Data => PersistentData.EnsureFoodData();
    public ModData_FoodData PersistentData { get; }
    public IFoodItemOperationGateway ItemOperations { get; }
    internal FoodRulePipeline RulePipeline { get; set; }

    public float EatingProgress
    {
        get => FoodObserverStateStore.ReadFloat(
            FoodObserverStateStore.Find(PersistentData, FoodObserverStateStore.ConsumptionStateKey),
            "EatingProgress",
            0f);
        set
        {
            FoodMechanicStateData state = FoodObserverStateStore.GetOrCreate(
                PersistentData,
                FoodObserverStateStore.ConsumptionStateKey);
            FoodObserverStateStore.WriteFloat(state, "EatingProgress", Mathf.Max(0f, value));
        }
    }

    public bool ConsumptionEnabled => readConsumptionEnabled == null || readConsumptionEnabled();

    public string ConsumeCompleteReplacementItemID => readReplacementItemID?.Invoke() ?? string.Empty;

    public FoodConsumeKind ConsumeKind => readConsumeKind != null
        ? readConsumeKind()
        : FoodConsumeKind.Solid;

    public bool IsPlayer => GameDifficultyService.IsPlayer(Item);

    internal void NotifyStateChanged()
    {
        FoodStateChangedContext stateContext = new FoodStateChangedContext(this);
        RulePipeline?.OnStateChanged(stateContext);
        stateChanged?.Invoke();
    }

    internal void NotifyConsumed(FoodConsumeResult result)
    {
        RulePipeline?.OnConsumed(result);
        consumed?.Invoke(result);
    }

    internal void NotifyConsumedRulesOnly(FoodConsumeResult result)
    {
        RulePipeline?.OnConsumed(result);
    }
}

/// <summary>给外部自定义上下文提供统一状态通知默认行为。</summary>
internal static class FoodRuntimeContextExtensions
{
    public static void NotifyStateChanged(this IFoodRuntimeContext context)
    {
        if (context is FoodRuntimeContext runtimeContext)
            runtimeContext.NotifyStateChanged();
    }

    public static void NotifyConsumed(this IFoodRuntimeContext context, FoodConsumeResult result)
    {
        if (context is FoodRuntimeContext runtimeContext)
            runtimeContext.NotifyConsumed(result);
    }
}

/// <summary>
/// 营养结算服务：只负责倍率、营养消耗、进食营养合并和上限规则。
/// </summary>
public sealed class FoodNutritionService
{
    private readonly IFoodRuntimeContext context;
    private float runtimeNutritionConsumeMultiplier = 1f;
    private float movementNutritionConsumeMultiplier = 1f;
    private float movementWaterConsumeMultiplier = 1f;
    private float buffNutritionConsumeMultiplier = 1f;
    private float buffWaterConsumeMultiplier = 1f;

    private const float PlayerInitialFatMaximum = 100f;
    private const float PlayerNaturalVitaminLossPerSecond = 0.001f;
    private const float DefaultNaturalVitaminLossPerSecond = 0.01f;
    private const float PlayerFatMaximumCap = PlayerInitialFatMaximum * 2f;
    private const float PlayerFatMaximumGrowthRatio = 0.5f;

    public FoodNutritionService(IFoodRuntimeContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public float RuntimeNutritionConsumeMultiplier
    {
        get => runtimeNutritionConsumeMultiplier;
        set => runtimeNutritionConsumeMultiplier = Mathf.Max(0f, value);
    }

    public float MovementNutritionConsumeMultiplier => movementNutritionConsumeMultiplier;
    public float MovementWaterConsumeMultiplier => movementWaterConsumeMultiplier;
    public float BuffNutritionConsumeMultiplier => buffNutritionConsumeMultiplier;
    public float BuffWaterConsumeMultiplier => buffWaterConsumeMultiplier;

    /// <summary>基础营养配置存在持续消耗时才需要周期推进。</summary>
    public bool RequiresFoodTick => context.Data?.nutrition != null &&
        context.Data.nutritionConsumeRate > 0f;

    public void SetMovementNutritionConsumeMultiplier(float multiplier)
    {
        if (!IsFiniteNonNegative(multiplier))
        {
            Debug.LogWarning($"[FoodNutrition] 忽略无效移动营养消耗倍率：{multiplier}");
            return;
        }

        movementNutritionConsumeMultiplier = multiplier;
    }

    public void SetMovementWaterConsumeMultiplier(float multiplier)
    {
        if (!IsFiniteNonNegative(multiplier))
        {
            Debug.LogWarning($"[FoodNutrition] 忽略无效移动水分消耗倍率：{multiplier}");
            return;
        }

        movementWaterConsumeMultiplier = multiplier;
    }

    public void MultiplyRuntimeNutritionConsumeSpeed(float multiplier)
    {
        if (!IsFinitePositive(multiplier))
        {
            Debug.LogWarning($"[FoodNutrition] 忽略无效营养消耗倍率：{multiplier}");
            return;
        }

        buffNutritionConsumeMultiplier = Mathf.Clamp(
            buffNutritionConsumeMultiplier * multiplier,
            0.01f,
            100f);
    }

    public void MultiplyRuntimeWaterConsumeSpeed(float multiplier)
    {
        if (!IsFinitePositive(multiplier))
        {
            Debug.LogWarning($"[FoodNutrition] 忽略无效水分消耗倍率：{multiplier}");
            return;
        }

        buffWaterConsumeMultiplier = Mathf.Clamp(
            buffWaterConsumeMultiplier * multiplier,
            0.01f,
            100f);
    }

    public float ConsumeNutrition(float timeDelta)
    {
        Food food = context.Data;
        if (food == null || food.nutrition == null || timeDelta <= 0f)
            return 0f;

        timeDelta *= RuntimeNutritionConsumeMultiplier *
                     MovementNutritionConsumeMultiplier *
                     BuffNutritionConsumeMultiplier;

        if (context.IsPlayer)
            timeDelta *= GameDifficultyService.Current.PlayerSurvival.HungerDrainMultiplier;

        GameValue_float nutritionConsumeSpeed = food.nutritionConsumeSpeed;
        float foodDelta = timeDelta * (nutritionConsumeSpeed?.Value ?? 0f);
        float remainingDelta = Mathf.Max(0f, foodDelta);
        float totalEnergy = Drain(ref food.nutrition.Carbohydrates, ref remainingDelta);
        totalEnergy += Drain(ref food.nutrition.Fat, ref remainingDelta);
        totalEnergy += Drain(ref food.nutrition.Protein, ref remainingDelta);

        float usedWater = timeDelta * food.WaterConsumeSpeedRate *
                          MovementWaterConsumeMultiplier *
                          BuffWaterConsumeMultiplier;
        food.nutrition.Water = Mathf.Max(0f, food.nutrition.Water - usedWater);

        float naturalVitaminLossPerSecond = context.IsPlayer
            ? PlayerNaturalVitaminLossPerSecond
            : DefaultNaturalVitaminLossPerSecond;
        float naturalVitaminLoss = timeDelta * naturalVitaminLossPerSecond;
        food.nutrition.Vitamins = Mathf.Max(0f, food.nutrition.Vitamins - naturalVitaminLoss);
        return totalEnergy;
    }

    private static float Drain(ref float value, ref float amount)
    {
        float used = Mathf.Min(Mathf.Max(0f, value), amount);
        value -= used;
        amount -= used;
        return used;
    }

    public void RestoreNutritionToMaximum()
    {
        if (context.Data?.nutrition == null)
            return;

        context.Data.nutrition.Max();
        context.NotifyStateChanged();
    }

    public void ApplyConsumedNutrition(IFoodRuntimeContext consumer, IFoodRuntimeContext consumedFood)
    {
        if (consumer?.Data?.nutrition == null || consumedFood?.Data?.nutrition == null)
            return;

        float fatBefore = consumer.Data.nutrition.Fat;
        float fatMaximumBefore = consumer.Data.nutrition.Max_Fat;
        float consumedFat = Mathf.Max(0f, consumedFood.Data.nutrition.Fat);
        bool playerFatWasFull = consumer.IsPlayer &&
            consumedFat > 0f &&
            fatBefore >= fatMaximumBefore - 0.001f;
        float consumedWater = Mathf.Max(0f, consumedFood.Data.nutrition.Water);
        float waterBefore = consumer.Data.nutrition.Water;

        consumer.Data.nutrition = consumer.Data.nutrition + consumedFood.Data.nutrition;

        if (playerFatWasFull)
        {
            float availableFatMaximumGrowth = Mathf.Max(0f, PlayerFatMaximumCap - fatMaximumBefore);
            float fatMaximumGrowth = Mathf.Min(
                consumedFat * PlayerFatMaximumGrowthRatio,
                availableFatMaximumGrowth);
            consumer.Data.nutrition.Max_Fat = fatMaximumBefore + fatMaximumGrowth;
        }

        float actualWaterGain = Mathf.Max(0f, consumer.Data.nutrition.Water - waterBefore);
        FoodConsumeResult result = new FoodConsumeResult(
            consumer.Item,
            consumedFood.Item,
            consumedFood.ConsumeKind,
            consumedWater,
            actualWaterGain);
        consumer.NotifyConsumed(result);
        if (!ReferenceEquals(consumer, consumedFood))
        {
            if (consumedFood is FoodRuntimeContext consumedRuntime)
                consumedRuntime.NotifyConsumedRulesOnly(result);
        }
        consumer.NotifyStateChanged();
    }

    public void ResetRuntimeMultipliers()
    {
        runtimeNutritionConsumeMultiplier = 1f;
        movementNutritionConsumeMultiplier = 1f;
        movementWaterConsumeMultiplier = 1f;
        buffNutritionConsumeMultiplier = 1f;
        buffWaterConsumeMultiplier = 1f;
    }

    private static bool IsFiniteNonNegative(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    }

    private static bool IsFinitePositive(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }
}

/// <summary>进食次数、完成判断和营养结算服务。</summary>
public static class FoodConsumptionService
{
    public static bool TryConsume(
        IFoodRuntimeContext source,
        IFoodRuntimeContext consumer,
        FoodRulePipeline rulePipeline,
        FoodNutritionService consumerNutrition)
    {
        if (source == null || consumer == null || consumerNutrition == null ||
            rulePipeline == null ||
            !source.ConsumptionEnabled || source.ItemData?.Stack == null || source.Data == null ||
            source.Data.Max_EatingProgress <= 0f)
            return false;

        FoodUseContext useContext = new FoodUseContext(source, consumer);
        if (!rulePipeline.CanUse(useContext))
            return false;

        rulePipeline.OnUse(useContext);
        source.EatingProgress += 1f;
        source.NotifyStateChanged();

        if (source.EatingProgress < source.Data.Max_EatingProgress)
            return true;

        bool operationSucceeded;
        if (!string.IsNullOrWhiteSpace(source.ConsumeCompleteReplacementItemID))
        {
            operationSucceeded = source.ItemOperations.TryReplaceCurrentItem(
                source,
                source.ConsumeCompleteReplacementItemID,
                out string replacementReason);
            if (!operationSucceeded)
            {
                source.EatingProgress = 0f;
                source.NotifyStateChanged();
                Debug.LogWarning(
                    $"[FoodConsumption] 完成食用替换失败，物品={source.ItemData?.IDName}，原因={replacementReason}");
                return false;
            }
        }
        else
        {
            operationSucceeded = source.ItemOperations.TryConsumeOne(
                source,
                out _,
                out string consumeReason);
            if (!operationSucceeded)
            {
                source.EatingProgress = 0f;
                source.NotifyStateChanged();
                Debug.LogWarning(
                    $"[FoodConsumption] 完成食用扣除失败，物品={source.ItemData?.IDName}，原因={consumeReason}");
                return false;
            }
        }

        source.EatingProgress = 0f;
        source.NotifyStateChanged();
        consumerNutrition.ApplyConsumedNutrition(consumer, source);
        return true;
    }
}

/// <summary>
/// 食物生存规则：只处理体力恢复和体力对应的营养消耗。
/// </summary>
public sealed class FoodSurvivalRule : IFoodMechanic, IFoodTickObserver, IFoodTickRequirement
{
    private readonly FoodNutritionService nutritionService;
    private readonly Mod_Stamina stamina;
    private readonly Mod_Food.FoodStaminaState staminaState;

    public string MechanicId => "core.survival";
    public int Priority => 90;

    /// <summary>只有绑定体力模块的角色才需要推进生存规则。</summary>
    public bool RequiresFoodTick => stamina?.Data != null;

    public FoodSurvivalRule(
        FoodNutritionService nutritionService,
        Mod_Stamina stamina,
        Mod_Food.FoodStaminaState staminaState)
    {
        this.nutritionService = nutritionService;
        this.stamina = stamina;
        this.staminaState = staminaState ?? new Mod_Food.FoodStaminaState();
    }

    public void OnFoodTick(FoodTickContext tickContext)
    {
        UpdateStamina(tickContext.DeltaTime);
    }

    private void UpdateStamina(float timeDelta)
    {
        if (stamina?.Data == null || nutritionService == null)
            return;

        if (stamina.Data.CurrentStamina < stamina.Data.MaxStamina)
        {
            nutritionService.ConsumeNutrition(timeDelta * staminaState.StaminaConsumeSpeed);
            stamina.AddStamina(staminaState.StaminaRecoverSpeed * timeDelta);
        }
    }

}

/// <summary>食用时的视觉反馈：只处理物品抖动和主色粒子。</summary>
public sealed class FoodFeedbackRule : IFoodMechanic, IFoodUseRule
{
    public string MechanicId => "core.feedback.visual";
    public int Priority => 0;

    public void OnFoodUse(FoodUseContext context)
    {
        Transform target = context.Food?.Item?.transform;
        if (target == null)
            return;

        int vibrato = UnityEngine.Random.Range(15, 30);
        target.DOShakePosition(0.2f, 0.2f, vibrato).SetEase(Ease.OutQuad);
        CreateMainColorParticle(target);
    }

    private static void CreateMainColorParticle(Transform target)
    {
        SpriteRenderer sprite = target.GetComponentInChildren<SpriteRenderer>();
        if (sprite == null || sprite.sprite == null || GameRes.Instance == null)
            return;

        GameObject particle = GameRes.Instance.InstantiatePrefab("Particle_BeEat", target.position);
        ParticleSystem system = particle?.GetComponent<ParticleSystem>();
        if (system == null)
            return;

        Texture2D texture = sprite.sprite.texture;
        ParticleSystem.MainModule main = system.main;
        main.startColor = texture != null && texture.isReadable
            ? new ColorThief.ColorThief().GetColor(texture).UnityColor
            : Color.white;
    }
}

/// <summary>
/// 食物运行时执行器：只编排服务和规则管线，Mod_Food 作为生命周期和外部 API 入口。
/// </summary>
public sealed class FoodRuntimeExecutor : IDisposable
{
    private readonly FoodRuntimeContext context;
    private readonly FoodNutritionService nutritionService;
    private readonly FoodRulePipeline rulePipeline;
    private readonly FoodUIModule uiModule;
    private bool initialized;

    public FoodRuntimeExecutor(
        FoodRuntimeContext context,
        Mod_Stamina stamina,
        DamageReceiver damageReceiver,
        Mod_PlayerDeathState deathState,
        Mod_Food.FoodStaminaState staminaState,
        GameObject panelPrefab,
        Func<GameObject> readPanelInstance,
        Action<GameObject> writePanelInstance,
        Func<BasePanel> readPanel,
        Action<BasePanel> writePanel)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        nutritionService = new FoodNutritionService(context);
        rulePipeline = new FoodRulePipeline(context);
        Module_HeldFood heldFood = context.Item?.itemMods?.GetMod_ByID<Module_HeldFood>(ModText.HeldFood);
        if (heldFood != null)
        {
            heldFood.BindFoodContext(context);
            rulePipeline.Add(heldFood);
        }
        rulePipeline.Add(new FoodSurvivalRule(
            nutritionService,
            stamina,
            staminaState));
        rulePipeline.Add(new FoodHealthModule(context, damageReceiver, deathState));
        rulePipeline.Add(new FoodFeedbackRule());
        rulePipeline.Add(new FoodAudioModule(context));
        uiModule = new FoodUIModule(
            context,
            damageReceiver,
            panelPrefab,
            readPanelInstance,
            writePanelInstance,
            readPanel,
            writePanel);
        rulePipeline.Add(uiModule);
    }

    public IFoodRuntimeContext Context => context;
    public FoodNutritionService Nutrition => nutritionService;

    /// <summary>汇总营养服务和扩展规则的持续调度需求。</summary>
    public bool RequiresFoodTick => nutritionService.RequiresFoodTick ||
        rulePipeline.HasActiveTickObservers();

    public void Initialize()
    {
        if (initialized)
            return;

        IceBlockFoodMechanicRegistration.EnsureRegistered();
        RawMeatInfectionMechanicRegistration.EnsureRegistered();
        List<IFoodMechanic> registeredRules = FoodMechanicRegistry.CreateFor(context);
        for (int i = 0; i < registeredRules.Count; i++)
            rulePipeline.Add(registeredRules[i]);

        rulePipeline.Initialize();
        initialized = true;
    }

    public void Tick(float deltaTime)
    {
        if (!initialized)
            return;

        FoodTickContext tickContext = new FoodTickContext(context, Mathf.Max(0f, deltaTime));
        rulePipeline.OnTick(tickContext);
        nutritionService.ConsumeNutrition(tickContext.DeltaTime * context.Data.nutritionConsumeRate);
        context.NotifyStateChanged();
    }

    public void BindInventoryContext(Inventory_Data inventoryData, ItemSlot slot, int slotIndex)
    {
        context.ItemOperations.BindInventoryContext(inventoryData, slot, slotIndex);
    }

    public void ConsumeInto(IFoodRuntimeContext consumer)
    {
        if (!initialized || consumer == null)
            return;

        FoodConsumptionService.TryConsume(context, consumer, rulePipeline, ResolveConsumerNutrition(consumer));
    }

    public void SetMovementNutritionConsumeMultiplier(float multiplier)
    {
        nutritionService.SetMovementNutritionConsumeMultiplier(multiplier);
    }

    public void SetMovementWaterConsumeMultiplier(float multiplier)
    {
        nutritionService.SetMovementWaterConsumeMultiplier(multiplier);
    }

    public void MultiplyRuntimeNutritionConsumeSpeed(float multiplier)
    {
        nutritionService.MultiplyRuntimeNutritionConsumeSpeed(multiplier);
    }

    public void MultiplyRuntimeWaterConsumeSpeed(float multiplier)
    {
        nutritionService.MultiplyRuntimeWaterConsumeSpeed(multiplier);
    }

    public float ConsumeNutrition(float timeDelta)
    {
        float result = nutritionService.ConsumeNutrition(timeDelta);
        context.NotifyStateChanged();
        return result;
    }

    public void RestoreNutritionToMaximum()
    {
        nutritionService.RestoreNutritionToMaximum();
    }

    public void RestoreOnRespawn()
    {
        nutritionService.RestoreNutritionToMaximum();
        rulePipeline.OnRespawn();
    }

    public void NotifyStateChanged()
    {
        context.NotifyStateChanged();
    }

    public void ShowPanel() => uiModule.ShowPanel();
    public void HidePanel() => uiModule.HidePanel();
    public void TogglePanel() => uiModule.TogglePanel();
    public void RefreshPanel() => uiModule.RefreshUI();
    public void SavePanelPosition() => uiModule.SavePanelPosition();

    public void Save()
    {
        rulePipeline.Save();
    }

    public void Dispose()
    {
        if (!initialized)
            return;

        rulePipeline.Dispose();
        context.ItemOperations.ClearInventoryContext();
        initialized = false;
    }

    private FoodNutritionService ResolveConsumerNutrition(IFoodRuntimeContext consumer)
    {
        if (consumer is FoodRuntimeContext runtimeContext &&
            runtimeContext == context)
            return nutritionService;

        if (consumer is FoodRuntimeContext otherRuntimeContext)
        {
            // Mod_Food 会把消费者上下文交给自己的执行器；此分支只防止外部自定义上下文空引用。
            return new FoodNutritionService(otherRuntimeContext);
        }

        return nutritionService;
    }

}
