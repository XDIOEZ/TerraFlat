using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>铲子完整玩法能力；挂上本模块即可获得选格、持续挖地和资源工具身份。</summary>
public sealed class Mod_Shovel : Mod_ResourceToolBase, IItemModuleDependencyBinder
{
    #region 配置与运行态

    public const string ModuleId = "Mod_Shovel";
    public override string CanonicalModuleId => ModuleId;
    public override ResourceToolKind HarvestKind => ResourceToolKind.Shovel;

    private IWeaponActionAnimation actionAnimation;
    private Mod_GameController ownerController;
    private WorldTileTargetOutline targetOutline;
    private GroundHarvestCrackOverlay crackOverlay;
    private bool actBound;
    private bool continuousUseArmed;

    #endregion

    #region 生命周期

    /// <summary>铲子只依赖挥动表现接口，不引用伤害模块实现。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        actionAnimation = modules.RequireSingleCapability<IWeaponActionAnimation>();
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
        ownerController = null;
        actionAnimation = null;
        ReleaseTargetVisuals();
    }

    private void OnEnable() => BindAct();

    private void OnDisable()
    {
        continuousUseArmed = false;
        ownerController = null;
        ReleaseTargetVisuals();
    }

    private void OnDestroy() => Unload();

    private void BindAct()
    {
        if (actBound || item == null)
            return;
        item.OnAct += Act;
        actBound = true;
    }

    #endregion

    #region 使用与表现

    public override void Act()
    {
        if (item?.Owner == null)
            return;

        ownerController ??= item.Owner.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        continuousUseArmed = ownerController?.IsRightClickHeld == true;
        TryPerformSwing(ownerController, true);
    }

    private void LateUpdate()
    {
        if (item == null || !item.InHand || item.Owner is not Player player || !player.IsLocalProfile)
        {
            continuousUseArmed = false;
            ownerController = null;
            targetOutline?.Hide();
            crackOverlay?.Hide();
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
        if (controller == null || item == null || !item.InHand)
            return;

        if (!GroundTileHarvestSystem.TryResolveTarget(this, out _, out _, out _, out string failureReason))
        {
            if (showFailureFeedback && !string.IsNullOrEmpty(failureReason))
                ItemActionFeedback.Show(item.Owner, failureReason);
            return;
        }

        actionAnimation ??= item.itemMods.RequireSingleCapability<IWeaponActionAnimation>();
        if (!actionAnimation.TryRequestAction(queueIfBusy: false))
            return;

        if (!GroundTileHarvestSystem.TryWork(this, out bool completed, out Vector2Int worldCell,
                out _, out failureReason))
        {
            if (showFailureFeedback && !string.IsNullOrEmpty(failureReason))
                ItemActionFeedback.Show(item.Owner, failureReason);
            return;
        }

        HoeTillingFeedback.PlayDigging(item, worldCell);
        UpdateTargetVisuals();
        if (!completed && GroundTileHarvestSystem.TryResolveTarget(this, out RuntimeTerrainTileSample sample,
                out _, out GroundTileHarvestRule rule))
        {
            int requiredUses = GroundTileHarvestSystem.ResolveRequiredUses(this, rule);
            int completedUses = Mathf.CeilToInt(GroundTileHarvestSystem.ReadProgress(sample) * requiredUses);
            ItemActionFeedback.Show(item.Owner, $"挖掘进度：{completedUses}/{requiredUses}");
        }
    }

    /// <summary>白框始终跟随当前指针地格，裂纹只显示可挖地面的累计进度。</summary>
    private void UpdateTargetVisuals()
    {
        if (!GroundTileHarvestSystem.TryResolvePreview(this, out RuntimeTerrainTileSample sample))
        {
            targetOutline?.Hide();
            crackOverlay?.Hide();
            return;
        }

        targetOutline ??= WorldTileTargetOutline.Create("Shovel Ground Target Outline");
        targetOutline.Show(sample.WorldCell);
        if (!GroundTileHarvestSystem.IsHarvestableGround(sample.Cell.GroundTileId))
        {
            crackOverlay?.Hide();
            return;
        }

        float progress = GroundTileHarvestSystem.ReadProgress(sample);
        if (progress <= 0f)
        {
            crackOverlay?.Hide();
            return;
        }

        crackOverlay ??= GroundHarvestCrackOverlay.Create();
        crackOverlay.Show(sample.WorldCell, progress);
    }

    private void ReleaseTargetVisuals()
    {
        if (targetOutline != null) Destroy(targetOutline.gameObject);
        if (crackOverlay != null) Destroy(crackOverlay.gameObject);
        targetOutline = null;
        crackOverlay = null;
    }

    #endregion
}
