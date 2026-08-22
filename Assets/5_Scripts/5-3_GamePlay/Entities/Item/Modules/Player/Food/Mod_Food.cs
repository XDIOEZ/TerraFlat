using AYellowpaper.SerializedCollections;
using DG.Tweening;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using UnityEngine;
using UnityEngine.UI;
using UltEvents;

public partial class Mod_Food : Module, IInstanceUI, IItemPoolLifecycle
{
    public override string CanonicalModuleId => ModText.Food;

    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
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

    [MemoryPackIgnore]
    private UnityEngine.InputSystem.InputAction _tabAction;
    [MemoryPackIgnore]
    private GameController _inputController;

    #endregion

    #region 生命周期方法
    public override void Awake()
    {
        base.Awake();
    }

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
        BindTabInput();

        // 根据保存的状态决定是否显示面板
        if (Data.ShowCanvas)
        {
            ShowPanel();
        }

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

    /// <summary>
    /// 为右键临时使用实例绑定真实库存槽位，确保多次点击写回同一份物品数据。
    /// </summary>
    public void BindRuntimeInventoryContext(Inventory_Data inventoryData, ItemSlot slot, int slotIndex)
    {
        _runtimeExecutor?.BindInventoryContext(inventoryData, slot, slotIndex);
    }

    public void OnItemTakenFromPool()
    {
        ConsumeCompleted = null;
        ReleaseRuntimeBindings(destroyPanel: true);
    }

    public void OnItemReturnedToPool()
    {
        ConsumeCompleted = null;
        ReleaseRuntimeBindings(destroyPanel: true);
    }

    private void OnDestroy()
    {
        ReleaseRuntimeBindings(destroyPanel: true);
    }

    private void OnTogglePanelPerformed(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        if (_inputController != null && !_inputController.IsGameplayInputAllowed(context))
            return;

        TogglePanel();
    }

    private void BindTabInput()
    {
        UnbindTabInput();
        if (item?.itemMods == null)
            return;

        item.itemMods.GetMod_ByID(ModText.Controller, out GameController controller);
        if (controller?._inputActions == null)
            return;

        _inputController = controller;
        _tabAction = controller._inputActions.Win10.Tab;
        _tabAction.performed += OnTogglePanelPerformed;
    }

    private void UnbindTabInput()
    {
        if (_tabAction != null)
            _tabAction.performed -= OnTogglePanelPerformed;

        _tabAction = null;
        _inputController = null;
    }

    private void ReleaseRuntimeBindings(bool destroyPanel)
    {
        _runtimeExecutor?.Dispose();
        _runtimeExecutor = null;

        if (item != null)
            item.OnAct -= Act;

        UnbindTabInput();
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

    #region 面板管理
    [Button("显示面板")]
    public void ShowPanel()
    {
        _runtimeExecutor?.ShowPanel();
    }

    [Button("隐藏面板")]
    public void HidePanel()
    {
        _runtimeExecutor?.HidePanel();
    }

    [Button("切换面板")]
    public void TogglePanel()
    {
        _runtimeExecutor?.TogglePanel();
    }

    public void I_ShowPanel()
    {
        ShowPanel();
    }

    public void I_ClosePanel()
    {
        HidePanel();
    }

    public void I_TogglePanel()
    {
        TogglePanel();
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

    private void ResolveFoodRuntimeModules()
    {
        if (item == null || item.itemMods == null)
        {
            return;
        }

        item.itemMods.GetMod_ByID(ModText.Stamina, out _stamina);
        item.itemMods.GetMod_ByID(ModText.Hp, out _damageReceiver);
        item.itemMods.GetMod_ByID(Mod_PlayerDeathState.ModuleId, out _deathState);
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
