using System;
using UnityEngine;

/// <summary>
/// 通用局部温度源模块：把设备配置转换为环境温度影响。
/// 可选择由燃烧状态门控，因此同一能力也能用于电热器、冷却器等非燃烧设备。
/// </summary>
public sealed class Mod_LocalTemperatureSource : Module, ICombustionStateReceiver
{
    #region 数据与配置

    public const string ModuleId = "Mod_LocalTemperatureSource";

    public Ex_ModData_MemoryPackable ModData = new() { ID = ModuleId };

    [Range(0.5f, 64f), Tooltip("影响半径，单位为地块。")]
    public float radius = 6f;

    [Tooltip("源中心温度增量；正数加热，负数制冷。")]
    public float celsiusOffset = 15f;

    [Tooltip("开启后，仅在同一物品的燃烧模块处于活动状态时输出温度。")]
    public bool requiresCombustion = true;

    private LocalTemperatureSource source;
    private bool combustionActive;

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ??
                         throw new ArgumentException("局部温度源模块数据类型错误。");
    }

    #endregion

    #region 生命周期

    public override void Load()
    {
        source = GetComponent<LocalTemperatureSource>();
        if (source == null)
            throw new InvalidOperationException($"{name} 缺少 {nameof(LocalTemperatureSource)}。");
        ApplyOutput();
    }

    public override void Save()
    {
    }

    public override void Unload()
    {
        source?.Configure(radius, 0f);
    }

    public void SetCombustionActive(bool active)
    {
        combustionActive = active;
        source ??= GetComponent<LocalTemperatureSource>();
        ApplyOutput();
    }

    private void ApplyOutput()
    {
        if (source == null)
            return;

        float output = !requiresCombustion || combustionActive ? celsiusOffset : 0f;
        source.Configure(Mathf.Max(0.5f, radius), output);
    }

    #endregion
}
