using MemoryPack;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>
/// 锄头持续使用入口；每次真实挥动只结算一次地块进度，地块进度不会随换工具重置。
/// 木、石、铜、铁分别配置 6、4、3、2 次完成，右键/手机长按共用 Mod_GameController 的使用状态。
/// </summary>
public partial class Mod_Hoe : Module, IItemModuleDependencyBinder
{
    #region 数据与配置

    [System.Serializable, MemoryPackable]
    public partial class Mod_Hoe_Data
    {
        public int tilledCount; // 已完成的耕地数
    }

    public Mod_Hoe_Data Data = new();
    public Ex_ModData_MemoryPackable ModData = new();
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModText.Tool;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    [Min(2)] public int usesPerTile = 6; // 从完整土地开始需要的使用次数
    [Min(0.1f)] public float maxTillingDistance = 2f; // 可操作距离
    private bool actBound;
    private WorldTileTargetOutline targetOutline;
    private Mod_GameController ownerController;
    private IWeaponActionAnimation actionAnimation;
    private bool continuousUseArmed;

    #endregion

    #region 生命周期与使用

    public override void Awake() => ModData.ID = ModText.Tool;

    /// <summary>锄地只依赖通用挥动能力，不绑定具体伤害模块或动画实现。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        actionAnimation = modules.RequireSingleCapability<IWeaponActionAnimation>();
    }

    public override void Load()
    {
        ModData.ReadData(ref Data);
        BindAct();
    }

    public override void Save() => ModData.WriteData(Data);

    public override void Unload()
    {
        if (actBound && item != null)
            item.OnAct -= Act;
        actBound = false;
        continuousUseArmed = false;
        ReleaseOutline();
        ownerController = null;
        actionAnimation = null;
    }

    private void OnEnable() => BindAct();
    private void OnDestroy() => Unload();

    /// <summary>热重载后重新建立右键事件，不把输入恢复职责塞进伤害模块。</summary>
    private void BindAct()
    {
        if (actBound || item == null)
            return;
        item.OnAct += Act;
        actBound = true;
    }

    /// <summary>手持时复用装水的单格白框；预览和右键都读取同一锄地资格。</summary>
    private void LateUpdate()
    {
        if (item == null || !item.InHand || item.Owner is not Player player || !player.IsLocalProfile)
        {
            continuousUseArmed = false;
            targetOutline?.Hide();
            ownerController = null;
            return;
        }
        ownerController ??= player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        if (ownerController == null)
        {
            targetOutline?.Hide();
            return;
        }

        if (FarmlandSystem.TryGetTillingTarget(ownerController.GetMouseWorldPosition(),
                player.transform.position, maxTillingDistance, out var sample))
        {
            targetOutline ??= WorldTileTargetOutline.Create("Hoe Tile Target Outline");
            targetOutline.Show(sample.WorldCell);
        }
        else
        {
            targetOutline?.Hide();
        }

        if (!ownerController.IsRightClickHeld)
            continuousUseArmed = false;

        // 只有本次按下已经通过 Item.Act 武装后才续挥，避免切换手持物时继承旧按住状态。
        if (continuousUseArmed)
            TryPerformTillingSwing(ownerController);
    }

    private void OnDisable() => ReleaseOutline();

    private void ReleaseOutline()
    {
        if (targetOutline != null) Destroy(targetOutline.gameObject);
        targetOutline = null;
    }

    /// <summary>单次按下立即尝试第一挥；持续按住由 LateUpdate 按动画节拍续挥。</summary>
    public override void Act()
    {
        if (item?.Owner == null)
            return;

        ownerController ??= item.Owner.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        continuousUseArmed = ownerController?.IsRightClickHeld == true;
        TryPerformTillingSwing(ownerController);
    }

    /// <summary>动画确认这一挥真实开始后，才对当前目标地块结算一次工作量。</summary>
    private void TryPerformTillingSwing(Mod_GameController controller)
    {
        if (!GameNetwork.HasStateAuthority || controller == null || item == null || !item.InHand ||
            item.Owner == null || usesPerTile <= 0)
            return;

        Vector3 pointer = controller.GetMouseWorldPosition();
        if (!FarmlandSystem.TryGetTillingTarget(pointer, item.Owner.transform.position,
                maxTillingDistance, out var target))
            return;

        actionAnimation ??= item.itemMods.RequireSingleCapability<IWeaponActionAnimation>();
        if (!actionAnimation.TryRequestAction(queueIfBusy: false))
            return;

        if (!FarmlandSystem.TryTill(pointer, item.Owner.transform.position,
                maxTillingDistance, 1f / usesPerTile, out bool completed))
            return;

        HoeTillingFeedback.Play(item, target.WorldCell);
        if (completed)
            Data.tilledCount++;
    }

    #endregion
}
