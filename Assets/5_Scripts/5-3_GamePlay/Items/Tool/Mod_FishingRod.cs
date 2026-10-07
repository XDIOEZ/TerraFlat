using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>独立钓具模块：左键投出挂载的一份食物，右键配置鱼钩与单份鱼饵。</summary>
public sealed partial class Mod_FishingRod : Module
{
    #region 钓具与持久化
    public const string ModuleId = "Mod_FishingRod";
    public const string HookTag = "FishingHook";
    public static readonly string[] FoodTags = { "Food", "47", "Meat", "Worm" };
    [Min(0.5f)] public float maxCastDistance = 10f;
    [Min(0.5f)] public float castSpeed = 12f;
    [Min(0.1f)] public float reelSpeed = 8f;
    public LineRenderer fishingLine;
    public Ex_ModData_MemoryPackable Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

    [MemoryPackable]
    public partial class RigState
    {
        public InventoryInstanceSnapshot Inventory;
    }

    [Serializable]
    public sealed class RigInventory : Inventory
    {
        public override void InitData()
        {
            base.InitData();
            if (Data.itemSlots.Count != 2) throw new InvalidOperationException("钓竿必须恰好有鱼钩、鱼饵两个槽位。");
            Data.SetUnlimitedSlots(false);
            Data.SetUnlimitedStackSize(false);
            for (int index = 0; index < 2; index++) Data.itemSlots[index].SlotMaxVolume = 1f;
            Data.itemSlots[0].CanAcceptTags = new List<string> { HookTag };
            Data.itemSlots[1].CanAcceptTags = new List<string>(FoodTags);
        }
    }

    private RigState state;
    private RigInventory rig;
    private Mod_GameController controller;
    private BasePanel panel;
    private FishingRodPanelBindings panelBindings;
    private bool loaded;
    private DroppedItemHandle castBait;
    private int castBaitLegacyGuid;
    private Mod_AI_Fish hookedFish;
    private bool hasCast, reeling;
    private Vector2 castOrigin, castDelta;
    private float castDuration, castElapsed;
    private Mod_Projectile.TrajectorySettings trajectory;
    private readonly Vector3[] linePoints = new Vector3[2];
    private static readonly HashSet<Mod_FishingRod> activeRods = new();
    public static event Action<Item, string> FeedbackRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeLines()
    {
        activeRods.Clear();
        FeedbackRequested = null;
    }

    public Inventory_Data RigData => rig?.Data;
    private ItemSlot HookSlot => rig.Data.itemSlots[0];
    private ItemSlot BaitSlot => rig.Data.itemSlots[1];
    #endregion

    #region 生命周期与输入
    public override void Awake()
    {
        Data.ID = ModuleId;
        base.Awake();
    }

    protected override void OnLoad()
    {
        bool hasSavedState = Data.BitData != null && Data.BitData.Length > 0;
        state = hasSavedState ? Data.GetData<RigState>() : new RigState
        {
            Inventory = InventoryInstanceSnapshot.Capture(CreateRigInventoryData())
        };
        if (state?.Inventory == null || state.Inventory.SlotCount != 2)
            throw new InvalidOperationException("钓竿库存实例快照必须恰好有两个槽位。");
        rig ??= new RigInventory();
        rig.Data ??= CreateRigInventoryData();
        // 只合并实例内容，钓具当前布局和同 GUID 物品引用继续复用。
        state.Inventory.RestoreTo(rig.Data,
            data => data.SharedConfiguration == null
                ? ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, data) : data);
        foreach (ItemSlot slot in rig.Data.itemSlots)
        {
            if (slot == null) throw new InvalidOperationException("钓竿存档包含空槽位记录。");
            if (slot.itemData?.Stack != null && slot.itemData.Stack.Amount > 1f)
                throw new InvalidOperationException("钓竿槽位最多装一件物品；拒绝丢弃超量存档中的物品。");
        }
        rig.item = item;
        rig.InitData();
        rig.Data.Event_OnDataChanged += HandleRigChanged;
        item.OnAct += OpenConfiguration;
        item.OnInHandChanged += HandleInHandChanged;
        loaded = true;
        hasCast = reeling = false;
        hookedFish = null;
        if (fishingLine != null) fishingLine.enabled = false;
        BindController();
    }

    protected override void OnSave()
    {
        if (state == null || rig?.Data == null) return;
        state.Inventory = InventoryInstanceSnapshot.Capture(rig.Data);
        Data.WriteData(state);
    }

    protected override void OnUnload()
    {
        loaded = false;
        CancelLine();
        if (item != null)
        {
            item.OnAct -= OpenConfiguration;
            item.OnInHandChanged -= HandleInHandChanged;
        }
        if (controller != null)
        {
            controller.AttackStarted -= HandleAttack;
            controller.ReleaseGameplayInputLock(this);
        }
        controller = null;
        if (rig != null)
        {
            rig.Data.Event_OnDataChanged -= HandleRigChanged;
            rig.UnbindController();
            rig.UnbindRuntimeDataEvents();
            rig.UnbindPlayerCarryWeightEvents();
            rig.DefaultTarget_Inventory = null;
        }
        if (panel != null)
        {
            panel.Closed -= HandlePanelClosed;
            if (panelBindings != null && panelBindings.CloseButton != null)
                panelBindings.CloseButton.onClick.RemoveListener(CloseConfiguration);
            panel.Close();
            UIManager.ExistingInstance?.DestroyPanel(panel);
        }
        panel = null;
        panelBindings = null;
        if (rig != null)
        {
            rig.basePanel = null;
            rig.SyncQuickTransferTarget();
            rig.item = null;
        }
        state = null;
    }

    private static Inventory_Data CreateRigInventoryData() => new(
        new List<ItemSlot> { new(0), new(1) }, "钓竿配置");

    private void HandleInHandChanged(bool inHand)
    {
        if (!inHand) { CloseConfiguration(); CancelLine(); }
        BindController();
    }

    private void BindController()
    {
        Mod_GameController next = item != null && item.InHand
            ? item.Owner?.itemMods?.GetMod_ByID<Mod_GameController>(ModText.Controller) : null;
        if (next == controller) return;
        if (controller != null)
        {
            controller.AttackStarted -= HandleAttack;
            controller.ReleaseGameplayInputLock(this);
        }
        controller = next;
        if (controller != null) controller.AttackStarted += HandleAttack;
    }

    private void HandleAttack()
    {
        if (!loaded || !GameNetwork.HasStateAuthority || controller == null || !item.InHand ||
            controller.IsGameplayInputLocked || (panel != null && panel.IsOpen())) return;
        if (hookedFish != null)
        {
            reeling = true;
            return;
        }
        if (hasCast) { CancelLine(); return; }
        TryCast(controller.GetAimWorldPosition(maxCastDistance));
    }
    #endregion

    #region 抛饵与收线
    private void TryCast(Vector2 target)
    {
        if (HookSlot.itemData?.Tags?.Contains(HookTag) != true)
        { ShowFeedback("请先在钓竿上安装鱼钩。"); return; }
        ItemData bait = BaitSlot.itemData;
        if (bait?.Stack == null || bait.Stack.Amount != 1f || !HasFoodPayload(bait))
        { ShowFeedback("请先在鱼钩上挂一份食物作为鱼饵。"); return; }
        Vector2 origin = item.Owner.transform.position;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(origin, target);
        if (!float.IsFinite(delta.x) || !float.IsFinite(delta.y)) return;
        delta = Vector2.ClampMagnitude(delta, maxCastDistance);
        Vector2 end = WorldTopologyRuntime.NormalizePosition(origin + delta);
        if (delta.magnitude < 0.5f || !AquaticHabitat.CanSwimAt(end))
        { ShowFeedback("请瞄准投掷距离以内的水面。"); return; }

        float duration = delta.magnitude / Mathf.Max(0.5f, castSpeed);
        // 使用投石索共用的抛物线公式，但鱼饵不挂伤害模块，也不成为攻击投射物。
        var path = new Mod_Projectile.TrajectorySettings(castSpeed, castSpeed, duration, 12f, true);
        Vector2 midpoint = path.EvaluateVisibleTrajectoryPoint(origin, delta.normalized, 1f, 0.5f);
        float arc = Mathf.Max(0f, midpoint.y - (origin.y + delta.y * 0.5f));
        DroppedItemHandle spawned = DroppedItemService.Spawn(bait, origin, end, duration,
            bezierOffset: 0f, arcHeight: arc, rotationSpeed: 0f);
        if (!spawned.IsValid) return;
        if (!rig.Data.TryConsumeFromSlot(BaitSlot, 1, out _))
        {
            DroppedItemService.Remove(spawned);
            return;
        }
        castBait = spawned;
        castBaitLegacyGuid = spawned.Legacy != null ? spawned.Legacy.itemData.Guid : 0;
        castOrigin = origin;
        castDelta = delta;
        castDuration = duration;
        castElapsed = 0f;
        trajectory = path;
        hasCast = true;
        reeling = false;
        activeRods.Add(this);
        ShowFeedback("鱼饵已抛出；小鱼吃饵后，左键收线。");
        Save();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
    }

    private static bool HasFoodPayload(ItemData data)
    {
        if (data?.ModuleDataDic == null) return false;
        foreach (ModuleData module in data.ModuleDataDic.Values)
            if (module is ModData_FoodData food && food.EnsureFoodData()?.nutrition != null) return true;
        return false;
    }

    /// <summary>只有食物原子消耗成功才通知钓线，不用距离碰撞猜测咬钩。</summary>
    public static void NotifyBaitEaten(DroppedItemHandle bait, Mod_AI_Fish fish, int legacyGuid)
    {
        foreach (Mod_FishingRod rod in activeRods)
        {
            if (rod == null || !rod.loaded || !rod.hasCast || rod.castBait.Id != bait.Id ||
                rod.castBait.Epoch != bait.Epoch || rod.castBait.Legacy != bait.Legacy ||
                rod.castBaitLegacyGuid != legacyGuid || rod.hookedFish != null) continue;
            if (!fish.TryHook(rod)) return;
            rod.hookedFish = fish;
            rod.castBait = default;
            rod.ShowFeedback("小鱼咬钩了！左键将它拉近。");
            return;
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!loaded) return;
        BindController();
        if (!hasCast) return;
        if (item.Owner == null || !item.InHand) { CancelLine(); return; }
        Vector2 endpoint;
        if (hookedFish != null)
        {
            if (!hookedFish.IsHookedBy(this)) { CancelLine(); return; }
            endpoint = hookedFish.ActorItem.transform.position;
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(endpoint, item.Owner.transform.position);
            if (delta.magnitude > maxCastDistance * 1.5f) { CancelLine(); return; }
            if (reeling && GameNetwork.HasStateAuthority)
            {
                endpoint = WorldTopologyRuntime.NormalizePosition(endpoint + Vector2.ClampMagnitude(delta, reelSpeed * deltaTime));
                hookedFish.PullByLine(this, endpoint);
                if (delta.magnitude <= 0.45f) { CancelLine(); return; }
            }
        }
        else
        {
            if (!castBait.IsValid || (castBait.Legacy != null && castBait.Legacy.itemData?.Guid != castBaitLegacyGuid))
            { CancelLine(); return; }
            castElapsed += Mathf.Max(0f, deltaTime);
            if (!DroppedItemService.TryGetPickablePosition(castBait, out endpoint))
                endpoint = trajectory.EvaluateVisibleTrajectoryPoint(castOrigin, castDelta.normalized, 1f,
                    Mathf.Clamp01(castElapsed / Mathf.Max(0.05f, castDuration)));
            if (WorldTopologyRuntime.SqrDistance(item.Owner.transform.position, endpoint) > maxCastDistance * maxCastDistance * 2.25f)
            { CancelLine(); return; }
        }
        if (fishingLine != null)
        {
            Vector3 start = item.Sprite != null ? item.Sprite.bounds.center : item.transform.position;
            Vector2 nearEnd = WorldTopologyRuntime.NearestImagePosition(start, endpoint);
            linePoints[0] = start;
            linePoints[1] = new Vector3(nearEnd.x, nearEnd.y, start.z);
            fishingLine.positionCount = 2;
            fishingLine.SetPositions(linePoints);
            fishingLine.enabled = true;
        }
    }

    private void CancelLine()
    {
        hookedFish?.ReleaseLine(this);
        hookedFish = null;
        castBait = default;
        castBaitLegacyGuid = 0;
        hasCast = reeling = false;
        activeRods.Remove(this);
        if (fishingLine != null) fishingLine.enabled = false;
        // 抛出的食物已经属于世界。换工具、回池、存档都不会把它复制回鱼饵槽。
    }
    #endregion

    #region 配置面板
    private void OpenConfiguration()
    {
        BindController();
        if (!loaded || item.Owner == null || !item.InHand || controller == null) return;
        if (hasCast) { ShowFeedback("请先收线，再更换鱼钩和鱼饵。"); return; }
        if (panel == null)
        {
            GameObject prefab = GameRes.Instance.GetPrefab("UI_FishingRod");
            if (prefab == null) throw new InvalidOperationException("钓竿缺少正式配置面板 UI_FishingRod。");
            panel = UIManager.Instance.CreatePanelFromGameObject(prefab);
            panelBindings = panel.GetComponent<FishingRodPanelBindings>();
            if (panelBindings == null) throw new InvalidOperationException("钓竿面板缺少序列化控件引用。");
            rig.basePanel = panel;
            rig.BindSlotUI(panelBindings.HookSlot, 0);
            rig.BindSlotUI(panelBindings.BaitSlot, 1);
            panel.SetGameplayInputBlocking(true);
            panel.Closed += HandlePanelClosed;
            panelBindings.CloseButton.onClick.AddListener(CloseConfiguration);
            panel.Close();
        }
        rig.DefaultTarget_Inventory = item.Owner.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        panel.Toggle();
        if (panel.IsOpen()) controller.AcquireGameplayInputLock(this);
        rig.SyncQuickTransferTarget(panel);
        RefreshPanel();
    }

    private void CloseConfiguration()
    {
        // Unity 已销毁的面板仍有托管引用，关闭前必须使用 Unity 判空。
        if (panel != null)
            panel.Close();
        else
            HandlePanelClosed();
    }

    private void HandlePanelClosed()
    {
        if (controller != null) controller.ReleaseGameplayInputLock(this);
        if (rig == null) return;
        rig.DefaultTarget_Inventory = null;
        rig.SyncQuickTransferTarget(panel);
    }

    private void HandleRigChanged(ItemSlot slot)
    {
        RefreshPanel();
        Save();
        if (GameNetwork.HasStateAuthority) ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
    }

    private void RefreshPanel()
    {
        if (panelBindings == null) return;
        rig.RefreshUI();
        panelBindings.Status.text = FlatWorldLocalizationService.GetUiFormat(
            "抛投距离：{0:0.#} 格   收线速度：{1:0.#} 格/秒\n鱼钩：{2}   鱼饵：{3}\n鱼钩与鱼饵各限一件；左键抛饵，咬钩后左键收线。",
            maxCastDistance, reelSpeed,
            HookSlot.itemData?.GameName ?? FlatWorldLocalizationService.GetUiText("未安装"),
            BaitSlot.itemData?.GameName ?? FlatWorldLocalizationService.GetUiText("未挂饵"));
    }

    private void ShowFeedback(string text)
    {
        string localized = FlatWorldLocalizationService.GetUiText(text);
        if (panelBindings != null && panel.IsOpen()) panelBindings.Status.text = localized;
        FeedbackRequested?.Invoke(item.Owner, localized);
    }
    #endregion
}
