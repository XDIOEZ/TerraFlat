using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>地表采挖工具共用选格、右键持续使用和挥动表现，具体工具只声明能力身份。</summary>
public abstract class Mod_GroundHarvestToolBase : Mod_ResourceToolBase, IItemModuleDependencyBinder
{
    #region 配置与运行态

    protected virtual bool SupportsGroundHarvest => true;

    private IWeaponActionAnimation actionAnimation;
    private Mod_GameController ownerController;
    private WorldTileTargetOutline targetOutline;
    private bool actBound;
    private bool continuousUseArmed;
    private float nextUseTime;

    #endregion

    #region 生命周期

    /// <summary>采挖只依赖挥动表现接口，不引用伤害模块实现。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        actionAnimation = SupportsGroundHarvest
            ? modules.RequireSingleCapability<IWeaponActionAnimation>() : null;
    }

    public override void Load()
    {
        base.Load();
        BindAct();
    }

    public override void Unload()
    {
        if (actBound && item != null)
            item.OnAct -= Act;
        actBound = false;
        continuousUseArmed = false;
        nextUseTime = 0f;
        ownerController = null;
        actionAnimation = null;
        ReleaseTargetVisuals();
    }

    protected void OnEnable() => BindAct();

    protected void OnDisable()
    {
        continuousUseArmed = false;
        nextUseTime = 0f;
        ownerController = null;
        ReleaseTargetVisuals();
    }

    protected void OnDestroy() => Unload();

    public override void OnResourcesReloaded()
    {
        base.OnResourcesReloaded();
        if (SupportsGroundHarvest)
            BindAct();
        else
            Unload();
    }

    private void BindAct()
    {
        if (!SupportsGroundHarvest || actBound || item == null)
            return;
        item.OnAct += Act;
        actBound = true;
    }

    #endregion

    #region 使用与表现

    // 扩展工具可优先处理覆盖层，未命中时继续使用普通地表采挖。
    protected virtual bool TryResolveOverlayTarget(out RuntimeTerrainTileSample sample)
    {
        sample = default;
        return false;
    }

    protected virtual bool TryWorkOverlay(out Vector2Int cell, out float interval, out string reason)
    {
        cell = default;
        interval = 0f;
        reason = null;
        return false;
    }

    public override void Act()
    {
        if (!SupportsGroundHarvest || item?.Owner == null)
            return;

        ownerController ??= item.Owner.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        continuousUseArmed = ownerController?.IsRightClickHeld == true;
        TryPerformSwing(ownerController, true);
    }

    protected void LateUpdate()
    {
        if (!SupportsGroundHarvest || item == null || !item.InHand || item.Owner is not Player player || !player.IsLocalProfile)
        {
            continuousUseArmed = false;
            ownerController = null;
            targetOutline?.Hide();
            return;
        }

        ownerController ??= player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        UpdateTargetVisuals();
        if (ownerController == null)
            return;

        if (!ownerController.IsRightClickHeld)
            continuousUseArmed = false;
        if (continuousUseArmed)
            TryPerformSwing(ownerController, false);
    }

    /// <summary>真实挥动开始后才提交一份挖掘进度。</summary>
    private void TryPerformSwing(Mod_GameController controller, bool showFailureFeedback)
    {
        if (controller == null || item == null || !item.InHand || Time.time < nextUseTime)
            return;

        bool overlay = TryResolveOverlayTarget(out _);
        string failureReason = null;
        if (!overlay && !GroundTileHarvestSystem.TryResolveTarget(this, out _, out _, out _, out failureReason))
        {
            if (showFailureFeedback && !string.IsNullOrEmpty(failureReason))
                ItemActionFeedback.Show(item.Owner, failureReason);
            return;
        }

        actionAnimation ??= item.itemMods.RequireSingleCapability<IWeaponActionAnimation>();
        if (!actionAnimation.TryRequestAction(queueIfBusy: false))
            return;

        Vector2Int worldCell;
        float useInterval;
        bool worked = overlay
            ? TryWorkOverlay(out worldCell, out useInterval, out failureReason)
            : GroundTileHarvestSystem.TryWork(this, out _, out worldCell, out useInterval, out failureReason);
        if (!worked)
        {
            if (showFailureFeedback && !string.IsNullOrEmpty(failureReason))
                ItemActionFeedback.Show(item.Owner, failureReason);
            return;
        }

        nextUseTime = Time.time + useInterval;
        // 铲空覆盖层后重新按右键才挖底层，避免一次长按顺手破坏雪下地面。
        if (overlay) continuousUseArmed = false;
        if (HarvestKind == ResourceToolKind.Shovel)
            HoeTillingFeedback.PlayDigging(item, worldCell);
        UpdateTargetVisuals();
    }

    /// <summary>白框始终跟随当前指针地格；挖掘进度由区块表现层渐显目标地块。</summary>
    private void UpdateTargetVisuals()
    {
        if (!TryResolveOverlayTarget(out RuntimeTerrainTileSample sample) &&
            !GroundTileHarvestSystem.TryResolvePreview(this, out sample))
        {
            targetOutline?.Hide();
            return;
        }

        targetOutline ??= WorldTileTargetOutline.Create($"{HarvestKind} Ground Target Outline");
        targetOutline.Show(sample.WorldCell);
    }

    private void ReleaseTargetVisuals()
    {
        if (targetOutline != null) Destroy(targetOutline.gameObject);
        targetOutline = null;
    }

    #endregion
}
