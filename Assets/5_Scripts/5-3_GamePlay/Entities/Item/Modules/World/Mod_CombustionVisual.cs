using System;

/// <summary>
/// 通用燃烧视觉模块：把燃烧状态转发给同 Prefab 的 CombustionVisualEffect。
/// 只负责火焰、火星和烟雾表现，不负责燃料、光照、伤害或温度。
/// </summary>
public sealed class Mod_CombustionVisual : Module, ICombustionStateReceiver
{
    #region 数据与依赖

    public const string ModuleId = "Mod_CombustionVisual";

    public Ex_ModData_MemoryPackable ModData = new() { ID = ModuleId };
    private CombustionVisualEffect effect;
    private bool combustionActive;

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ??
                         throw new ArgumentException("燃烧视觉模块数据类型错误。");
    }

    #endregion

    #region 生命周期

    public override void Load()
    {
        effect = GetComponent<CombustionVisualEffect>();
        if (effect == null)
            throw new InvalidOperationException($"{name} 缺少 {nameof(CombustionVisualEffect)}。");
        effect.BindHost(item);
        effect.SetCombustionActive(combustionActive);
    }

    public override void Save()
    {
    }

    public override void Unload()
    {
        effect?.SetCombustionActive(false);
    }

    public void SetCombustionActive(bool active)
    {
        combustionActive = active;
        effect ??= GetComponent<CombustionVisualEffect>();
        effect?.BindHost(item);
        effect?.SetCombustionActive(active);
    }

    #endregion
}
