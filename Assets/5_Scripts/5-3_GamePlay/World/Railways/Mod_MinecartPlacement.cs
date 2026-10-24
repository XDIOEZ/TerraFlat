using System;
using UnityEngine;

/// <summary>矿车沿用普通地面建造资格，只额外拒绝与已有车辆重叠。</summary>
public sealed class Mod_MinecartPlacement : Module, IBuildingPlacementRule
{
    #region 矿车放置净空
    public const string ModuleId = "Mod_MinecartPlacement";
    public Ex_ModData Data = new();
    [Min(0f)] public float RailCaptureDistance = Mod_Minecart.DefaultRailCaptureDistance;
    public override string CanonicalModuleId => ModuleId;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    protected override void OnLoad()
    {
        if (!float.IsFinite(RailCaptureDistance) || RailCaptureDistance < 0f)
            throw new InvalidOperationException("矿车上轨距离必须是有限非负数。");
    }
    protected override void OnSave() { }
    public bool ValidateBuildingPlacement(Item actor, Vector3 position, out string reason)
    {
        Item host = actor ?? item;
        Vector2 target = Mod_Minecart.ResolvePlacementPosition(host, position, RailCaptureDistance);
        bool valid = Mod_Minecart.IsPlacementClear(host, target);
        reason = valid ? null : "这里已经有矿车。";
        return valid;
    }
    #endregion
}
