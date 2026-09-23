using MemoryPack;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>
/// 锄头单次使用入口；地块进度由农业系统持有，不会随换工具重置。
/// 木、石、铜、铁分别配置 6、4、3、2 次完成，右键和手机使用共用 Item.OnAct。
/// </summary>
public partial class Mod_Hoe : Module
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
    [Min(0f)] public float useInterval = 0.2f; // 最小使用间隔
    private bool actBound;
    private float nextUseTime;
    private WorldTileTargetOutline targetOutline;
    private GameController ownerController;
    private Mod_Weapon_AnimationAction attackAction;

    #endregion

    #region 生命周期与使用

    public override void Awake() => ModData.ID = ModText.Tool;

    public override void Load()
    {
        ModData.ReadData(ref Data);
        nextUseTime = 0f;
        attackAction = item?.itemMods?.GetMod_ByID<Mod_Weapon_AnimationAction>("Module_Weapon_AnimationAction");
        if (!actBound)
        {
            item.OnAct += Act;
            actBound = true;
        }
    }

    public override void Save() => ModData.WriteData(Data);

    public override void Unload()
    {
        if (actBound && item != null)
            item.OnAct -= Act;
        actBound = false;
        ReleaseOutline();
        ownerController = null;
        attackAction = null;
    }

    private void OnDestroy() => Unload();

    /// <summary>手持时复用装水的单格白框；预览和右键都读取同一锄地资格。</summary>
    private void LateUpdate()
    {
        if (item == null || !item.InHand || item.Owner is not Player player || !player.IsLocalProfile)
        {
            targetOutline?.Hide();
            ownerController = null;
            return;
        }
        ownerController ??= player.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (ownerController == null || !FarmlandSystem.TryGetTillingTarget(ownerController.GetMouseWorldPosition(),
                player.transform.position, maxTillingDistance, out var sample))
        {
            targetOutline?.Hide();
            return;
        }
        targetOutline ??= WorldTileTargetOutline.Create("Hoe Tile Target Outline");
        targetOutline.Show(sample.WorldCell);
    }

    private void OnDisable() => ReleaseOutline();

    private void ReleaseOutline()
    {
        if (targetOutline != null) Destroy(targetOutline.gameObject);
        targetOutline = null;
    }

    /// <summary>真实手持且目标有效时，只累计一次工作量。</summary>
    public override void Act()
    {
        if (!GameNetwork.HasStateAuthority || item == null || !item.InHand ||
            item.Owner == null || Time.time < nextUseTime)
            return;
        GameController controller = item.Owner.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (controller == null)
            return;

        if (!FarmlandSystem.TryGetTillingTarget(controller.GetMouseWorldPosition(), item.Owner.transform.position,
                maxTillingDistance, out var target))
            return;

        if (FarmlandSystem.TryTill(controller.GetMouseWorldPosition(), item.Owner.transform.position,
                maxTillingDistance, 1f / usesPerTile, out bool completed))
        {
            nextUseTime = Time.time + useInterval;
            attackAction ??= item.itemMods?.GetMod_ByID<Mod_Weapon_AnimationAction>("Module_Weapon_AnimationAction");
            attackAction?.RequestAttack();
            HoeTillingFeedback.Play(item, target.WorldCell);
            if (completed)
                Data.tilledCount++;
        }
    }

    #endregion
}
