using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>钓鱼声效动作，只通知实际发生的抛饵、咬钩和首次收线。</summary>
public enum FishingSoundAction { Cast, Hook, Reel }

/// <summary>允许鱼类与可钓小鸟共用钓线挂接、牵引和释放。</summary>
public interface IFishingLineCatchTarget
{
    Item ActorItem { get; }
    string HookedFeedback { get; }
    bool CanBeHookedByCast { get; }
    bool TryHook(Mod_FishingRod rod);
    bool IsHookedBy(Mod_FishingRod rod);
    void PullByLine(Mod_FishingRod rod, Vector2 position);
    void ReleaseLine(Mod_FishingRod rod);
}

/// <summary>独立钓具模块：左键投出挂载的一份食物，右键配置鱼钩与单份鱼饵。</summary>
public sealed partial class Mod_FishingRod : Module
{
    #region 钓具与持久化
    public const string ModuleId = "Mod_FishingRod";
    public const string HookTag = "FishingHook";
    private const float HookDurabilityPerCast = 1f;
    public static readonly string[] FoodTags = { "Food", "47", "Meat", "Worm" };
    [Min(0.5f)] public float maxCastDistance = 10f;
    [Min(0.5f)] public float castSpeed = 12f;
    [Min(0.1f)] public float reelSpeed = 8f;
    public LineRenderer fishingLine;
    public Sprite castSprite;
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

        /// <summary>鱼饵成功生成后提交本次耗材；鱼钩扣耐久失败时原样退还鱼饵。</summary>
        public bool TryConsumeCastSupplies(ItemData expectedHook, ItemData expectedBait, out bool hookBroke)
        {
            hookBroke = false;
            if (Data?.itemSlots?.Count != 2) return false;
            ItemSlot hookSlot = Data.itemSlots[0], baitSlot = Data.itemSlots[1];
            if (!ReferenceEquals(hookSlot?.itemData, expectedHook) ||
                !ReferenceEquals(baitSlot?.itemData, expectedBait) || !IsUsableHook(expectedHook) ||
                expectedBait?.Stack?.Amount != 1f || !HasFoodPayload(expectedBait)) return false;
            if (!Data.TryConsumeFromSlot(baitSlot, 1, out ItemData consumedBait)) return false;
            if (Data.TryConsumeDurability(expectedHook, HookDurabilityPerCast, out hookBroke)) return true;
            if (!Data.TrySetSlotItemAmount(baitSlot, consumedBait, 1f))
                throw new InvalidOperationException("抛竿事务失败后无法退还原鱼饵。");
            return false;
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
    private IFishingLineCatchTarget hookedTarget;
    private ItemData castBaitPayload;
    private bool hasCast, reeling;
    private bool castVisualApplied;
    private Sprite castSourceSprite;
    private readonly List<SpriteRenderer> castHiddenRenderers = new();
    private readonly List<bool> castHiddenRendererStates = new();
    private Vector2 castOrigin, castDelta;
    private Vector2 castLastPosition;
    private float castDuration, castElapsed;
    private Mod_Projectile.TrajectorySettings trajectory;
    private readonly Vector3[] linePoints = new Vector3[2];
    private readonly List<Item> castCatchCandidates = new();
    private readonly HashSet<Item> castCatchDedupe = new();
    // 锚点对应原始 32×32 贴图的竿梢像素中心，跟随贴图翻面镜像。
    private static readonly Vector2 RodTipSpriteAnchor = new(29.5f / 32f, 29.5f / 32f);
    private static readonly HashSet<Mod_FishingRod> activeRods = new();
    public static event Action<Item, string> FeedbackRequested;
    public static event Action<Player, FishingSoundAction> SoundRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeLines()
    {
        activeRods.Clear();
        FeedbackRequested = null;
        SoundRequested = null;
    }

    public Inventory_Data RigData => rig?.Data;
    private ItemSlot HookSlot => rig.Data.itemSlots[0];
    private ItemSlot BaitSlot => rig.Data.itemSlots[1];
    #endregion

    #region 手持钓具表现
    private static readonly Vector3 HookVisualPosition = new(0.58f, -0.14f, 0f);
    private static readonly Vector3 BaitVisualPosition = new(0.61f, -0.28f, 0f);
    private const float HookVisualSize = 0.14f;
    private const float BaitVisualSize = 0.14f;
    private Transform rigVisualRoot;
    private SpriteRenderer hookVisualRenderer, baitVisualRenderer;
    private ActorRenderEffectController rigVisualEffects;
    private bool rigVisualPoseInitialized, rigVisualFlipX, rigVisualFlipY, rigVisualColorInitialized;
    private int rigVisualSortingLayerId = int.MinValue, rigVisualSortingOrder = int.MinValue;
    private Color rigVisualColor;

    private void RefreshRigVisuals()
    {
        // 只叠加钓竿配置槽的 Sprite，不复制真实物品或库存数据。
        if (!loaded || item == null || !item.InHand || item.Owner == null)
        {
            HideRigVisuals();
            return;
        }

        GameRes resources = GameRes.ExistingInstance;
        Sprite hookSprite = ResolveRigSprite(resources, HookSlot.itemData);
        Sprite baitSprite = ResolveRigSprite(resources, BaitSlot.itemData);
        if (hookSprite == null && baitSprite == null)
        {
            HideRigVisuals();
            return;
        }

        EnsureRigVisuals();
        rigVisualRoot.gameObject.SetActive(true);
        SetRigVisualSprite(hookVisualRenderer, hookSprite, HookVisualSize);
        SetRigVisualSprite(baitVisualRenderer, baitSprite, BaitVisualSize);
        rigVisualPoseInitialized = false;
        SynchronizeRigVisualPose();
    }

    private static Sprite ResolveRigSprite(GameRes resources, ItemData data)
    {
        if (resources == null || data == null ||
            !resources.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition))
            return null;
        return ItemDataPresentationResolverRegistry.TryResolve(data, out Sprite stateSprite)
            ? stateSprite : definition.Sprite;
    }

    private void EnsureRigVisuals()
    {
        if (rigVisualRoot == null)
        {
            GameObject root = new("FishingRigVisuals");
            rigVisualRoot = root.transform;
            rigVisualRoot.SetParent(item.Sprite != null ? item.Sprite.transform : item.transform, false);
            hookVisualRenderer = CreateRigVisualRenderer("Hook");
            baitVisualRenderer = CreateRigVisualRenderer("Bait");
            rigVisualSortingLayerId = rigVisualSortingOrder = int.MinValue;
        }
        else if (item.Sprite != null && rigVisualRoot.parent != item.Sprite.transform)
            rigVisualRoot.SetParent(item.Sprite.transform, false);

        ActorRenderEffectController nextEffects = item.Owner.GetComponentInChildren<ActorRenderEffectController>(true);
        if (nextEffects == rigVisualEffects) return;
        rigVisualEffects?.UnregisterExternalRenderers(rigVisualRoot);
        rigVisualEffects = nextEffects;
        rigVisualEffects?.RegisterExternalRenderers(rigVisualRoot);
    }

    private SpriteRenderer CreateRigVisualRenderer(string visualName)
    {
        GameObject visual = new(visualName);
        visual.transform.SetParent(rigVisualRoot, false);
        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.enabled = false;
        if (item.Sprite != null) renderer.sharedMaterial = item.Sprite.sharedMaterial;
        return renderer;
    }

    private static void SetRigVisualSprite(SpriteRenderer renderer, Sprite sprite, float maximumSize)
    {
        renderer.sprite = sprite;
        renderer.enabled = sprite != null;
        if (sprite == null) return;
        float extent = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        renderer.transform.localScale = Vector3.one * (extent > 0f ? maximumSize / extent : 1f);
    }

    private void SynchronizeRigVisualPose()
    {
        SpriteRenderer rodRenderer = item != null ? item.Sprite : null;
        if (rigVisualRoot == null || !rigVisualRoot.gameObject.activeSelf || rodRenderer == null) return;

        bool flipX = rodRenderer.flipX, flipY = rodRenderer.flipY;
        if (!rigVisualPoseInitialized || flipX != rigVisualFlipX || flipY != rigVisualFlipY)
        {
            hookVisualRenderer.transform.localPosition = ApplyRigFlip(HookVisualPosition, flipX, flipY);
            baitVisualRenderer.transform.localPosition = ApplyRigFlip(BaitVisualPosition, flipX, flipY);
            hookVisualRenderer.flipX = baitVisualRenderer.flipX = flipX;
            hookVisualRenderer.flipY = baitVisualRenderer.flipY = flipY;
            rigVisualFlipX = flipX;
            rigVisualFlipY = flipY;
            rigVisualPoseInitialized = true;
        }

        if (rigVisualSortingLayerId != rodRenderer.sortingLayerID)
        {
            hookVisualRenderer.sortingLayerID = baitVisualRenderer.sortingLayerID = rodRenderer.sortingLayerID;
            rigVisualSortingLayerId = rodRenderer.sortingLayerID;
        }

        if (rigVisualSortingOrder != rodRenderer.sortingOrder)
        {
            hookVisualRenderer.sortingOrder = rodRenderer.sortingOrder + 1;
            baitVisualRenderer.sortingOrder = rodRenderer.sortingOrder + 2;
            rigVisualSortingOrder = rodRenderer.sortingOrder;
        }

        if (!rigVisualColorInitialized || rigVisualColor != rodRenderer.color)
        {
            hookVisualRenderer.color = baitVisualRenderer.color = rodRenderer.color;
            rigVisualColor = rodRenderer.color;
            rigVisualColorInitialized = true;
        }
    }

    private static Vector3 ApplyRigFlip(Vector3 position, bool flipX, bool flipY)
    {
        if (flipX) position.x = -position.x;
        if (flipY) position.y = -position.y;
        return position;
    }

    private void HideRigVisuals()
    {
        if (rigVisualRoot != null) rigVisualRoot.gameObject.SetActive(false);
        rigVisualEffects?.UnregisterExternalRenderers(rigVisualRoot);
        rigVisualEffects = null;
    }

    private void DestroyRigVisuals()
    {
        if (rigVisualRoot != null)
        {
            HideRigVisuals();
            Destroy(rigVisualRoot.gameObject);
        }
        rigVisualRoot = null;
        hookVisualRenderer = baitVisualRenderer = null;
        rigVisualEffects = null;
        rigVisualPoseInitialized = false;
        rigVisualSortingLayerId = rigVisualSortingOrder = int.MinValue;
        rigVisualColorInitialized = false;
    }
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
        hookedTarget = null;
        if (fishingLine != null) fishingLine.enabled = false;
        BindController();
        RefreshRigVisuals();
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
        DestroyRigVisuals();
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
        RefreshRigVisuals();
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
        if (hookedTarget != null)
        {
            if (!reeling)
            {
                reeling = true;
                RaiseSound(FishingSoundAction.Reel);
            }
            return;
        }
        if (hasCast) { CancelLine(); return; }
        TryCast(controller.GetAimWorldPosition(maxCastDistance));
    }
    #endregion

    #region 抛饵与收线
    private void TryCast(Vector2 target)
    {
        ItemData hook = HookSlot.itemData;
        if (!IsUsableHook(hook))
        { ShowFeedback("请先在钓竿上安装鱼钩。"); return; }
        ItemData bait = BaitSlot.itemData;
        if (bait?.Stack == null || bait.Stack.Amount != 1f || !HasFoodPayload(bait))
        { ShowFeedback("请先在鱼钩上挂一份食物作为鱼饵。"); return; }
        Vector2 origin = item.Owner.transform.position;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(origin, target);
        if (!float.IsFinite(delta.x) || !float.IsFinite(delta.y)) return;
        delta = Vector2.ClampMagnitude(delta, maxCastDistance);
        Vector2 end = WorldTopologyRuntime.NormalizePosition(origin + delta);
        if (delta.magnitude < 0.5f)
        { ShowFeedback("请瞄准投掷距离以内的地点。"); return; }

        float duration = delta.magnitude / Mathf.Max(0.5f, castSpeed);
        ItemData returnBait = FastCloner.FastCloner.DeepClone(bait);
        returnBait.Stack.Amount = 1f;
        // 使用投石索共用的抛物线公式，但鱼饵不挂伤害模块，也不成为攻击投射物。
        var path = new Mod_Projectile.TrajectorySettings(castSpeed, castSpeed, duration, 12f, true);
        Vector2 midpoint = path.EvaluateVisibleTrajectoryPoint(origin, delta.normalized, 1f, 0.5f);
        float arc = Mathf.Max(0f, midpoint.y - (origin.y + delta.y * 0.5f));
        DroppedItemHandle spawned = DroppedItemService.Spawn(bait, origin, end, duration,
            bezierOffset: 0f, arcHeight: arc, rotationSpeed: 0f);
        if (!spawned.IsValid) return;
        bool suppliesCommitted = false;
        try
        {
            suppliesCommitted = rig.TryConsumeCastSupplies(hook, bait, out _);
        }
        finally
        {
            if (!suppliesCommitted) DroppedItemService.Remove(spawned);
        }
        if (!suppliesCommitted) return;
        castBait = spawned;
        castBaitPayload = returnBait;
        castBaitLegacyGuid = spawned.Legacy != null ? spawned.Legacy.itemData.Guid : 0;
        castOrigin = origin;
        castDelta = delta;
        castLastPosition = origin;
        castDuration = duration;
        castElapsed = 0f;
        trajectory = path;
        // 最后一点耐久消耗后鱼钩清槽，但本次已成功抛出的钓线仍可完成收线。
        hasCast = true;
        reeling = false;
        ApplyCastSprite();
        activeRods.Add(this);
        ShowFeedback("鱼饵已抛出；小鱼吃饵或鱼钩勾到小鸟后，左键收线。");
        Save();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        RaiseSound(FishingSoundAction.Cast);
    }

    private static bool HasFoodPayload(ItemData data)
    {
        if (data?.ModuleDataDic == null) return false;
        foreach (ModuleData module in data.ModuleDataDic.Values)
            if (module is ModData_FoodData food && food.EnsureFoodData()?.nutrition != null) return true;
        return false;
    }

    private static bool IsUsableHook(ItemData hook) => hook?.Tags?.Contains(HookTag) == true &&
        hook.Stack?.Amount == 1f && ItemDurabilityRules.CanConsume(hook, HookDurabilityPerCast);

    /// <summary>食物原子消耗成功后通知匹配的钓线目标。</summary>
    public static void NotifyBaitEaten(DroppedItemHandle bait, IFishingLineCatchTarget target, int legacyGuid)
    {
        foreach (Mod_FishingRod rod in activeRods)
        {
            if (rod == null || !rod.loaded || !rod.hasCast || rod.castBait.Id != bait.Id ||
                rod.castBait.Epoch != bait.Epoch || rod.castBait.Legacy != bait.Legacy ||
                rod.castBaitLegacyGuid != legacyGuid || rod.hookedTarget != null) continue;
            if (!target.TryHook(rod)) return;
            rod.SetHookedTarget(target, target.HookedFeedback);
            return;
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!loaded) return;
        BindController();
        SynchronizeRigVisualPose();
        if (!hasCast) return;
        if (item.Owner == null || !item.InHand) { CancelLine(); return; }
        Vector2 endpoint;
        if (hookedTarget != null)
        {
            if (!hookedTarget.IsHookedBy(this) || hookedTarget.ActorItem == null) { CancelLine(); return; }
            endpoint = hookedTarget.ActorItem.transform.position;
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(endpoint, item.Owner.transform.position);
            if (delta.magnitude > maxCastDistance * 1.5f) { CancelLine(); return; }
            if (reeling && GameNetwork.HasStateAuthority)
            {
                endpoint = WorldTopologyRuntime.NormalizePosition(endpoint + Vector2.ClampMagnitude(delta, reelSpeed * deltaTime));
                hookedTarget.PullByLine(this, endpoint);
                if (delta.magnitude <= 0.45f) { CancelLine(); return; }
            }
        }
        else
        {
            if (!castBait.IsValid || (castBait.Legacy != null && castBait.Legacy.itemData?.Guid != castBaitLegacyGuid))
            { CancelLine(); return; }
            float previousCastElapsed = castElapsed;
            castElapsed += Mathf.Max(0f, deltaTime);
            if (!DroppedItemService.TryGetPickablePosition(castBait, out endpoint))
                endpoint = trajectory.EvaluateVisibleTrajectoryPoint(castOrigin, castDelta.normalized, 1f,
                    Mathf.Clamp01(castElapsed / Mathf.Max(0.05f, castDuration)));
            if (WorldTopologyRuntime.SqrDistance(item.Owner.transform.position, endpoint) > maxCastDistance * maxCastDistance * 2.25f)
            { CancelLine(); return; }
            if (previousCastElapsed < castDuration &&
                TryHookTargetAlongCast(castLastPosition, endpoint, out IFishingLineCatchTarget castTarget))
            {
                if (castBait.IsValid) DroppedItemService.Remove(castBait);
                SetHookedTarget(castTarget, castTarget.HookedFeedback);
                endpoint = castTarget.ActorItem.transform.position;
            }
            castLastPosition = endpoint;
        }
        if (fishingLine != null)
        {
            Vector3 start = GetRodTipWorldPosition();
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
        bool returnedBait = hookedTarget == null && TryReturnCastBait();
        hookedTarget?.ReleaseLine(this);
        hookedTarget = null;
        castBait = default;
        castBaitLegacyGuid = 0;
        castBaitPayload = null;
        hasCast = reeling = false;
        activeRods.Remove(this);
        if (fishingLine != null) fishingLine.enabled = false;
        RestoreCastSprite();
        if (returnedBait)
        {
            Save();
            if (GameNetwork.HasStateAuthority) ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        }
    }

    private bool TryReturnCastBait()
    {
        if (castBaitPayload?.Stack == null || !castBait.IsValid ||
            (castBait.Legacy != null && castBait.Legacy.itemData?.Guid != castBaitLegacyGuid) ||
            BaitSlot.itemData != null) return false;

        ItemData returned = FastCloner.FastCloner.DeepClone(castBaitPayload);
        returned.Stack.Amount = 1f;
        DroppedItemService.Remove(castBait);
        if (rig.Data.TrySetSlotItemAmount(BaitSlot, returned, 1f)) return true;

        // 若槽位在回收事务中被占用，就把鱼饵放回钩点，避免复制或丢失。
        try
        {
            DroppedItemService.Spawn(returned, castLastPosition, destination: castLastPosition,
                duration: 0f, arcHeight: 0f, rotationSpeed: 0f, randomizeRotation: false);
        }
        catch (Exception exception) { Debug.LogException(exception); }
        return false;
    }

    private bool TryHookTargetAlongCast(Vector2 from, Vector2 to, out IFishingLineCatchTarget hooked)
    {
        hooked = null;
        if (!GameNetwork.HasStateAuthority || ItemMgr.Instance == null) return false;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(from, to);
        const float hookRadius = 0.3f;
        Vector2 midpoint = WorldTopologyRuntime.NormalizePosition(from + delta * 0.5f);
        float queryRadius = delta.magnitude * 0.5f + hookRadius;
        ItemMgr.Instance.QueryItemsInCircleNonAlloc(midpoint, queryRadius, ~0, item.Owner,
            castCatchCandidates, castCatchDedupe);

        float nearest = hookRadius * hookRadius;
        IFishingLineCatchTarget nearestTarget = null;
        foreach (Item candidate in castCatchCandidates)
        {
            if (candidate?.itemMods == null || candidate.DestructionHandled) continue;
            IReadOnlyList<IFishingLineCatchTarget> targets = candidate.itemMods.GetCapabilities<IFishingLineCatchTarget>();
            for (int index = 0; index < targets.Count; index++)
            {
                IFishingLineCatchTarget target = targets[index];
                if (target == null || target.ActorItem == null || !target.CanBeHookedByCast) continue;
                Vector2 targetPosition = WorldTopologyRuntime.NearestImagePosition(from, target.ActorItem.transform.position);
                float progress = delta.sqrMagnitude > 0.0001f
                    ? Mathf.Clamp01(Vector2.Dot(targetPosition - from, delta) / delta.sqrMagnitude) : 1f;
                float distance = (targetPosition - (from + delta * progress)).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                nearestTarget = target;
            }
        }
        castCatchCandidates.Clear();
        castCatchDedupe.Clear();
        if (nearestTarget == null || !nearestTarget.TryHook(this)) return false;
        hooked = nearestTarget;
        return true;
    }

    private void SetHookedTarget(IFishingLineCatchTarget target, string feedback)
    {
        hookedTarget = target;
        castBait = default;
        castBaitLegacyGuid = 0;
        castBaitPayload = null;
        ShowFeedback(feedback);
        RaiseSound(FishingSoundAction.Hook);
    }

    private Vector3 GetRodTipWorldPosition()
    {
        SpriteRenderer renderer = item != null ? item.Sprite : null;
        Sprite sprite = renderer != null ? renderer.sprite : null;
        if (sprite == null) return item != null ? item.transform.position : transform.position;

        Bounds bounds = sprite.bounds;
        Vector2 anchor = RodTipSpriteAnchor;
        if (renderer.flipX) anchor.x = 1f - anchor.x;
        if (renderer.flipY) anchor.y = 1f - anchor.y;
        Vector3 localPosition = new(
            bounds.min.x + bounds.size.x * anchor.x,
            bounds.min.y + bounds.size.y * anchor.y,
            0f);
        return renderer.transform.TransformPoint(localPosition);
    }

    private void ApplyCastSprite()
    {
        SpriteRenderer renderer = item != null ? item.Sprite : null;
        if (renderer == null || castSprite == null || castVisualApplied) return;
        castSourceSprite = renderer.sprite;
        renderer.sprite = castSprite;
        HideAttachedRenderers(renderer);
        castVisualApplied = true;
    }

    private void RestoreCastSprite()
    {
        if (!castVisualApplied) return;
        SpriteRenderer renderer = item != null ? item.Sprite : null;
        if (renderer != null && renderer.sprite == castSprite)
            renderer.sprite = castSourceSprite;
        for (int index = 0; index < castHiddenRenderers.Count; index++)
        {
            SpriteRenderer attachedRenderer = castHiddenRenderers[index];
            if (attachedRenderer != null)
                attachedRenderer.enabled = castHiddenRendererStates[index];
        }
        castHiddenRenderers.Clear();
        castHiddenRendererStates.Clear();
        castSourceSprite = null;
        castVisualApplied = false;
    }

    private void HideAttachedRenderers(SpriteRenderer rodRenderer)
    {
        castHiddenRenderers.Clear();
        castHiddenRendererStates.Clear();
        SpriteRenderer[] attachedRenderers = rodRenderer.GetComponentsInChildren<SpriteRenderer>(true);
        for (int index = 0; index < attachedRenderers.Length; index++)
        {
            SpriteRenderer attachedRenderer = attachedRenderers[index];
            if (attachedRenderer == null || attachedRenderer == rodRenderer) continue;
            castHiddenRenderers.Add(attachedRenderer);
            castHiddenRendererStates.Add(attachedRenderer.enabled);
            attachedRenderer.enabled = false;
        }
    }

    // 音效订阅方失败不影响已经完成的鱼饵、鱼钩和收线状态。
    private void RaiseSound(FishingSoundAction action)
    {
        if (item?.Owner is not Player player || SoundRequested == null)
            return;
        foreach (Delegate callback in SoundRequested.GetInvocationList())
        {
            try { ((Action<Player, FishingSoundAction>)callback)(player, action); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
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
        RefreshRigVisuals();
        RefreshPanel();
        Save();
        if (GameNetwork.HasStateAuthority) ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
    }

    private void RefreshPanel()
    {
        if (panelBindings == null) return;
        rig.RefreshUI();
        ItemData hook = HookSlot.itemData;
        string hookStatus = hook != null ? $"{hook.GameName} ({hook.Durability:0}/{hook.MaxDurability:0})"
            : FlatWorldLocalizationService.GetUiText("未安装");
        panelBindings.Status.text = FlatWorldLocalizationService.GetUiFormat(
            "抛投距离：{0:0.#} 格   收线速度：{1:0.#} 格/秒\n鱼钩：{2}   鱼饵：{3}\n任意方向抛投；未钓中目标再按左键收回鱼饵，陆地可勾小鸟。",
            maxCastDistance, reelSpeed,
            hookStatus,
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
