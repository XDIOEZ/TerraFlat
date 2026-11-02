using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>地表采挖工具共用选格与挥动表现，持续使用由快捷栏统一调度。</summary>
public abstract class Mod_GroundHarvestToolBase : Mod_ResourceToolBase, IItemModuleDependencyBinder, IContinuousHeldItemUse
{
    #region 配置与运行态

    protected virtual bool SupportsGroundHarvest => true;
    protected virtual bool SupportsRightClickToolUse => SupportsGroundHarvest;
    public bool SupportsHeldUseContinuation => SupportsRightClickToolUse;

    private IWeaponActionCompletionAnimation actionAnimation;
    private Mod_GameController ownerController;
    private WorldTileTargetOutline targetOutline;
    private bool actBound;
    private bool stopContinuousUse;
    private float nextUseTime;
    private RuntimeTerrainTileSample? pendingOverlayTarget;
    private Item pendingOverlayOwner;
    private float overlayStartTime;
    private bool overlayFailureFeedback;

    #endregion

    #region 生命周期

    /// <summary>采挖只依赖挥动表现接口，不引用伤害模块实现。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        actionAnimation = SupportsRightClickToolUse
            ? modules.RequireSingleCapability<IWeaponActionCompletionAnimation>() : null;
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        BindAct();
    }

    protected override void OnUnload()
    {
        if (actBound && item != null)
            item.OnAct -= Act;
        actBound = false;
        stopContinuousUse = false;
        ClearPendingOverlay();
        nextUseTime = 0f;
        ownerController = null;
        actionAnimation = null;
        ReleaseTargetVisuals();
    }

    protected void OnEnable() => BindAct();

    protected void OnDisable()
    {
        stopContinuousUse = false;
        ClearPendingOverlay();
        nextUseTime = 0f;
        ownerController = null;
        ReleaseTargetVisuals();
    }

    protected void OnDestroy() => Unload();

    public override void OnResourcesReloaded()
    {
        base.OnResourcesReloaded();
        if (SupportsRightClickToolUse)
            BindAct();
        else
            Unload();
    }

    private void BindAct()
    {
        if (!SupportsRightClickToolUse || actBound || item == null)
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

    protected virtual bool TryWorkOverlay(RuntimeTerrainTileSample target,
        out Vector2Int cell, out float interval, out string reason)
    {
        cell = default;
        interval = 0f;
        reason = null;
        return false;
    }

    public override void Act()
    {
        if (!SupportsGroundHarvest || item?.Owner == null || pendingOverlayTarget.HasValue)
            return;

        stopContinuousUse = false;
        ownerController ??= item.Owner.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        TryPerformSwing(ownerController, true);
    }

    public virtual bool ContinueHeldUse()
    {
        if (!SupportsGroundHarvest || stopContinuousUse || item?.Owner == null)
            return false;
        ownerController ??= item.Owner.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        TryPerformSwing(ownerController, false);
        return !stopContinuousUse;
    }

    protected void LateUpdate()
    {
        if (!SupportsGroundHarvest || item == null || !item.InHand || item.Owner is not Player player || !player.IsLocalProfile)
        {
            ClearPendingOverlay();
            ownerController = null;
            targetOutline?.Hide();
            return;
        }

        ownerController ??= player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        UpdateTargetVisuals();
    }

    /// <summary>普通砍伐与地表采挖复用武器动作节拍和已有伤害窗口。</summary>
    protected bool TryRequestWeaponSwing()
    {
        if (item == null || !item.InHand || item.Owner == null)
            return false;
        actionAnimation ??= item.itemMods.RequireSingleCapability<IWeaponActionCompletionAnimation>();
        return actionAnimation.TryRequestAction(queueIfBusy: false);
    }

    /// <summary>真实挥动开始后才提交一份挖掘进度。</summary>
    private void TryPerformSwing(Mod_GameController controller, bool showFailureFeedback)
    {
        if (controller == null || item == null || !item.InHand ||
            pendingOverlayTarget.HasValue || Time.time < nextUseTime)
            return;

        bool overlay = TryResolveOverlayTarget(out RuntimeTerrainTileSample overlayTarget);
        string failureReason = null;
        if (!overlay && !GroundTileHarvestSystem.TryResolveTarget(this, out _, out _, out _, out failureReason))
        {
            if (showFailureFeedback && !string.IsNullOrEmpty(failureReason))
                ItemActionFeedback.Show(item.Owner, failureReason);
            return;
        }

        if (overlay)
        {
            actionAnimation ??= item.itemMods.RequireSingleCapability<IWeaponActionCompletionAnimation>();
            if (!actionAnimation.TryRequestAction(CompleteOverlayWork)) return;
            pendingOverlayTarget = overlayTarget;
            pendingOverlayOwner = item.Owner;
            overlayStartTime = Time.time;
            overlayFailureFeedback = showFailureFeedback;
            return;
        }

        if (!TryRequestWeaponSwing())
            return;

        Vector2Int worldCell;
        float useInterval;
        bool worked = GroundTileHarvestSystem.TryWork(this, out _, out worldCell, out useInterval, out failureReason);
        if (!worked)
        {
            if (showFailureFeedback && !string.IsNullOrEmpty(failureReason))
                ItemActionFeedback.Show(item.Owner, failureReason);
            return;
        }

        nextUseTime = Time.time + useInterval;
        if (HarvestKind == ResourceToolKind.Shovel)
            HoeTillingFeedback.PlayDigging(item, worldCell);
        UpdateTargetVisuals();
        item.DecreaseDurability(1);
    }

    // 覆盖层锁定起手地格，动画完成后才逐次结算；挖空或取消后停止长按。
    private void CompleteOverlayWork(bool completed)
    {
        if (!pendingOverlayTarget.HasValue) return;
        RuntimeTerrainTileSample target = pendingOverlayTarget.Value;
        Item owner = pendingOverlayOwner;
        float startedAt = overlayStartTime;
        bool showFailureFeedback = overlayFailureFeedback;
        ClearPendingOverlay();
        if (!completed || !IsRuntimeLoaded || !isActiveAndEnabled || item == null ||
            !item.InHand || item.Owner != owner)
        {
            stopContinuousUse = true;
            return;
        }

        if (!TryWorkOverlay(target, out Vector2Int cell, out float interval, out string reason))
        {
            stopContinuousUse = true;
            if (showFailureFeedback && !string.IsNullOrEmpty(reason))
                ItemActionFeedback.Show(item.Owner, reason);
            return;
        }

        nextUseTime = startedAt + interval;
        if (!TryResolveOverlayTarget(out RuntimeTerrainTileSample remaining) ||
            !ReferenceEquals(remaining.Terrain, target.Terrain) || remaining.WorldCell != target.WorldCell)
            stopContinuousUse = true;
        if (HarvestKind == ResourceToolKind.Shovel)
            HoeTillingFeedback.PlayDigging(item, cell);
        UpdateTargetVisuals();
        item.DecreaseDurability(1);
    }

    private void ClearPendingOverlay()
    {
        pendingOverlayTarget = null;
        pendingOverlayOwner = null;
        overlayStartTime = 0f;
        overlayFailureFeedback = false;
    }

    /// <summary>白框只显示当前可采集覆盖层或可挖掘地块，失效时立即隐藏。</summary>
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
