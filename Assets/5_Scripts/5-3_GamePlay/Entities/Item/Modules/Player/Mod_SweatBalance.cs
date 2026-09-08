using System;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>独立体力容量修正；基础体力上限保持原值，各来源以乘数组合。</summary>
public interface IStaminaCapacityModifier { float StaminaCapacityMultiplier { get; } }

/// <summary>出汗与补盐状态；缺盐不会被普通体力恢复或清除 Buff 抹掉。</summary>
[Serializable, MemoryPackable]
public partial class SweatBalanceState
{
    public float SaltBurden; // 0～1 缺盐负担。
    public float PendingRecovery; // 补盐后尚待缓慢恢复的量。
    public float RecoveryRate; // 当前补盐每秒恢复量。
    public float HeatExposure; // 高温缺水连续暴露秒。
}

/// <summary>高温补给规则：30℃以上增加耗水与盐负担，最多降低 25% 体力上限；盐食物在 60 秒内缓慢恢复。</summary>
public sealed class Mod_SweatBalance : Module, IItemModuleDependencyBinder, IStaminaCapacityModifier
{
    #region 数据与依赖
    public const string ModuleId = "Mod_SweatBalance";
    public Ex_ModData_MemoryPackable ModData = new(); // 独立持久化。
    public SweatBalanceState Data = new(); // 盐负担与补给进度。
    public float sweatingTemperature = 30f; // 开始出汗的环境温度。
    public float extraWaterPerSecond = 0.025f; // 标准出汗耗水。
    public float saltBurdenPerSecond = 0.001f; // 标准出汗盐负担。
    [Range(0f, 0.95f)] public float maxCapacityLoss = 0.25f; // 最大体力上限扣减。
    public float recoverySeconds = 60f; // 补盐恢复时长。
    public float saltRecoveryPerServing = 0.3f; // 每份含盐食物恢复负担。
    public float heatstrokeSeconds = 60f; // 高温缺水的预警时长。
    private Mod_Food food; // 水分来源。
    private Mod_Temperature temperature; // 当地气温。
    private Mod_Stamina stamina; // 有效体力上限。
    private bool heatWarningShown; // 本次高温事件已提示。
    private bool saltWarningShown; // 本次缺盐已提示。
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 1f;
    public float StaminaCapacityMultiplier => 1f - Mathf.Clamp01(Data.SaltBurden) * maxCapacityLoss;
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData_MemoryPackable)value; }
    /// <summary>解析唯一营养、体温和体力能力。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        food = modules.RequireSingleModById<Mod_Food>(ModText.Food);
        temperature = modules.RequireSingleModById<Mod_Temperature>(ModText.Temperature);
        stamina = modules.RequireSingleModById<Mod_Stamina>(ModText.Stamina);
    }
    /// <summary>恢复负担；新实例不携带上一角色的提示状态。</summary>
    public override void Load()
    {
        ModData.ReadData(ref Data);
        if (!IsFinite(Data.SaltBurden) || Data.SaltBurden < 0f || Data.SaltBurden > 1f ||
            !IsFinite(Data.PendingRecovery) || Data.PendingRecovery < 0f || Data.PendingRecovery > 1f ||
            !IsFinite(Data.RecoveryRate) || Data.RecoveryRate < 0f ||
            !IsFinite(Data.HeatExposure) || Data.HeatExposure < 0f ||
            !IsFinite(recoverySeconds) || recoverySeconds <= 0f)
            throw new InvalidOperationException("出汗补盐状态或恢复时长无效。");
        heatWarningShown = saltWarningShown = false;
    }
    /// <summary>盐负担和剩余恢复量随玩家保存。</summary>
    public override void Save() => ModData.WriteData(Data);
    /// <summary>只清理引用，不清空已经保存的生存状态。</summary>
    public override void Unload() { food = null; temperature = null; stamina = null; }
    #endregion

    /// <summary>以独立上限负担结算出汗和补盐，普通休息只恢复剩余容量内的体力。</summary>
    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || food?.Data?.nutrition == null || temperature?.Data == null)
            return;
        Nutrition nutrition = food.Data.nutrition;
        float sweat = Mathf.Clamp01((temperature.Data.AmbientTemperature - sweatingTemperature) / 10f);
        float previous = Data.SaltBurden;
        Data.SaltBurden = Mathf.Clamp01(Data.SaltBurden + saltBurdenPerSecond * sweat * deltaTime);
        nutrition.Water = Mathf.Max(0f, nutrition.Water - extraWaterPerSecond * sweat * deltaTime);
        float restored = Mathf.Min(Data.PendingRecovery, Data.RecoveryRate * deltaTime);
        Data.PendingRecovery -= restored;
        Data.SaltBurden = Mathf.Max(0f, Data.SaltBurden - restored);
        bool dehydratedHeat = sweat > 0f && nutrition.Water < nutrition.Max_Water * 0.15f;
        Data.HeatExposure = dehydratedHeat ? Data.HeatExposure + deltaTime : Mathf.Max(0f, Data.HeatExposure - deltaTime * 2f);
        if (Data.HeatExposure >= heatstrokeSeconds)
        {
            // 中暑压力由持续体力流失表现，补水或降温后可恢复，吃盐不替代补水。
            stamina.AddStamina(-deltaTime * 0.8f);
            if (!heatWarningShown) ItemActionFeedback.Show(item, "又热又渴，先补水，到凉快的地方休息。");
            heatWarningShown = true;
        }
        else if (Data.HeatExposure <= 0f) heatWarningShown = false;
        if (Data.SaltBurden >= 0.25f && !saltWarningShown)
        {
            ItemActionFeedback.Show(item, "流汗太多，体力上限下降了，吃些含盐食物吧。");
            saltWarningShown = true;
        }
        if (Data.SaltBurden < 0.1f) saltWarningShown = false;
        if (!Mathf.Approximately(previous, Data.SaltBurden)) stamina.RefreshCapacity();
        if (sweat > 0f) food.NotifyStateChanged();
    }

    /// <summary>追加有限的补盐恢复量，不永久增加基础体力上限。</summary>
    public void SupplySalt()
    {
        Data.PendingRecovery = Mathf.Min(Data.SaltBurden, Data.PendingRecovery + saltRecoveryPerServing);
        Data.RecoveryRate = Data.PendingRecovery / recoverySeconds;
    }
    /// <summary>持久化状态不接受非有限数值。</summary>
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>盐类食物完整消费后的独立效果，不能用食用进度反复刷上限。</summary>
public sealed class SaltFoodMechanic : IFoodMechanic, IFoodConsumptionObserver
{
    public string MechanicId => "survival.salt_food";
    public int Priority => 160;
    /// <summary>按稳定标签注册含盐食物，不把具体菜谱写入营养系统。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register() => FoodMechanicRegistry.RegisterForTag("survival.salt_food", "SaltFood", _ => new SaltFoodMechanic());
    /// <summary>一份盐食物恢复最多三成缺盐负担。</summary>
    public void OnFoodConsumed(FoodConsumeResult result) =>
        result.Consumer.itemMods.GetMod_ByID<Mod_SweatBalance>(Mod_SweatBalance.ModuleId)?.SupplySalt();
}
