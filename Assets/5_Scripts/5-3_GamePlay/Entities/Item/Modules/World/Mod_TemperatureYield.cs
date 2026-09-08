using System;
using UnityEngine;

/// <summary>
/// 用资源所在地的即时摄氏温度调整指定产物的产出倍率，叠加天气和局部冷热源。
/// 默认 0℃以下为 1 倍、25℃以上为 4 倍，中间线性插值，两端封顶；物品 JSON 可独立配置。
/// 只在产出方查询时采样，无逐帧轮询、季节开关或持久化温度，支持以后大棚和导管复用。
/// </summary>
public sealed class Mod_TemperatureYield : Module, IResourceYieldModifier
{
    #region 模块身份

    public const string ModuleId = "温度产量模块"; // 通用模块的稳定身份
    public Ex_ModData ModData = new(); // 仅使用通用模块启停状态

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ??
            throw new ArgumentException("[Mod_TemperatureYield] 模块数据类型错误。", nameof(value));
    }

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    #endregion

    #region 温度响应配置

    [Tooltip("受温度影响的产物稳定 ID；其他产物保持基础产量。")]
    public string OutputItemId = string.Empty;
    [Tooltip("低温档的摄氏温度下界。")]
    public float ColdTemperatureCelsius = 0f;
    [Tooltip("达到高产档的摄氏温度。")]
    public float WarmTemperatureCelsius = 25f;
    [Min(0f), Tooltip("低温档产出倍率。")]
    public float ColdMultiplier = 1f;
    [Min(0f), Tooltip("高温档产出倍率，达到后不再继续增加。")]
    public float WarmMultiplier = 4f;

    #endregion

    #region 生命周期

    /// <summary>建立实例时校验参数；温度与产量不写入存档。</summary>
    public override void Load()
    {
        ValidateConfiguration();
        if (!GameRes.Instance.TryGetItemDefinition(OutputItemId, out _))
            throw new InvalidOperationException($"[Mod_TemperatureYield] 找不到产物定义：{OutputItemId}。");
    }

    /// <summary>倍率来自当前温度场，保存时不固化或改写原始战利品表。</summary>
    public override void Save()
    {
    }

    #endregion

    #region 即时产出查询

    /// <summary>每次结算读取资源脚下的实际温度，让局部保温和天气变化直接生效。</summary>
    public float GetYieldMultiplier(string outputItemId)
    {
        if (!string.Equals(outputItemId, OutputItemId, StringComparison.OrdinalIgnoreCase))
            return 1f;

        if (!TemperatureMgr.Instance.TryGetAmbientTemperature(item.transform.position, out float temperature))
            throw new InvalidOperationException(
                $"[Mod_TemperatureYield] 资源 {item.itemData.IDName} 所在地块温度尚未就绪，不能结算 {OutputItemId} 产量。");

        float warmth = Mathf.InverseLerp(ColdTemperatureCelsius, WarmTemperatureCelsius, temperature);
        return Mathf.Lerp(ColdMultiplier, WarmMultiplier, warmth);
    }

    /// <summary>配置必须具备有效产物、递增温度区间和有限的非负倍率。</summary>
    public void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(OutputItemId))
            throw new InvalidOperationException("[Mod_TemperatureYield] 必须配置产物 ID。");
        if (!IsFinite(ColdTemperatureCelsius) || !IsFinite(WarmTemperatureCelsius) ||
            WarmTemperatureCelsius <= ColdTemperatureCelsius)
            throw new InvalidOperationException("[Mod_TemperatureYield] 暖温阈值必须高于冷温阈值，且温度必须为有限值。");
        if (!IsFinite(ColdMultiplier) || !IsFinite(WarmMultiplier) ||
            ColdMultiplier < 0f || WarmMultiplier < ColdMultiplier)
            throw new InvalidOperationException("[Mod_TemperatureYield] 温度产量倍率必须为有限非负值，且暖温倍率不得低于冷温倍率。");
    }

    /// <summary>拒绝非数和无穷大的配置。</summary>
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    #endregion
}
