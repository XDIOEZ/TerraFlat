using UnityEngine;

/// <summary>角色被感知的体型倍率，生命周期发布缓存值，感知热路径不查模块。</summary>
public sealed class Mod_TargetPerceptionSignature : Module
{
    #region 目标感知倍率
    public const string ModuleId = "Actor.TargetPerceptionSignature";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public Ex_ModData Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    [Min(0f)] public float Multiplier = 1f;
    private sealed class State { public float Multiplier = 1f; }
    protected override void OnLoad()
    {
        var state = Data.GetData<State>();
        if (state != null) Multiplier = state.Multiplier;
        item.TargetPerceptionRadiusMultiplier = Multiplier;
    }
    protected override void OnSave() => Data.WriteData(new State { Multiplier = Multiplier });
    protected override void OnUnload() => item.TargetPerceptionRadiusMultiplier = 1f;
    public override void OnResourcesReloaded() => item.TargetPerceptionRadiusMultiplier = Multiplier;
    #endregion
}
