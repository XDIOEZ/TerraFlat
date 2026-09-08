using DG.Tweening;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using UnityEngine;
using UltEvents;

/// <summary>维护食物运行时与营养规则；本地玩家的参数 HUD 随模块加载创建、随卸载释放，不提供显隐开关。</summary>
public partial class Mod_Food : Module, IItemPoolLifecycle
{
    public override string CanonicalModuleId => ModText.Food;

    /// <summary>没有持续营养或规则工作的静态食物退出物品 Tick 调度。</summary>
    public override ModuleTickMode TickMode => _runtimeExecutor?.RequiresFoodTick == true
        ? ModuleTickMode.FixedInterval
        : ModuleTickMode.Disabled;
    public override float FixedTickInterval => 0.1f;

    [MemoryPackable]
    [System.Serializable]
    public partial class FoodStaminaState
    {
        public float StaminaRecoverSpeed = 1f;
        public float StaminaConsumeSpeed = 0.5f;
    }

    #region 数据定义
    [ShowInInspector]
    public ModData_FoodData FoodModData = new ModData_FoodData();
    public override ModuleData _Data
    {
        get => FoodModData;
        set => FoodModData = value as ModData_FoodData ?? new ModData_FoodData();
    }

    [FoldoutGroup("食物数据")]
    [ShowInInspector]
    [LabelText("食物模块数据")]
    public Food Data
    {
        get
        {
            FoodModData ??= new ModData_FoodData();
            return FoodModData.EnsureFoodData();
        }
        set
        {
            FoodModData ??= new ModData_FoodData();
            FoodModData.FoodData = value ?? new Food();
        }
    }

    [FoldoutGroup("运行时")]
    [LabelText("数据更新事件")]
    public UltEvent DataUpdate = new UltEvent();

    [FoldoutGroup("UI")]
    [LabelText("面板预制体")]
    public GameObject PanelPrefab;

    [FoldoutGroup("UI")]
    [ReadOnly]
    [LabelText("面板实例")]
    public GameObject PanleInstance;

    [FoldoutGroup("UI")]
    [ReadOnly]
    [LabelText("面板控制器")]
    public BasePanel panelUI; // 替换UI_FloatData_Slider为BasePanel

    [FoldoutGroup("运行时")]
    [LabelText("体力联动状态")]
    public FoodStaminaState StaminaState = new FoodStaminaState();

    [FoldoutGroup("食用类型")]
    [LabelText("食用方式")]
    [Tooltip("静态物品定义。Drink 只在完整喝下一份饮品后触发饮水玩法事件。")]
    public FoodConsumeKind ConsumeKind = FoodConsumeKind.Solid;

    [FoldoutGroup("食用类型")]
    [LabelText("允许食用")]
    [Tooltip("关闭后物品保留食物数据和计时模块，但不会响应使用操作或提供营养。")]
    public bool ConsumptionEnabled = true;

    [FoldoutGroup("食用类型")]
    [LabelText("完成食用后替换物品ID")]
    [Tooltip("达到食用次数后在原库存槽位替换为该物品；留空时沿用普通食物的消耗逻辑。")]
    public string ConsumeCompleteReplacementItemID = string.Empty;

    public event Action<FoodConsumeResult> ConsumeCompleted;

    /// <summary>由容器等饮水来源恢复水分，并发布与普通饮品相同的完整饮用结果。</summary>
    public float DrinkWater(float amount, Item source)
    {
        if (amount <= 0f || Data?.nutrition == null)
            return 0f;
        Nutrition nutrition = Data.nutrition;
        float gain = Mathf.Min(amount, Mathf.Max(0f, nutrition.Max_Water - nutrition.Water));
        if (gain <= 0f) return 0f;
        nutrition.Water += gain;
        ConsumeCompleted?.Invoke(new FoodConsumeResult(item, source, FoodConsumeKind.Drink, amount, gain));
        NotifyStateChanged();
        return gain;
    }

    [MemoryPackIgnore]
    private Mod_Stamina _stamina;

    [MemoryPackIgnore]
    private DamageReceiver _damageReceiver;

    [MemoryPackIgnore]
    private Mod_PlayerDeathState _deathState;

    [MemoryPackIgnore]
    private FoodRuntimeExecutor _runtimeExecutor;

    public float RuntimeNutritionConsumeMultiplier
    {
        get => _runtimeExecutor?.Nutrition.RuntimeNutritionConsumeMultiplier ?? 1f;
        set
        {
            if (_runtimeExecutor != null)
                _runtimeExecutor.Nutrition.RuntimeNutritionConsumeMultiplier = value;
        }
    }

    /// <summary>移动动作提供的独立营养消耗倍率，不受 Buff 清理影响。</summary>
    public float MovementNutritionConsumeMultiplier => _runtimeExecutor?.Nutrition.MovementNutritionConsumeMultiplier ?? 1f;

    public float MovementWaterConsumeMultiplier => _runtimeExecutor?.Nutrition.MovementWaterConsumeMultiplier ?? 1f;

    public float BuffNutritionConsumeMultiplier => _runtimeExecutor?.Nutrition.BuffNutritionConsumeMultiplier ?? 1f;
    public float BuffWaterConsumeMultiplier => _runtimeExecutor?.Nutrition.BuffWaterConsumeMultiplier ?? 1f;

    #endregion

    #region 生命周期方法
    public override void Awake()
    {
        base.Awake();
    }

    /// <summary>构建食物运行时并刷新该物品的 Tick 调度状态。</summary>
    public override void Load()
    {
        FoodModData ??= new ModData_FoodData();
        ResolveFoodRuntimeModules();

        _runtimeExecutor?.Dispose();
        FoodRuntimeContext runtimeContext = new FoodRuntimeContext(
            item,
            FoodModData,
            () => ConsumptionEnabled,
            () => ConsumeCompleteReplacementItemID,
            () => ConsumeKind,
            () => DataUpdate?.Invoke(),
            result => ConsumeCompleted?.Invoke(result));
        _runtimeExecutor = new FoodRuntimeExecutor(
            runtimeContext,
            _stamina,
            _damageReceiver,
            _deathState,
            StaminaState,
            PanelPrefab,
            () => PanleInstance,
            value => PanleInstance = value,
            () => panelUI,
            value => panelUI = value);
        _runtimeExecutor.Initialize();
        InvalidateTickSchedule();
        if (item != null)
        {
            item.OnAct -= Act;
            item.OnAct += Act;
        }

    }
    public override void Save()
    {
        _runtimeExecutor?.Save();

        // 保存面板位置
        _runtimeExecutor?.SavePanelPosition();
    }

    /// <summary>模块卸载时释放运行时和 HUD，避免离开世界后残留参数面板。</summary>
    public override void Unload()
    {
        ReleaseRuntimeBindings();
    }

    /// <summary>
    /// 调用吃的行为
    /// </summary>
    public override void Act()
    {
        if (item == null || item.DestructionHandled || item.itemData?.Stack == null ||
            item.itemData.Stack.Amount < 1f)
            return;

        // 同一个右键同时支持建筑和食用；建筑预览有效时由建筑模块优先消费本次动作。
        Mod_Building building = item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
        if (building != null && (building.IsPlacementPending || building.IsPlacementActionAvailable))
            return;

        Item owner = item?.Owner;
        Mod_Food playerFood = owner?.itemMods?.GetMod_ByID(ModText.Food) as Mod_Food;
        if (playerFood == null)
        {
            Debug.LogWarning($"[Mod_Food] {item?.name ?? name} 缺少有效食用者或食用者 Food 模块。", this);
            return;
        }

        playerFood.Eat(BeEater: this);
    }

    public void OnItemTakenFromPool()
    {
        ConsumeCompleted = null;
        ReleaseRuntimeBindings();
    }

    public void OnItemReturnedToPool()
    {
        ConsumeCompleted = null;
        ReleaseRuntimeBindings();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeBindings();
    }

    /// <summary>释放食物运行时和常驻参数面板。</summary>
    private void ReleaseRuntimeBindings()
    {
        _runtimeExecutor?.Dispose();
        _runtimeExecutor = null;
        InvalidateTickSchedule();

        if (item != null)
            item.OnAct -= Act;

        DOTween.Kill(item?.transform);
    }

    public override void ModUpdate(float timeDelta)
    {
        _runtimeExecutor?.Tick(timeDelta);
    }
    #endregion

    #region 营养管理
    /// <summary>由移动饥饿动作设置当前倍率；不写入 Buff 列表，也不参与 Buff 清理。</summary>
    public void SetMovementNutritionConsumeMultiplier(float multiplier)
    {
        _runtimeExecutor?.SetMovementNutritionConsumeMultiplier(multiplier);
    }

    public void SetMovementWaterConsumeMultiplier(float multiplier)
    {
        _runtimeExecutor?.SetMovementWaterConsumeMultiplier(multiplier);
    }

    public void MultiplyRuntimeNutritionConsumeSpeed(float multiplier)
    {
        _runtimeExecutor?.MultiplyRuntimeNutritionConsumeSpeed(multiplier);
    }

    /// <summary>单独叠乘水分消耗倍率，不影响其他营养消耗。</summary>
    public void MultiplyRuntimeWaterConsumeSpeed(float multiplier)
    {
        _runtimeExecutor?.MultiplyRuntimeWaterConsumeSpeed(multiplier);
    }

    public float ConsumeNutrition(float timeDelta)
    {
        return _runtimeExecutor?.ConsumeNutrition(timeDelta) ?? 0f;
    }

    public void RestoreNutritionToMaximum()
    {
        _runtimeExecutor?.RestoreNutritionToMaximum();
    }

    /// <summary>提供外部营养修改入口，并把状态变化发布给执行器观察者。</summary>
    public void NotifyStateChanged()
    {
        if (_runtimeExecutor != null)
            _runtimeExecutor.NotifyStateChanged();
        else
            DataUpdate?.Invoke();
    }
    #endregion

    #region UI更新
    [Button("刷新面板")]
    public void RefreshUI()
    {
        _runtimeExecutor?.RefreshPanel();
    }
    #endregion

    #region 进食行为
    public void BeEat(Mod_Food Eater)
    {
        if (Eater?._runtimeExecutor == null)
            return;

        _runtimeExecutor?.ConsumeInto(Eater._runtimeExecutor.Context);
    }

    public void Eat(Mod_Food BeEater)
    {
        if (BeEater?._runtimeExecutor == null)
            return;

        BeEater._runtimeExecutor.ConsumeInto(_runtimeExecutor?.Context);
    }
    #endregion

    #region 工具方法

    /// <summary>静默解析只在角色食物上存在的可选运行时模块。</summary>
    private void ResolveFoodRuntimeModules()
    {
        if (item == null || item.itemMods == null)
        {
            return;
        }

        _stamina = item.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        _damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        _deathState = item.itemMods.GetMod_ByID<Mod_PlayerDeathState>(Mod_PlayerDeathState.ModuleId);
    }

    public void RestoreOnRespawn()
    {
        _runtimeExecutor?.RestoreOnRespawn();
    }

    public static ItemData CreateSpoilageTargetItemData(string targetItemID)
    {
        return new InventoryFoodItemOperationGateway().CreateItemData(targetItemID);
    }

    #endregion

}
