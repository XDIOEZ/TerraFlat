using System.Globalization;
using FlatWorld.Spaceflight;

public sealed class Mod_SpaceElevatorTerminal : Module, IInteractable, IBuildingDismantlePolicy,
    IBuildingPlacementRule, IBuildingPlacementCommitted
{
    #region 电梯终端交互
    public const string ModuleId = "太空电梯终端模块";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public Ex_ModData Data = new() { ID = ModuleId };
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    protected override void OnLoad() { }
    protected override void OnSave() { }
    protected override void OnUnload() => SpaceElevatorPanelSession.CloseForTerminal(item?.itemData?.Guid.ToString(CultureInfo.InvariantCulture));
    public bool CanInteract(Item actor) => actor is Player && item != null && item.Owner == null;
    public void OnInteractStart(Item actor)
    {
        // 地面和站台共用入口，运输与库存规则只由电梯会话执行。
        if (CanInteract(actor)) SpaceElevatorPanelSession.Open(SpaceSession.EnsureLoaded(), (Player)actor, item);
    }
    public void OnInteractCancel(Item actor) { }
    public bool CanDismantle(out string reason)
    {
        reason = null;
        return SpaceSession.Current == null || item?.itemData == null ||
            SpaceSession.Current.CanDismantleSpaceElevatorTerminal(item.itemData.Guid.ToString(CultureInfo.InvariantCulture), out reason);
    }

    public bool ValidateBuildingPlacement(Item actor, UnityEngine.Vector3 position, out string reason)
    {
        string world = actor != null ? actor.gameObject.scene.name : item?.gameObject.scene.name;
        return SpaceSession.ValidateSpaceElevatorPlacement(world,
            WorldLocalPresentation.ToLogical(position), out reason);
    }

    public void OnBuildingPlacementCommitted()
    {
        if (item?.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.IsInstalled() != true) return;
        SpaceSession session = SpaceSession.EnsureLoaded();
        if (!session.TryCreateSpaceElevator(item, out _, out string reason))
            UnityEngine.Debug.LogError("[太空电梯] 放置后的站台创建失败：" + reason);
    }
    #endregion
}
