// AI-Context: 玩家世界物品拾取入口；联机时只发起服务端事务，收到授权后才写入背包，禁止直接 Destroy 绕过 ItemMgr/网络生命周期。
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Audio;
using FlatWorld.Diagnostics;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 物品拾取器组件，用于自动收集可拾取物品
/// </summary>
public partial class Mod_ItemPicker : Module
{
    #region 拾取性能诊断
    // 数量快照与事务次数分开记录，整堆入包不等于逐件创建。
    private static readonly PerformanceDebugPoint PickupAcceptDebug = new("FlatWorld.Pickup.AcceptTransaction");
    private static readonly PerformanceDebugPoint PickupInventoryAddDebug = new("FlatWorld.Pickup.InventoryAdd");
    private static readonly PerformanceDebugPoint PickupRefreshDebug = new("FlatWorld.Pickup.InventoryRefresh");
    private static readonly PerformanceDebugPoint PickupNotifyDebug = new("FlatWorld.Pickup.NotifyCommitted");
    private static readonly PerformanceDebugPoint PickupSourceWholeUnitsDebug = new("FlatWorld.Pickup.SourceStackWholeUnits");
    private static readonly PerformanceDebugPoint PickupAddedWholeUnitsDebug = new("FlatWorld.Pickup.AcceptedWholeUnits");
    private static readonly PerformanceDebugPoint PickupCapacityRejectedDebug = new("FlatWorld.Pickup.CapacityRejectedTransactions");
    private static readonly PerformanceDebugPoint PickupInventoryRejectedDebug = new("FlatWorld.Pickup.InventoryRejectedTransactions");
    #endregion

    private const float InventoryFullPromptCooldownSeconds = 600f;

    #region 基础参数

    // 模板读取早于 Awake，能力 ID 必须在装配前确定。
    public override string CanonicalModuleId => ModText.Picker;
    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    public string[] Data;
    #endregion

    #region 生命周期

    protected override void OnLoad()
    {
        ModSaveData.ReadData(ref Data);
        RebuildTargetInventories();

        // 默认优先级：Hotbar -> Bag
        // 无论列表是否已有配置，都补齐这两个核心容器（内部会去重）
        TryAddInventoryById(ModText.Hotbar);
        TryAddInventoryById(ModText.Bag);
        BindPickupCapacityEvents();
        droppedPickupCollider = GetComponent<Collider2D>();
        if (droppedPickupCollider == null) droppedPickupCollider = GetComponentInChildren<Collider2D>(true);
        DroppedItemService.RegisterPicker(this);
        PickerLoaded?.Invoke(this);
    }

    /// <summary>
    /// 初始化时尝试获取自身的Inventory组件
    /// </summary>
    private void Start()
    {
    }
    protected override void OnSave()
    {
        ModSaveData.WriteData(Data);
    }

    /// <summary>清理碰撞补偿状态，避免物品复用或模块重载后残留旧拾取请求。</summary>
    protected override void OnUnload()
    {
        DroppedItemService.UnregisterPicker(this);
        UnbindPickupCapacityEvents();
        deferredPickupItems.Clear();
        pickupAttemptedItems.Clear();
        PickupAmountMultiplier = 1;
    }

    public override void Act()
    {
        base.Act();
    }
    #endregion


    /// <summary>
    /// 尝试根据模块 ID 获取对应的 Mod_Inventory，并将其 inventory 加入目标列表
    /// </summary>
    /// <param name="modId">ModText 中定义的背包模块 ID</param>
    private void TryAddInventoryById(string modId)
    {
        var module = item.itemMods.GetMod_ByID(modId);
        if (module == null)
            return;

        if (module is not IInventory inventoryProvider)
            return;

        if (!AddTargetInventories.Contains(inventoryProvider))
            AddTargetInventories.Add(inventoryProvider);
    }


    #region 字段与属性

    [Header("目标物品栏（按优先级排列）")]
    [SerializeField]
    public List<Module> AddTargetInventoryModules = new List<Module>(); // 检查器可拖拽的目标库存模块

    [System.NonSerialized]
    public List<IInventory> AddTargetInventories = new List<IInventory>(); // 运行时使用的目标库存接口列表

    [Header("拾取吸入动画")]
    [SerializeField, Min(0.05f)]
    private float pickupSuctionDuration = 0.28f;

    [SerializeField, Min(0f)]
    private float pickupSuctionCurveOffset = 0.3f;

    [SerializeField]
    private float pickupSuctionRotation = 90f;

    /// <summary>
    /// 基础拾取权限控制变量
    /// </summary>
    private bool canPickUp = true;
    private float nextInventoryFullPromptTime = -Mathf.Infinity;
    private readonly HashSet<Item> deferredPickupItems = new HashSet<Item>();
    // 当前接触期间已经发起过的拾取请求。
    private readonly HashSet<Item> pickupAttemptedItems = new HashSet<Item>();
    private Collider2D droppedPickupCollider;

    /// <summary>
    /// 综合判断是否可以拾取物品
    /// 1. 检查基础权限
    /// 2. 检查是否有可用的物品栏
    /// </summary>
    public bool CanPickUp
    {
        get
        {
            if (!canPickUp)
                return false;

            // 只要存在有效背包就允许进入拾取流程。
            // 是否能实际放入（包括满包堆叠）由 TryAddItem 决定。
            foreach (var inventory in AddTargetInventories)
            {
                var targetInventory = inventory?.GetDefaultTargetInventory();
                if (targetInventory != null && targetInventory.Data != null)
                {
                    return true;
                }
            }
            return false;
        }
        set => canPickUp = value;
    }

    #endregion

    #region 拾取数量倍率

    public const int MinPickupAmountMultiplier = 1;
    public const int MaxPickupAmountMultiplier = 100;
    public static event System.Action<Mod_ItemPicker> PickerLoaded;
    private int pickupAmountMultiplier = MinPickupAmountMultiplier;

    /// <summary>外部工具设置拾取收益，只有可堆叠物品使用倍率，库存转移不经过此规则。</summary>
    public int PickupAmountMultiplier
    {
        get => pickupAmountMultiplier;
        set
        {
            int clamped = Mathf.Clamp(value, MinPickupAmountMultiplier, MaxPickupAmountMultiplier);
            if (pickupAmountMultiplier == clamped) return;
            pickupAmountMultiplier = clamped;
            pickupCapacityRevision++;
        }
    }

    #endregion


#region 私有方法

    private void RebuildTargetInventories()
    {
        AddTargetInventories.Clear();

        for (int i = 0; i < AddTargetInventoryModules.Count; i++)
        {
            Module module = AddTargetInventoryModules[i];
            if (module is not IInventory inventoryProvider)
            {
                continue;
            }

            if (!AddTargetInventories.Contains(inventoryProvider))
            {
                AddTargetInventories.Add(inventoryProvider);
            }
        }
    }

#endregion



    #region 物品交互

    /// <summary>
    /// 当有物体进入触发器时尝试拾取物品
    /// </summary>
    /// <param name="other">进入触发器的碰撞体</param>
    private void OnTriggerEnter2D(Collider2D other)
    {
        Item pickAble = GameplayPhysics2D.ResolveComponent<Item>(other);
        if (pickAble == null) return;

        RefreshPickupAttempts();
        deferredPickupItems.Remove(pickAble);
        pickupAttemptedItems.Remove(pickAble);

        if (pickAble.itemData?.Stack != null)
            TryPickUp(pickAble, logMissingInventories: true);
    }

    /// <summary>
    /// 物品可能在掉落飞行期间先进入拾取范围，待恢复可拾取后补做一次拾取。
    /// </summary>
    private void OnTriggerStay2D(Collider2D other)
    {
        Item pickAble = GameplayPhysics2D.ResolveComponent<Item>(other);
        if (pickAble?.itemData?.Stack == null)
            return;

        if (Mod_Droping.IsDropInProgress(pickAble) ||
            !pickAble.itemData.Stack.CanBePickedUp || !CanReachWaterItem(pickAble))
        {
            deferredPickupItems.Add(pickAble);
            pickupAttemptedItems.Remove(pickAble);
            return;
        }

        // 真正具备拾取资格才同步容量修订；飞行中和禁止拾取的物品仍保留补偿队列。
        RefreshPickupAttempts();
        if (deferredPickupItems.Remove(pickAble) ||
            pickupAttemptedItems.Add(pickAble))
            TryPickUp(pickAble, logMissingInventories: false);
    }

    /// <summary>
    /// 离开拾取范围时清理等待补偿拾取的物品。
    /// </summary>
    private void OnTriggerExit2D(Collider2D other)
    {
        Item pickAble = GameplayPhysics2D.ResolveComponent<Item>(other);
        if (pickAble != null)
        {
            deferredPickupItems.Remove(pickAble);
            pickupAttemptedItems.Remove(pickAble);
        }
    }

    /// <summary>
    /// 统一处理进入和持续停留触发器的拾取请求。
    /// </summary>
    private void TryPickUp(Item pickAble, bool logMissingInventories)
    {
        // 检查物品栏列表是否为空
        if (AddTargetInventories.Count == 0)
        {
            if (logMissingInventories)
                Debug.LogWarning($"[{nameof(Mod_ItemPicker)}] AddTargetInventories is empty on {gameObject.name}");
            return;
        }

        // 检查是否具有拾取权限
        if (!CanPickUp)
        {
            return;
        }

        // 已由进入/停留事件解析正式碰撞来源，避免每次接触再次遍历组件层级。
        if (pickAble?.itemData?.Stack == null)
            return;

        if (Mod_Droping.IsDropInProgress(pickAble) ||
            !pickAble.itemData.Stack.CanBePickedUp || !CanReachWaterItem(pickAble))
        {
            deferredPickupItems.Add(pickAble);
            pickupAttemptedItems.Remove(pickAble);
            return;
        }

        deferredPickupItems.Remove(pickAble);
        pickupAttemptedItems.Add(pickAble);
        if (ItemNetworkStateSerialization.BeginNetworkPickup(this, pickAble))
        {
            return;
        }

        pickAble.ModuleSave();
        if (TryAcceptNetworkPickup(pickAble.itemData))
        {
            PlayPickupSuction(pickAble);
            // 只在整组物品都已入包时回收世界物品；部分拾取要保留剩余数量。
            if (!pickAble.itemData.Stack.CanBePickedUp)
            {
                pickupAttemptedItems.Remove(pickAble);
                ItemMgr.Instance.DespawnItem(pickAble);
            }
            return;
        }

        ShowInventoryFullPrompt(pickAble.itemData);
        Debug.Log($"[{nameof(Mod_ItemPicker)}] All target inventories are full, cannot pick up item: {pickAble.itemData.IDName}");
    }

    /// <summary>拾取失败时按触发事件限频提示，不在空闲期间轮询或重复弹窗。</summary>
    private void ShowInventoryFullPrompt(ItemData attemptedItem)
    {
        float now = Time.unscaledTime;
        if (now < nextInventoryFullPromptTime)
            return;

        GameManager gameManager = FindObjectOfType<GameManager>();
        GameSaveStatusHUD statusHUD = gameManager != null
            ? GameSaveStatusHUD.Ensure(gameManager)
            : null;
        if (statusHUD == null)
            return;

        nextInventoryFullPromptTime = now + InventoryFullPromptCooldownSeconds;
        statusHUD.ShowTransientMessage(ResolveInventoryFullMessage(attemptedItem));
    }

    /// <summary>优先告诉玩家具体卡在重量还是体积；纯槽位不足继续使用通用“背包满了”。</summary>
    private string ResolveInventoryFullMessage(ItemData attemptedItem)
    {
        if (item is Player player &&
            PlayerCarryCapacityUtility.TryGetSnapshot(player, out PlayerCarryCapacitySnapshot snapshot) &&
            !snapshot.IsUnlimited)
        {
            bool weightBlocked = snapshot.BlocksByWeight(attemptedItem);
            bool volumeBlocked = snapshot.BlocksByVolume(attemptedItem);
            if (weightBlocked && volumeBlocked)
                return "我的背包重量和体积都到上限了";
            if (weightBlocked)
                return "我的背包太重了";
            if (volumeBlocked)
                return "我的背包没有剩余空间了";
        }

        return "我的背包满了";
    }

    /// <summary>
    /// 网络拾取授权和单机拾取共用的唯一入包入口。
    /// </summary>
    public bool TryAcceptNetworkPickup(ItemData itemData, bool allowPartial = true)
        => TryAcceptNetworkPickup(itemData, out _, out _, allowPartial);

    /// <summary>以实际入包结果返回地面扣减份数和新增数量，预检阶段不改世界原物品。</summary>
    public bool TryAcceptNetworkPickup(ItemData itemData, out float consumedSourceAmount,
        out float addedAmount, bool allowPartial = true)
    {
        consumedSourceAmount = 0f;
        addedAmount = 0f;
        if (itemData == null || itemData.Stack == null)
            return false;

        using var pickupDebug = PickupAcceptDebug.Measure(1, itemData.IDName);
        float sourceAmount = itemData.Stack.Amount;
        int multiplier = itemData.Stack.Stackable && GameNetwork.HasStateAuthority ? PickupAmountMultiplier : 1;
        float requestedAmount = sourceAmount * multiplier;
        if (float.IsNaN(requestedAmount) || float.IsInfinity(requestedAmount) || requestedAmount <= 0f)
            return false;
        PickupSourceWholeUnitsDebug.SetGauge((long)Mathf.Min(sourceAmount, int.MaxValue));

        ItemData pickupData = ItemInstanceDataFactory.CloneRuntime(itemData);
        using var planLease = UnityEngine.Pool.ListPool<PickupInventoryPlan>.Get(out var plan);
        float targetAmount = BuildPickupPlan(pickupData, sourceAmount, multiplier, plan);
        // 容量计划模拟下一件的状态，正式提交必须重新从原本的受损件开始。
        pickupData.Durability = itemData.Durability;
        if (targetAmount <= 0.0001f || !allowPartial && targetAmount + 0.0001f < requestedAmount)
        {
            PickupCapacityRejectedDebug.AddWork(1);
            return false;
        }

        float remainingAmount = targetAmount;
        foreach (PickupInventoryPlan entry in plan)
        {
            Inventory targetInventory = entry.Inventory;
            if (targetInventory?.Data == null)
                continue;

            pickupData.Stack.Amount = Mathf.Min(remainingAmount, entry.Amount);

            bool added;
            float inventoryAddedAmount;
            using (PickupInventoryAddDebug.Measure(1, itemData.IDName))
                added = targetInventory.Data.TryAddItem(pickupData, true, out inventoryAddedAmount);
            if (!added || inventoryAddedAmount <= 0f)
                continue;

            // 当前受损件只进入第一个成功接收的库存，后续分配都是完整件。
            ItemDurabilityRules.AdvanceAfterRemoval(pickupData);

            // 快捷栏只能容纳一部分时，余量继续进入主背包，不能提前结束此次拾取。
            remainingAmount = Mathf.Max(0f, remainingAmount - inventoryAddedAmount);
            try
            {
                using var refreshDebug = PickupRefreshDebug.Measure(1, itemData.IDName);
                targetInventory.RefreshUI();
            }
            catch (System.Exception exception) { Debug.LogWarning($"[Mod_ItemPicker] 入包已提交，界面刷新失败：{exception.Message}"); }
            if (remainingAmount <= 0.0001f)
                break;
        }

        float totalAddedAmount = targetAmount - remainingAmount;
        if (totalAddedAmount <= 0f)
        {
            PickupInventoryRejectedDebug.AddWork(1);
            return false;
        }
        PickupAddedWholeUnitsDebug.SetGauge((long)Mathf.Min(totalAddedAmount, int.MaxValue));

        consumedSourceAmount = totalAddedAmount / multiplier;
        addedAmount = totalAddedAmount;
        float worldRemainingAmount = Mathf.Max(0f, sourceAmount - consumedSourceAmount);
        bool fullyAdded = worldRemainingAmount <= 0.0001f;
        itemData.Stack.Amount = fullyAdded ? sourceAmount : worldRemainingAmount;
        if (!fullyAdded) ItemDurabilityRules.AdvanceAfterRemoval(itemData);
        itemData.Stack.CanBePickedUp = !fullyAdded;
        try
        {
            using var notifyDebug = PickupNotifyDebug.Measure(1, itemData.IDName);
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
            DimensionManager dimensionManager = DimensionManager.ExistingInstance;
            string dimensionId = dimensionManager != null && dimensionManager.ActiveAddress.IsValid
                ? dimensionManager.ActiveAddress.DimensionId
                : null;
            GameplayProgressEvents.PublishPickupSucceeded(
                item as Player,
                itemData.IDName,
                totalAddedAmount,
                dimensionId);
        }
        catch (System.Exception exception) { Debug.LogError($"[Mod_ItemPicker] 入包已提交，后续通知失败：{exception}"); }
        return true;
    }

    #endregion

    #region 只读拾取容量计划

    private readonly struct PickupInventoryPlan
    {
        public readonly Inventory Inventory;
        public readonly float Amount;
        public PickupInventoryPlan(Inventory inventory, float amount) { Inventory = inventory; Amount = amount; }
    }

    /// <summary>联机预约与单机入包共用只读容量计划，返回最多能从原堆取走的完整份数。</summary>
    public float GetAcceptablePickupSourceAmount(ItemData itemData, int multiplierOverride = 0)
    {
        if (itemData?.Stack == null || !float.IsFinite(itemData.Stack.Amount) || itemData.Stack.Amount <= 0f)
            return 0f;
        int multiplier = !itemData.Stack.Stackable ? 1 : multiplierOverride > 0
            ? Mathf.Clamp(multiplierOverride, MinPickupAmountMultiplier, MaxPickupAmountMultiplier)
            : GameNetwork.HasStateAuthority ? PickupAmountMultiplier : 1;
        ItemData candidate = ItemInstanceDataFactory.CloneRuntime(itemData);
        using var planLease = UnityEngine.Pool.ListPool<PickupInventoryPlan>.Get(out var plan);
        return BuildPickupPlan(candidate, itemData.Stack.Amount, multiplier, plan) / multiplier;
    }

    private float BuildPickupPlan(ItemData candidate, float sourceAmount, int multiplier, List<PickupInventoryPlan> plan)
    {
        WorldItemWaterSystem.SetSavedBottomState(candidate, false);
        float requestedAmount = sourceAmount * multiplier;
        if (!float.IsFinite(requestedAmount) || requestedAmount <= 0f)
            return 0f;
        bool wholeUnits = !candidate.Stack.Stackable || Mathf.Abs(sourceAmount - Mathf.Round(sourceAmount)) <= .0001f;
        float capacity = GetPickupCapacityLimitedAmount(candidate, requestedAmount);
        if (wholeUnits) capacity = Inventory_Data.GetWholeStackAmount(capacity + .0001f);
        float remainingCapacity = Mathf.Min(requestedAmount, capacity);
        using var countedLease = UnityEngine.Pool.HashSetPool<Inventory_Data>.Get(out var counted);
        foreach (IInventory provider in AddTargetInventories)
        {
            Inventory inventory = provider?.GetDefaultTargetInventory();
            if (inventory?.Data == null || !counted.Add(inventory.Data)) continue;
            candidate.Stack.Amount = remainingCapacity;
            if (!inventory.Data.TryAddItem(candidate, false, out float available)) continue;
            if (wholeUnits) available = Inventory_Data.GetWholeStackAmount(available + .0001f);
            if (available <= .0001f) continue;
            plan.Add(new PickupInventoryPlan(inventory, available));
            ItemDurabilityRules.AdvanceAfterRemoval(candidate);
            remainingCapacity = Mathf.Max(0f, remainingCapacity - available);
            if (remainingCapacity <= .0001f) break;
        }
        float total = Mathf.Min(requestedAmount, capacity) - remainingCapacity;
        return wholeUnits ? Mathf.Floor(total / multiplier + .0001f) * multiplier : total;
    }

    /// <summary>
    /// 拾取会优先经过快捷栏，因此这里按玩家所有拾取目标的实际占用统一计算主背包携带上限，
    /// 防止先塞快捷栏再塞背包绕过重量/体积限制。创造背包直接放行。
    /// </summary>
    private float GetPickupCapacityLimitedAmount(ItemData itemData, float requestedAmount)
    {
        if (itemData?.Stack == null || requestedAmount <= 0f)
            return 0f;

        if (item is Player creativePlayer && CreativeInventoryState.IsEnabled(creativePlayer))
            return requestedAmount;

        if (item is Player player &&
            PlayerCarryCapacityUtility.TryGetSnapshot(player, out PlayerCarryCapacitySnapshot playerSnapshot))
        {
            return playerSnapshot.GetCapacityLimitedAmount(itemData, requestedAmount);
        }

        Inventory_Data bagData = null;
        float totalWeight = 0f;
        float totalVolume = 0f;
        using var countedLease = UnityEngine.Pool.HashSetPool<Inventory_Data>.Get(out var countedInventories);
        for (int i = 0; i < AddTargetInventories.Count; i++)
        {
            Inventory targetInventory = AddTargetInventories[i]?.GetDefaultTargetInventory();
            Inventory_Data targetData = targetInventory?.Data;
            if (targetData == null || !countedInventories.Add(targetData))
                continue;

            targetData.GetCarryUsage(out float weight, out float volume);
            totalWeight += weight;
            totalVolume += volume;
            if (string.Equals(targetData.Name, ModText.Bag, System.StringComparison.Ordinal))
                bagData = targetData;
        }

        if (bagData == null || !bagData.HasCarryCapacity || bagData.HasUnlimitedCarryCapacity)
            return requestedAmount;

        return bagData.GetCapacityLimitedAmount(
            itemData,
            requestedAmount,
            totalWeight,
            totalVolume);
    }

    #endregion

    #region 拾取表现

    /// <summary>
    /// 创建不含碰撞与业务脚本的视觉快照，并将其加速缩小吸入玩家身体。
    /// 真实物品可立即按原生命周期回收，动画不会污染对象池状态。
    /// </summary>
    public void PlayPickupSuction(Item worldItem, bool hideSourceRenderers = false)
    {
        if (worldItem == null)
            return;

        AudioService.Instance.PlayAt(AudioEventIds.ItemPickup, worldItem.transform.position);

        SpriteRenderer[] sourceRenderers = worldItem.GetComponentsInChildren<SpriteRenderer>(false);
        GameObject visualRoot = CreatePickupVisualSnapshot(worldItem, sourceRenderers);
        if (visualRoot == null)
            return;

        if (hideSourceRenderers)
        {
            for (int i = 0; i < sourceRenderers.Length; i++)
            {
                if (sourceRenderers[i] != null)
                    sourceRenderers[i].enabled = false;
            }
        }

        Transform target = item != null ? item.transform : transform;
        Vector3 targetLocalPoint = Vector3.zero;
        SpriteRenderer bodyRenderer = item != null
            ? (item.Sprite != null ? item.Sprite : item.GetComponentInChildren<SpriteRenderer>())
            : GetComponentInChildren<SpriteRenderer>();

        if (bodyRenderer != null)
            targetLocalPoint = target.InverseTransformPoint(bodyRenderer.bounds.center);

        StartCoroutine(AnimatePickupSuction(
            visualRoot,
            target,
            targetLocalPoint,
            worldItem.itemData?.Guid ?? worldItem.GetInstanceID()));
    }

    #region 轻量掉落物桥接

    /// <summary>轻量掉落物与完整 Item 共用失败提示限频，静止在满包玩家身边不会不断刷提示。</summary>
    internal bool TryAcceptDroppedPickup(ItemData data)
    {
        if (TryAcceptNetworkPickup(data)) return true;
        ShowInventoryFullPrompt(data);
        return false;
    }

    /// <summary>复用拾取器本身的范围，额外容差用于替代小型掉落物的接触面积。</summary>
    internal bool TryGetDroppedPickupBounds(out Bounds bounds)
    {
        bounds = default;
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy || droppedPickupCollider == null ||
            !droppedPickupCollider.enabled || item == null || item.DestructionHandled) return false;
        bounds = droppedPickupCollider.bounds;
        bounds.Expand(0.5f);
        // 沉底物按脚下整格拾取，即使恰好落在该格的另一角也不能漏掉。
        Vector3 cell = new(Mathf.Floor(item.transform.position.x), Mathf.Floor(item.transform.position.y), bounds.center.z);
        bounds.Encapsulate(cell);
        bounds.Encapsulate(cell + new Vector3(1f, 1f, 0f));
        return true;
    }

    private bool CanReachWaterItem(Item target) => item != null &&
        WorldItemWaterSystem.CanPickUpAt(target, WorldLocalPresentation.ToLogical(item.transform.position));

    internal void TryPickupBottomItem(Item target)
    {
        if (!CanPickUp || !CanReachWaterItem(target))
        {
            pickupAttemptedItems.Remove(target);
            deferredPickupItems.Remove(target);
            return;
        }
        RefreshPickupAttempts();
        if (deferredPickupItems.Remove(target) || pickupAttemptedItems.Add(target))
            TryPickUp(target, logMissingInventories: false);
    }

    internal bool CanReachDroppedPoint(Vector2 point, bool onBottom = false)
    {
        if (onBottom)
            return item != null && WorldItemWaterRules.CanPickUp(true,
                WorldTopologyRuntime.NormalizePosition(item.transform.position), WorldTopologyRuntime.NormalizePosition(point));
        return droppedPickupCollider != null &&
            (droppedPickupCollider.ClosestPoint(point) - point).sqrMagnitude <= 0.0625f;
    }

    /// <summary>只为已经成功入包的物品创建短期吸入视觉。</summary>
    internal void PlayDroppedPickupFeedback(DroppedItemVisual visual, FlatWorld.DroppedItems.LightweightDroppedBody body)
    {
        Vector3 position = new(body.Position.x, body.Position.y + body.VisualHeight, 0f);
        AudioService.Instance.PlayAt(AudioEventIds.ItemPickup, position);
        GameObject root = new GameObject("轻量掉落物_拾取反馈");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        Quaternion rotation = Quaternion.Euler(0f, 0f, body.Rotation);
        Vector3 scale = new(body.Scale.x, body.Scale.y, 1f);
        root.transform.SetPositionAndRotation(position + rotation * Vector3.Scale(visual.LocalPosition, scale),
            rotation * visual.LocalRotation);
        root.transform.localScale = Vector3.Scale(scale, visual.LocalScale);
        SpriteRenderer sprite = root.AddComponent<SpriteRenderer>();
        sprite.sprite = visual.Sprite; sprite.color = visual.Color;
        // 拾取反馈保持世界物品原排序键，移动过程继续交给 Y 轴透明排序处理。
        sprite.sortingLayerID = visual.Layer; sprite.sortingOrder = visual.Order;
        sprite.sharedMaterial = visual.SpriteMaterial;
        Transform target = item.transform;
        Vector3 targetPoint = item.Sprite != null ? target.InverseTransformPoint(item.Sprite.bounds.center) : Vector3.zero;
        StartCoroutine(AnimatePickupSuction(root, target, targetPoint, body.Id));
    }

    #endregion

    /// <summary>保留世界物品外层排序组，使离体拾取动画沿用主体的真实世界层级。</summary>
    private static GameObject CreatePickupVisualSnapshot(
        Item worldItem,
        SpriteRenderer[] sourceRenderers)
    {
        if (sourceRenderers == null || sourceRenderers.Length == 0)
            return null;

        GameObject visualRoot = new GameObject($"PickupSuction_{worldItem.name}");
        visualRoot.layer = worldItem.gameObject.layer;
        visualRoot.transform.SetPositionAndRotation(worldItem.transform.position, Quaternion.identity);
        visualRoot.transform.localScale = Vector3.one;
        SortingGroup sourceGroup = worldItem.GetComponent<SortingGroup>();
        if (sourceGroup != null && sourceGroup.enabled)
        {
            SortingGroup snapshotGroup = visualRoot.AddComponent<SortingGroup>();
            snapshotGroup.sortingLayerID = sourceGroup.sortingLayerID;
            snapshotGroup.sortingOrder = sourceGroup.sortingOrder;
        }

        int copiedRendererCount = 0;
        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            SpriteRenderer source = sourceRenderers[i];
            if (source == null || !source.enabled || source.sprite == null ||
                !source.gameObject.activeInHierarchy)
            {
                continue;
            }

            GameObject rendererObject = new GameObject(source.gameObject.name);
            rendererObject.layer = source.gameObject.layer;
            rendererObject.transform.SetParent(visualRoot.transform, false);
            rendererObject.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            rendererObject.transform.localScale = source.transform.lossyScale;

            SpriteRenderer copy = rendererObject.AddComponent<SpriteRenderer>();
            copy.sprite = source.sprite;
            copy.sharedMaterial = source.sharedMaterial;
            copy.color = source.color;
            copy.flipX = source.flipX;
            copy.flipY = source.flipY;
            copy.drawMode = source.drawMode;
            copy.size = source.size;
            copy.maskInteraction = source.maskInteraction;
            copy.spriteSortPoint = source.spriteSortPoint;
            copy.sortingLayerID = source.sortingLayerID;
            copy.sortingOrder = source.sortingOrder;
            copiedRendererCount++;
        }

        if (copiedRendererCount > 0)
            return visualRoot;

        Destroy(visualRoot);
        return null;
    }

    private IEnumerator AnimatePickupSuction(
        GameObject visualRoot,
        Transform target,
        Vector3 targetLocalPoint,
        int itemIdentity)
    {
        Vector3 initialScale = visualRoot.transform.localScale;
        Quaternion initialRotation = visualRoot.transform.rotation;
        float duration = Mathf.Max(0.05f, pickupSuctionDuration);
        float elapsed = 0f;

        Vector3 initialTarget = target != null
            ? target.TransformPoint(targetLocalPoint)
            : visualRoot.transform.position;
        Vector3 startPosition = WorldTopologyRuntime.NearestImagePosition(
            initialTarget,
            visualRoot.transform.position);
        startPosition.z = visualRoot.transform.position.z;
        visualRoot.transform.position = startPosition;
        Vector2 direction = initialTarget - startPosition;
        Vector2 perpendicular = direction.sqrMagnitude > 0.0001f
            ? new Vector2(-direction.y, direction.x).normalized
            : Vector2.up;
        float curveSign = (itemIdentity & 1) == 0 ? 1f : -1f;
        Vector3 curveOffset = perpendicular * (pickupSuctionCurveOffset * curveSign);

        // 首帧保持落地快照；后续旋转叠加在当前姿态上，不先摆正再吸入。
        yield return null;

        while (elapsed < duration && visualRoot != null)
        {
            if (target == null)
                break;

            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float suctionTime = normalizedTime * normalizedTime * normalizedTime;
            Vector3 targetPosition = WorldTopologyRuntime.NearestImagePosition(
                visualRoot.transform.position,
                target.TransformPoint(targetLocalPoint));
            float arcWeight = Mathf.Sin(normalizedTime * Mathf.PI);

            visualRoot.transform.position =
                Vector3.LerpUnclamped(startPosition, targetPosition, suctionTime) +
                curveOffset * arcWeight;
            visualRoot.transform.localScale =
                initialScale * Mathf.Max(0.02f, 1f - normalizedTime * normalizedTime);
            visualRoot.transform.rotation =
                initialRotation * Quaternion.Euler(0f, 0f, pickupSuctionRotation * suctionTime * curveSign);
            yield return null;
        }

        if (visualRoot != null)
            Destroy(visualRoot);
    }

    #endregion
}
