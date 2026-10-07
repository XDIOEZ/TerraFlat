using MemoryPack;
using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.UI;

public partial class Mod_Stamina : Module, IItemModuleDependencyBinder
{
    private readonly List<IStaminaCapacityModifier> capacityModifiers = new(); // 独立容量来源。
    private Mod_PlayerAdminController adminController; // 管理员拥有无限体力，统一在体力权威模块拦截消耗。

    /// <summary>任意体力消费完成后发布；仅用于运行时观察和调试，不进入存档。</summary>
    public event Action<StaminaConsumptionRecord> StaminaConsumed;

    /// <summary>缓存影响体力容量的模块，体力模块不依赖缺盐等具体玩法。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        capacityModifiers.Clear();
        adminController = null;
        foreach (Module module in modules.Mods.Values)
        {
            if (module is IStaminaCapacityModifier modifier) capacityModifiers.Add(modifier);
            if (module is Mod_PlayerAdminController controller) adminController = controller;
        }
    }
    /// <summary>容量变化后限制当前值，并通知现有 HUD。</summary>
    public void RefreshCapacity() => CurrentValue = Data.CurrentStamina;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    [System.Serializable]
    [MemoryPackable]
    public partial class StaminaData
    {
        public float CurrentStamina;
        public float MaxStamina;
    }
    // 是否为满精力 - 实时判断
    public bool IsStaminaFull => Mathf.Approximately(Data.CurrentStamina, MaxValue);

    public StaminaData Data;

    public UltEvent OnChangeStamina = new UltEvent();

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData_MemoryPackable)value;
    }

    public Ex_ModData_MemoryPackable modData;

    public GameObject Prefab_UI;//TODO 这个是UI的预制体

    public Slider slider;
    private BasePanel staminaPanel; // 模块持有自己创建的体力面板，卸载时对称释放。
    private RectTransform staminaBackground; // 原体力条灰黑底纹，只调整可用宽度。
    private RectTransform staminaFillArea; // 保持原填充样式，仅让填充范围与可用上限一致。
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Stamina;
        }
    }
    protected override void OnLoad()
    {
        modData.ReadData(ref Data);

        // 实例化体力 UI 预制体并获取 Slider 组件
        if (Prefab_UI != null)
        {
            staminaPanel = UIManager.Instance.CreatePanelFromGameObject(Prefab_UI);
            if (staminaPanel != null)
            {
                // 耐力条是常驻信息 HUD：不阻断玩法输入，并固定在其它面板下方。
                staminaPanel.SetGameplayInputBlocking(false);
                staminaPanel.transform.SetAsFirstSibling();
                slider = staminaPanel.GetComponentInChildren<Slider>();
            }
        }

        // 兜底：如果预制体上没找到，就在当前物体的子节点中找 Slider
        if (slider == null)
        {
            slider = GetComponentInChildren<Slider>();
        }

        CacheStaminaBarRects();

        // 用当前体力值刷新一次 UI
        UpdateSlider();
    }

    protected override void OnSave()
    {
        modData.WriteData(Data);
    }

    #region 体力面板生命周期
    protected override void OnUnload()
    {
        if (staminaPanel != null)
        {
            UIManager manager = UIManager.ExistingInstance;
            if (manager != null)
                manager.DestroyPanel(staminaPanel);
            else
                Destroy(staminaPanel.gameObject);
        }

        staminaPanel = null;
        slider = null;
        staminaBackground = null;
        staminaFillArea = null;
        capacityModifiers.Clear();
        adminController = null;
    }
    #endregion

    /// <summary>增加基础体力；负值仅作为未分类兼容入口，正式玩法消费应使用带来源的 Consume API。</summary>
    public void AddStamina(float value)
    {
        if (value < 0f)
        {
            ConsumeStamina(StaminaConsumptionSources.Unspecified, -value);
            return;
        }

        CurrentValue += ResolveStaminaDelta(value); // 会自动触发事件和更新Slider
    }

    /// <summary>持续消耗基础体力；同帧多个来源按调用顺序独立结算并自然累加。</summary>
    public float ConsumeStaminaPerSecond(string sourceId, float amountPerSecond, float deltaTime)
    {
        if (amountPerSecond <= 0f || deltaTime <= 0f)
            return 0f;

        return ConsumeStamina(sourceId, amountPerSecond * deltaTime);
    }

    /// <summary>消耗指定来源的基础体力并返回实际扣除量；不足时扣到零。</summary>
    public float ConsumeStamina(string sourceId, float amount)
    {
        if (amount <= 0f)
            return 0f;

        if (HasAdministratorUnlimitedStamina())
        {
            EnsureAdministratorStaminaFull();
            return 0f;
        }

        float effectiveCost = Mathf.Max(0f, -ResolveStaminaDelta(-amount));
        if (effectiveCost <= 0f)
            return 0f;

        float before = CurrentValue;
        CurrentValue = before - effectiveCost;
        float consumed = Mathf.Max(0f, before - CurrentValue);
        PublishConsumption(sourceId, amount, consumed);
        return consumed;
    }

    /// <summary>尝试一次性消耗指定来源的基础体力；不足时完全不扣除。</summary>
    public bool TryConsumeStamina(string sourceId, float amount)
    {
        if (amount <= 0f)
            return true;

        if (HasAdministratorUnlimitedStamina())
        {
            EnsureAdministratorStaminaFull();
            return true;
        }

        float effectiveCost = Mathf.Max(0f, -ResolveStaminaDelta(-amount));
        if (CurrentValue + 0.0001f < effectiveCost)
            return false;

        float before = CurrentValue;
        CurrentValue = before - effectiveCost;
        float consumed = Mathf.Max(0f, before - CurrentValue);
        PublishConsumption(sourceId, amount, consumed);
        return true;
    }

    /// <summary>未提供来源的旧调用统一标记为未分类；内置玩法不得使用此入口。</summary>
    public bool TryConsumeStamina(float amount)
    {
        return TryConsumeStamina(StaminaConsumptionSources.Unspecified, amount);
    }

    /// <summary>发布来源清晰的实际消费记录；零消耗不产生事件。</summary>
    private void PublishConsumption(string sourceId, float requestedBaseAmount, float consumedAmount)
    {
        if (consumedAmount <= 0f)
            return;

        string stableSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? StaminaConsumptionSources.Unspecified
            : sourceId;
        StaminaConsumed?.Invoke(new StaminaConsumptionRecord(
            stableSourceId,
            Mathf.Max(0f, requestedBaseAmount),
            consumedAmount,
            CurrentValue));
    }

    /// <summary>把基础体力变化换算为难度规则下的实际变化。</summary>
    private float ResolveStaminaDelta(float value)
    {
        float multiplier = GameDifficultyService.ResolveStaminaDeltaMultiplier(item, value);
        return value * multiplier;
    }

    /// <summary>管理员身份本身即拥有无限体力，不依赖额外的无敌开关。</summary>
    private bool HasAdministratorUnlimitedStamina()
    {
        return adminController != null && adminController.IsAdministrator;
    }

    /// <summary>管理员首次进入无限体力状态时立即补满，避免低体力阈值继续阻止奔跑或攻击。</summary>
    private void EnsureAdministratorStaminaFull()
    {
        if (Data == null || MaxValue <= 0f || Mathf.Approximately(Data.CurrentStamina, MaxValue))
            return;

        CurrentValue = MaxValue;
    }


    public override void ModUpdate(float deltaTime)
    {
        UpdateSlider();
    }

    private void UpdateSlider()
    {
        if (slider != null && MaxValue > 0)
        {
            slider.value = CurrentValue / MaxValue;
        }

        UpdateStaminaCapacityWidth();
    }

    /// <summary>缓存原体力条的底纹与填充区域，不改颜色、层级或外框。</summary>
    private void CacheStaminaBarRects()
    {
        if (slider == null)
            return;

        staminaBackground = slider.transform.Find("Background") as RectTransform;
        staminaFillArea = slider.fillRect != null ? slider.fillRect.parent as RectTransform : null;
    }

    /// <summary>上限减少时只从右侧缩短原底纹和可填充范围，根节点尺寸保持不变。</summary>
    private void UpdateStaminaCapacityWidth()
    {
        float baseMaxValue = Mathf.Max(0f, Data?.MaxStamina ?? 0f);
        float ratio = baseMaxValue > 0f ? Mathf.Clamp01(MaxValue / baseMaxValue) : 0f;

        SetRightEdgeRatio(staminaBackground, ratio);
        SetRightEdgeRatio(staminaFillArea, ratio);
    }

    /// <summary>仅调整右侧锚点，保留原有视觉参数。</summary>
    private static void SetRightEdgeRatio(RectTransform target, float ratio)
    {
        if (target == null)
            return;

        Vector2 anchorMax = target.anchorMax;
        anchorMax.x = ratio;
        target.anchorMax = anchorMax;
    }


    // 当前体力值 - 带事件触发
    public float CurrentValue
    {
        get => Data.CurrentStamina;
        set
        {
            Data.CurrentStamina = Mathf.Clamp(value, 0, MaxValue);
            OnChangeStamina.Invoke(); // 值改变时触发事件
            UpdateSlider(); // 更新Slider显示
        }
    }

    // 最大体力值 - 只读属性，不受事件影响
    public float MaxValue
    {
        get
        {
            float multiplier = 1f;
            foreach (IStaminaCapacityModifier modifier in capacityModifiers)
                multiplier *= modifier.StaminaCapacityMultiplier;
            return Data.MaxStamina * multiplier;
        }
        // 移除了 setter，使其成为只读属性
    }
}

/// <summary>
/// 内置体力消费来源的稳定 ID。玩法模块只声明来源，倍率和最终数值仍由 Mod_Stamina 统一结算。
/// MOD 可使用自己的命名空间 ID 接入同一消费 API，无需修改此列表。
/// </summary>
public static class StaminaConsumptionSources
{
    public const string Unspecified = "flatworld.stamina.unspecified";
    public const string MovementWalk = "flatworld.movement.walk";
    public const string MovementRun = "flatworld.movement.run";
    public const string CarrierBoost = "flatworld.carrier.boost";
    public const string BowCharge = "flatworld.combat.bow_charge";
    public const string WeaponAttack = "flatworld.combat.weapon_attack";
    public const string LegacyColdWeaponAttack = "flatworld.combat.legacy_cold_weapon_attack";
    public const string Swimming = "flatworld.environment.swimming";
    public const string HeatStress = "flatworld.environment.heat_stress";
    public const string BuffEffect = "flatworld.buff.stamina_effect";
}

/// <summary>一次已完成的体力消费事务；仅描述运行时事实，不持久化消费过程。</summary>
public readonly struct StaminaConsumptionRecord
{
    public string SourceId { get; }
    public float RequestedBaseAmount { get; }
    public float ConsumedAmount { get; }
    public float RemainingStamina { get; }

    public StaminaConsumptionRecord(string sourceId, float requestedBaseAmount, float consumedAmount, float remainingStamina)
    {
        SourceId = sourceId;
        RequestedBaseAmount = requestedBaseAmount;
        ConsumedAmount = consumedAmount;
        RemainingStamina = remainingStamina;
    }
}
