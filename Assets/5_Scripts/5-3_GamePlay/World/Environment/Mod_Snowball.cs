using System;
using UnityEngine;

/// <summary>雪球通过正式物品使用入口铺设一层雪。</summary>
public sealed class Mod_Snowball : Module
{
    #region 配置
    public const string ModuleId = "Mod_Snowball";
    public Ex_ModData ModData = new();
    public float reach = 2f;
    private bool actBound;
    private float nextUseTime;
    private WorldTileTargetOutline outline;
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("雪球模块数据类型错误。");
    }
    #endregion

    #region 生命周期
    public override void Awake() { ModData.ID = ModuleId; base.Awake(); }
    protected override void OnLoad()
    {
        if (reach <= 0f || float.IsNaN(reach) || float.IsInfinity(reach))
            throw new InvalidOperationException("雪球铺设距离必须是有限正数。");
        nextUseTime = 0f;
        BindAct();
    }
    protected override void OnSave() { }
    protected override void OnUnload()
    {
        if (actBound && item != null) item.OnAct -= Act;
        actBound = false;
        nextUseTime = 0f;
        ReleaseOutline();
    }
    private void OnEnable() => BindAct();
    private void BindAct()
    {
        if (actBound || item == null) return;
        item.OnAct += Act;
        actBound = true;
    }
    private void OnDisable() => ReleaseOutline();
    private void OnDestroy() => Unload();
    private void ReleaseOutline()
    {
        if (outline != null) Destroy(outline.gameObject);
        outline = null;
    }
    #endregion

    #region 使用与选格
    public override void Act()
    {
        if (Time.time < nextUseTime) return;
        if (!WorldSnowInteraction.TryPlace(item, reach, out string reason))
        {
            if (!string.IsNullOrEmpty(reason)) ItemActionFeedback.Show(item?.Owner, reason);
            return;
        }
        nextUseTime = Time.time + 0.2f;
    }
    private void LateUpdate()
    {
        if (!WorldSnowInteraction.TryResolveTarget(item, reach, out var sample, out _))
        {
            outline?.Hide();
            return;
        }
        outline ??= WorldTileTargetOutline.Create("Snowball Target Outline");
        outline.Show(sample.WorldCell);
    }
    #endregion
}
