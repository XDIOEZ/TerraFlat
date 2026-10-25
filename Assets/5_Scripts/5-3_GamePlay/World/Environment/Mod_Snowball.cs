using System;
using FlatWorld.Diagnostics;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>雪球复用通用丢弃伤害，并在未命中而碎裂时给落点补一层雪。</summary>
public sealed class Mod_Snowball : Module, IItemModuleDependencyBinder
{
    #region 选格性能诊断
    // 目标解析与轮廓显示分段，首次建框由通用表现点记录。
    private static readonly PerformanceDebugPoint TargetResolveDebug = new("FlatWorld.Snowball.ResolveTarget");
    private static readonly PerformanceDebugPoint OutlineShowDebug = new("FlatWorld.Snowball.ShowOutline");
    #endregion

    #region 配置
    public const string ModuleId = "Mod_Snowball";
    public Ex_ModData ModData = new();
    public float reach = 2f;
    [Range(0f, 1f)] public float shatterChance = 0.5f;
    private Mod_DiscardFlightDamage discardFlight;
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
    public void BindModuleDependencies(ItemMods modules)
        => discardFlight = modules.RequireSingleModById<Mod_DiscardFlightDamage>(Mod_DiscardFlightDamage.PersistedModuleId);

    protected override void OnLoad()
    {
        if (reach <= 0f || float.IsNaN(reach) || float.IsInfinity(reach))
            throw new InvalidOperationException("雪球铺设距离必须是有限正数。");
        if (!float.IsFinite(shatterChance) || shatterChance < 0f || shatterChance > 1f)
            throw new InvalidOperationException("雪球碎裂概率必须在 0 到 1 之间。");
        discardFlight.FlightCompleted -= HandleFlightCompleted;
        discardFlight.FlightCompleted += HandleFlightCompleted;
        nextUseTime = 0f;
        BindAct();
    }
    protected override void OnSave() { }
    protected override void OnUnload()
    {
        if (discardFlight != null) discardFlight.FlightCompleted -= HandleFlightCompleted;
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

    #region 投掷碎裂
    internal enum ThrowResult { Keep, Shatter, ScatterSnow }

    // 整次投掷只抽一次；有效命中包含零伤害，只有未命中碎裂才铺雪。
    internal static ThrowResult ResolveThrowResult(DiscardFlightOutcome outcome, float chance, float roll)
    {
        if (!float.IsFinite(chance) || !float.IsFinite(roll) ||
            chance < 0f || chance > 1f || roll < 0f || roll >= 1f || roll >= chance)
            return ThrowResult.Keep;
        return outcome.HitTarget ? ThrowResult.Shatter : ThrowResult.ScatterSnow;
    }

    private void HandleFlightCompleted(DiscardFlightOutcome outcome)
    {
        if (!IsRuntimeLoaded || !Enabled || !isActiveAndEnabled || !GameNetwork.HasStateAuthority ||
            item == null || item.DestructionHandled || item.InHand)
            return;
        ThrowResult result = ResolveThrowResult(outcome, shatterChance, UnityEngine.Random.value);
        if (result == ThrowResult.Keep) return;

        if (result == ThrowResult.ScatterSnow && GameplayWorldRegistry.TryGet(item.gameObject.scene.name, out _))
        {
            using var worldScope = GameplayWorldRegistry.UseWorld(item.gameObject.scene.name);
            WorldSnowInteraction.TryAddLayer(item.transform.position, out _);
        }
        ItemMgr.Instance.DespawnItem(item, saveData: false);
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
        if (item == null || !item.InHand)
        {
            outline?.Hide();
            return;
        }
        bool resolved;
        RuntimeTerrainTileSample sample;
        using (TargetResolveDebug.Measure(1, item?.itemData?.IDName))
            resolved = WorldSnowInteraction.TryResolveTarget(item, reach, out sample, out _);
        if (!resolved)
        {
            outline?.Hide();
            return;
        }
        outline ??= WorldTileTargetOutline.Create("Snowball Target Outline");
        using (OutlineShowDebug.Measure(1, item?.itemData?.IDName))
            outline.Show(sample.WorldCell);
    }
    #endregion
}
