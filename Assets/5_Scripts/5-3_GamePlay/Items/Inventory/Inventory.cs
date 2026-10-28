using UnityEngine;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.InventoryFeedback;
using TMPro;
using UnityEngine.InputSystem;

[System.Serializable]
// 库存持有权威数据与转移入口，面板和设备交互分文件维护。
public partial class Inventory
{
    #region 库存数据、生命周期与转移
    [NonSerialized] public MachineEntity MachineOwner; // 数据机器的归属信息不参与库存序列化。
    [NonSerialized, FastCloner.Code.FastClonerIgnore] public Func<bool> RuntimeAccessGuard; // 临时面板会话在实际提交时再核对访问资格。
    public bool CanAccessRuntime => RuntimeAccessGuard?.Invoke() != false;

    [FoldoutGroup("基础引用"), ReadOnly, LabelText("所属物品")]
    public Item item;

    [FoldoutGroup("基础引用"), LabelText("UI面板预制体"), Tooltip("在Inspector中设置对应Inventory的面板预制体")]
    public GameObject InventoryPanel_Prefab;

    [FoldoutGroup("基础引用"), ReadOnly, LabelText("当前面板"), Tooltip("外部自动注入")]
    public BasePanel basePanel;

    [FoldoutGroup("数据"), LabelText("库存数据")]
    public Inventory_Data Data;

    [FoldoutGroup("数据"), LabelText("默认交互库存")]
    public Inventory DefaultTarget_Inventory;

    [FoldoutGroup("数据"), LabelText("容器质量上限 (kg)"), MinValue(0f),
     Tooltip("与容器体积上限都大于 0 时启用容量限制；0 表示未配置。")]
    public float StorageMaxWeightKg; // 储物容器可存放物品的总质量上限。

    [FoldoutGroup("数据"), LabelText("容器体积上限 (m³)"), MinValue(0f),
     Tooltip("按立方米配置；运行时会转换为库存内部使用的升。")]
    public float StorageMaxVolumeCubicMeters; // 储物容器可存放物品的总体积上限。

    [FoldoutGroup("数据"), ShowInInspector, ReadOnly, LabelText("UI开关按键"), Tooltip("UI面板开关Action名称，对应InputSystem中的Action Name")]
    public string ToggleActionName => Data?.ToggleActionName;

    [FoldoutGroup("UI"), ReadOnly, LabelText("物品槽UI")]
    public List<ItemSlot_UI> itemSlot_UI = new List<ItemSlot_UI>();

    public static Inventory LastOpenedContainer;

    // 快捷转移只用所属面板判断容器是否开放，不参与库存槽位 UI 的创建。
    private BasePanel _quickTransferOwnerPanel;
    [NonSerialized, FastCloner.Code.FastClonerIgnore] private InventoryFlickTargets _flickTargets;

    // 拖拽期间保留来源槽；加工器可据此暂缓消费正在被玩家移动的物品。
    [NonSerialized] private Dictionary<ItemSlot, int> _activeDragSourceCounts;

    // 运行时赋值，不序列化
    GameObject ItemSlot_Prefab;
    Transform ItemSlot_Parent;
    [NonSerialized] private bool defaultBagPlacementPending;
    [NonSerialized] private InventoryVirtualizedSlotGrid _virtualizedSlotGrid; // UI_Bag 只实例化可视区域附近的槽位。
    [NonSerialized, FastCloner.Code.FastClonerIgnore] private InventoryModuleDataScheduler _moduleDataScheduler;

    // 玩家行囊或受限容器的容量显示节点；正式节点由 UI_Bag Prefab 提供。
    private TextMeshProUGUI _carryWeightValueText;
    private TextMeshProUGUI _carryVolumeValueText;
    private UnityEngine.UI.Slider _carryWeightProgress;
    private UnityEngine.UI.Slider _carryVolumeProgress;
    [NonSerialized, FastCloner.Code.FastClonerIgnore] private List<Inventory_Data> _linkedCarryUsageData;
    private Inventory_Data _overweightObservedData; // 当前绑定超重监听的数据源，用于安全解绑旧引用。

    // 输入绑定缓存，便于之后解除绑定
    private InputAction _boundToggleAction;
    private Action<InputAction.CallbackContext> _toggleCallback;
    private bool _isProcessingGamepadSubmit;

    // 轻触同类物品时记录当前是连续拿取还是连续放置，不引入独立状态机。
    private enum TouchTapFlow
    {
        None,
        Pickup,
        PutDown
    }

    private TouchTapFlow _touchTapFlow;

    public virtual void OnValidate()
    {
        Data ??= new Inventory_Data(new List<ItemSlot>(), ModText.Bag);
        if (string.IsNullOrEmpty(Data.Name))
            Data.Name = ModText.Bag;
    }

    public virtual void Awake()
    {

    }

    public virtual void ModUpdate(float deltaTime)
    {
        // 在模块 Tick 统一执行动态槽位自检，并同步扩容/收缩后的槽位 UI。
        if (Data != null && Data.HasUnlimitedSlots)
        {
            Data.MaintainDynamicSlotCount();
            SyncDynamicSlotUI();
        }
        UpdateModuleData(deltaTime);
    }

    private void UpdateModuleData(float deltaTime)
    {
        _moduleDataScheduler ??= new InventoryModuleDataScheduler(this);
        _moduleDataScheduler.Tick(deltaTime);
    }


    public void BindController(Mod_GameController gameController)
    {
        // 先解除之前的绑定，避免重复订阅
        UnbindController();

        // 常驻容器没有开关按键，不需要输入资产，也不应产生绑定警告。
        if (string.IsNullOrEmpty(ToggleActionName))
            return;

        // 基本防守：控制器或输入资产为空则不绑定
        if (gameController == null || gameController._inputActions == null)
        {
            Debug.LogWarning("[Inventory.BindController] Mod_GameController 或 _inputActions 为空，取消绑定");
            return;
        }


        // 有开关按键的容器需要对应面板。
        if (InventoryPanel_Prefab == null)
        {
            InventoryPanel_Prefab = GameRes.Instance.GetPrefab(Data.UIPrefabName);
            if (InventoryPanel_Prefab == null)
            {
                Debug.LogError("[Inventory.BindController] InventoryPanel_Prefab 未设置，取消绑定");
                return;
            }
        }

        var action = gameController._inputActions.FindAction(ToggleActionName);
        if (action == null)
        {
            // 找不到对应 Action：只给出提示，不做绑定
            Debug.LogWarning($"[Inventory.BindController] 找不到 Action '{ToggleActionName}'，取消绑定");
            return;
        }

        // 记录回调与 Action，便于之后解绑
        _toggleCallback = ctx =>
        {
            if (!gameController.IsGameplayInputAllowed(ctx))
                return;

            if (!gameController.CanToggleGameplayWindow(basePanel))
                return;

            // 键盘 B 是库存自身的开关键；手柄 B 已交给全局返回动作。
            SwitchUI();
        };

        action.performed += _toggleCallback;

        _boundToggleAction = action;
        BindCarryCapacityLinkedInventory();
    }

    /// <summary>
    /// 解除通过 BindController 建立的输入绑定。
    /// 库存数据/UI 事件拥有独立生命周期，不能在这里解绑，否则重新绑定控制器会让槽位停止自动刷新。
    /// </summary>
    public void UnbindController()
    {
        UnbindCarryCapacityLinkedInventory();
        if (_boundToggleAction != null && _toggleCallback != null)
        {
            _boundToggleAction.performed -= _toggleCallback;
        }

        _boundToggleAction = null;
        _toggleCallback = null;
    }

    /// <summary>库存真正退出运行时生命周期时，解除数据层到 UI 的刷新监听。</summary>
    public void UnbindRuntimeDataEvents()
    {
        _moduleDataScheduler?.Dispose();
        _moduleDataScheduler = null;
        if (Data != null)
            Data.Event_RefreshUI -= RefreshUI;

        UnbindSlotDataEvents();
    }

    /// <summary>解除当前库存所有槽位的 UI 监听，避免场景/玩家销毁后继续回调旧界面。</summary>
    public void UnbindSlotDataEvents()
    {
        if (Data?.itemSlots == null)
            return;

        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            ItemSlot slot = Data.itemSlots[i];
            slot?.onSlotDataChanged.Clear();
        }
    }


    [Tooltip("在Load时调用此函数进行数据初始化（仅初始化数据和逻辑，不涉及UI）")]
    public virtual void InitData()
    {
        if (DefaultTarget_Inventory == null)
            DefaultTarget_Inventory = Inventory_Hand.PlayerHand;

        Player player = item as Player;
        bool isPlayerBag = player != null && string.Equals(Data.Name, ModText.Bag, StringComparison.Ordinal);
        if (isPlayerBag)
        {
            CreativeInventoryState.Restore(this);
            Data.ConfigurePlayerBagCapacity(
                player.Data?.MaxCarryWeight ?? Inventory_Data.DefaultPlayerBagMaxWeight,
                player.Data?.MaxCarryVolume ?? Inventory_Data.DefaultPlayerBagMaxVolume);
        }
        else if (StorageMaxWeightKg > 0f &&
                 StorageMaxVolumeCubicMeters > 0f &&
                 !float.IsNaN(StorageMaxWeightKg) &&
                 !float.IsInfinity(StorageMaxWeightKg) &&
                 !float.IsNaN(StorageMaxVolumeCubicMeters) &&
                 !float.IsInfinity(StorageMaxVolumeCubicMeters))
        {
            Data.ConfigureStorageContainerCapacity(
                StorageMaxWeightKg,
                StorageMaxVolumeCubicMeters * Inventory_Data.LitersPerCubicMeter);
        }
        else
        {
            Data.SetUnlimitedSlots(false);
            // 玩家手部槽与快捷栏只是主背包物品的临时/快捷承载入口，
            // 单格堆叠规则应与主背包一致；重量与体积仍由玩家统一携带容量统计负责。
            Data.SetUnlimitedStackSize(IsHandInventory() || IsHotBarInventory());
            Data.ClearCarryCapacity();
        }

        Data.MaintainDynamicSlotCount();

        // 初始化物品槽位数据
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            ItemSlot slot = Data.itemSlots[i];
            if (slot == null)
                continue;

            // Inventory 可能复用存档中的 ItemSlot，先清理上一轮玩家/UI 的旧监听。
            slot.onSlotDataChanged.Clear();
            slot.Index = i;
            slot.SlotMaxVolume = Inventory_Data.DefaultSlotVolume;
        }

        // 初始化事件系统
        Data.Event_RefreshUI = new();
        Data.Event_RefreshUI.Clear();
        Data.Event_RefreshUI += RefreshUI;

        BindPlayerCarryWeightEvents();
        _moduleDataScheduler ??= new InventoryModuleDataScheduler(this);
        _moduleDataScheduler.Bind(Data);
    }

    /// <summary>
    /// 自动初始化容器内的物品
    /// </summary>
    public void TryInitializeItems(Inventoryinit inventoryinit)
    {
        // 使用InventoryInit的注册函数将物品注册到inventory中
        inventoryinit.InjectRandomItemsToInventory(this);
        Debug.Log($"[{Data.Name}] 容器初始化完成，注册 {inventoryinit.items.Count} 个物品");
    }

    /// <summary>
    /// 检查容器是否为空，没有任何物品
    /// </summary>
    /// <returns>如果容器为空返回true，否则返回false</returns>
    private bool IsInventoryEmpty()
    {
        foreach (var slot in Data.itemSlots)
        {
            if (slot.itemData != null)
                return false;
        }
        return true;
    }

    /// <summary>主背包和快捷栏通过库存数据事件维护超重减速，不依赖库存 Tick 轮询。</summary>
    private void BindPlayerCarryWeightEvents()
    {
        UnbindPlayerCarryWeightEvents();
        if (item is not Player player || Data == null ||
            (!IsPlayerBagInventory() && this is not Mod_HotBar.HotBarRuntimeInventory))
            return;

        _overweightObservedData = Data;
        _overweightObservedData.Event_OnDataChanged += HandlePlayerCarryWeightDataChanged;

        // 重载或网络恢复可能直接替换整份库存数据，没有逐槽位变更事件；绑定时做一次状态校准。
        PlayerCarryCapacityUtility.RefreshOverweightSlowdown(player);
    }

    /// <summary>库存或所属物品卸载时解除超重监听，避免保留旧玩家引用。</summary>
    public void UnbindPlayerCarryWeightEvents()
    {
        if (_overweightObservedData == null)
            return;

        _overweightObservedData.Event_OnDataChanged -= HandlePlayerCarryWeightDataChanged;
        _overweightObservedData = null;
    }

    /// <summary>背包物品输入、输出或数量变化后，按玩家当前随身总重量更新超重状态。</summary>
    private void HandlePlayerCarryWeightDataChanged(ItemSlot _)
    {
        if (item is Player player)
            PlayerCarryCapacityUtility.RefreshOverweightSlowdown(player);
    }

    public virtual void Interact_Start(Item item_)
    {
        SwitchUI();
    }

    /// <summary>
    /// 仅识别本地玩家的主背包，避免改变箱子、工作台和其他独立库存的手部交换规则。
    /// </summary>
    private bool IsPlayerBagInventory()
    {
        return item != null && item is Player && Data?.Name == ModText.Bag;
    }

    public virtual void OnShiftQuickTransfer(int index)
    {
        TryShiftQuickTransfer(index);
    }

    /// <summary>
    /// 执行一次 Shift 快速转移，并返回是否实际移动了物品。
    /// 子类可据此只在成功时同步手持物或网络状态。
    /// </summary>
    protected bool TryShiftQuickTransfer(int index)
    {
        Inventory targetInventory = ResolveShiftQuickTransferTarget();
        return TryQuickTransferTo(index, targetInventory);
    }

    /// <summary>鼠标与触屏甩动以指针起点和方向选面板，复用正式转移、音频与逐格反馈。</summary>
    public virtual void OnFlickTransfer(int index, Vector2 origin, Vector2 direction)
    {
        TryFlickTransfer(index, origin, direction);
    }

    protected bool TryFlickTransfer(int index, Vector2 origin, Vector2 direction)
    {
        Inventory hotbar = GetPlayerHotBarInventory();
        hotbar?.RegisterFlickTarget(hotbar.basePanel, true);
        Inventory target = InventoryFlickTargets.Resolve(this, index, origin, direction);
        using (InventoryQuickTransferFeedbackEvents.UseScreenOrigin(this, index, origin + direction))
            return TryQuickTransferTo(index, target);
    }

    /// <summary>没有其他打开的目标库存界面时，拖动直接沿用拿到手部的普通流程。</summary>
    public bool HasVisibleFlickTarget()
    {
        Inventory hotbar = GetPlayerHotBarInventory();
        hotbar?.RegisterFlickTarget(hotbar.basePanel, true);
        return InventoryFlickTargets.HasVisibleTarget(this);
    }

    /// <summary>方向偏出真实目标窗口及十度余量时，沿用普通拖拽拿到手部。</summary>
    public bool HasVisibleFlickTarget(Vector2 origin, Vector2 direction)
    {
        Inventory hotbar = GetPlayerHotBarInventory();
        hotbar?.RegisterFlickTarget(hotbar.basePanel, true);
        return InventoryFlickTargets.HasTargetInDirection(this, origin, direction);
    }

    private bool TryQuickTransferTo(int index, Inventory targetInventory)
    {
        if (!IsValidQuickTransferTarget(targetInventory) || targetInventory == this)
            return false;

        ItemSlot sourceSlot = Data?.itemSlots != null && index >= 0 && index < Data.itemSlots.Count
            ? Data.itemSlots[index] : null;
        ItemData sourceItem = sourceSlot?.itemData;
        float before = sourceItem?.Stack?.Amount ?? 0f;
        bool moved = TryQuickMoveSlotToInventory(index, targetInventory);
        if (moved)
            PublishInventoryAudio(this, targetInventory, sourceItem,
                GetCommittedTransferAmount(sourceSlot, sourceItem, before));
        return moved;
    }

    /// <summary>只读判断是否至少能收下一件，全部槽位复用正式接收策略和转移数量计算。</summary>
    public bool CanQuickTransferTo(int sourceIndex, Inventory target)
    {
        ItemSlot source = Data?.GetItemSlot(sourceIndex);
        if (source?.itemData?.Stack == null || target?.Data?.itemSlots == null ||
            ReferenceEquals(Data, target.Data) || target.Data.IsDepositBlocked) return false;
        foreach (ItemSlot destination in target.Data.itemSlots)
        {
            if (CanTransferToSlot(source, target, destination, int.MaxValue, allowSwap: false)) return true;
        }
        return false;
    }

    private Inventory ResolveShiftQuickTransferTarget()
    {
        if (IsHotBarInventory())
            return GetValidLastOpenedContainer();

        return GetPlayerHotBarInventory();
    }

    private bool TryQuickMoveSlotToInventory(int sourceIndex, Inventory targetInventory)
    {
        if (FlatWorld.Spaceflight.SpaceSession.RejectClientGroundInventoryMutation()) return false;
        if (Data == null || Data.itemSlots == null || targetInventory == null || targetInventory.Data == null || targetInventory.Data.itemSlots == null)
            return false;

        if (sourceIndex < 0 || sourceIndex >= Data.itemSlots.Count)
            return false;

        ItemSlot sourceSlot = Data.itemSlots[sourceIndex];
        if (sourceSlot == null || sourceSlot.itemData == null)
            return false;

        if (MachineInventoryCommands.TryRoute(this, sourceIndex, targetInventory, -1, "quick", 0, out bool routed)) return routed;
        bool moved = false;
        List<ItemSlot> targetSlots = targetInventory.Data.itemSlots;
        // 两轮只负责选槽：先已有同类堆，再空槽，每次提交都复用统一事务。
        for (int pass = 0; pass < 2 && sourceSlot.itemData != null; pass++)
        {
            for (int i = 0; i < targetSlots.Count && sourceSlot.itemData != null; i++)
            {
                ItemSlot targetSlot = targetSlots[i];
                if (targetSlot == null || (pass == 0 ? targetSlot.itemData == null : targetSlot.itemData != null))
                    continue;
                int transferCount = Mathf.CeilToInt(sourceSlot.itemData.Stack.Amount);
                if (transferCount <= 0) break;
                moved |= TryTransferQuickQuantity(sourceSlot, targetInventory, targetSlot, transferCount, true);
            }
        }
        return moved;
    }

    private bool TryTransferQuickQuantity(
        ItemSlot sourceSlot,
        Inventory targetInventory,
        ItemSlot targetSlot,
        int transferCount,
        bool quickTransferFeedback = false)
    {
        return TryCommitSlotTransfer(sourceSlot, targetInventory, targetSlot, transferCount,
            allowSwap: false, quickTransferFeedback: quickTransferFeedback);
    }

    // 输入只决定目标与操作方式，接收规则、容量和交换资格在预检与提交间共用。
    private bool CanTransferToSlot(ItemSlot sourceSlot, Inventory targetInventory, ItemSlot targetSlot,
        int amount, bool allowSwap)
    {
        if (!CanAccessRuntime || targetInventory?.CanAccessRuntime != true) return false;
        ItemData source = sourceSlot?.itemData;
        if (source?.Stack == null || targetSlot == null || amount <= 0 ||
            Data?.itemSlots == null || targetInventory?.Data?.itemSlots == null ||
            !Data.itemSlots.Contains(sourceSlot) || !targetInventory.Data.itemSlots.Contains(targetSlot) ||
            !ReferenceEquals(Data, targetInventory.Data) && targetInventory.Data.IsDepositBlocked)
            return false;
        if (ReferenceEquals(sourceSlot, targetSlot)) return allowSwap;
        if (!targetInventory.CanAcceptQuickTransfer(sourceSlot, targetSlot)) return false;
        ItemData target = targetSlot.itemData;
        if (target != null && !target.CanStackWith(source))
            return allowSwap && CanAcceptQuickTransfer(targetSlot, sourceSlot) &&
                targetInventory.CanHoldWholeStack(targetSlot, source) && CanHoldWholeStack(sourceSlot, target);
        return Data.GetTransferableQuantityTo(sourceSlot, targetInventory.Data, targetSlot, amount) > 0;
    }

    private bool TryCommitSlotTransfer(ItemSlot sourceSlot, Inventory targetInventory, ItemSlot targetSlot,
        int amount, bool allowSwap, bool quickTransferFeedback = false)
    {
        if (FlatWorld.Spaceflight.SpaceSession.RejectClientGroundInventoryMutation() ||
            !CanTransferToSlot(sourceSlot, targetInventory, targetSlot, amount, allowSwap)) return false;
        if (ReferenceEquals(sourceSlot, targetSlot)) return true;
        int sourceIndex = Data.itemSlots.IndexOf(sourceSlot);
        int targetIndex = targetInventory.Data.itemSlots.IndexOf(targetSlot);
        if (MachineInventoryCommands.TryRoute(this, sourceIndex, targetInventory, targetIndex,
                allowSwap ? "move" : "quantity", allowSwap ? 0 : amount, out bool routed)) return routed;
        ItemData movedItem = sourceSlot.itemData;
        float targetBefore = targetSlot.itemData?.Stack?.Amount ?? 0f;
        bool moved = allowSwap
            ? Data.DropItemSlotTo(sourceSlot, targetInventory.Data, targetSlot)
            : Data.TransferItemQuantityTo(sourceSlot, targetInventory.Data, targetSlot, amount);
        if (!moved) return false;
        RefreshUI(sourceIndex);
        targetInventory.RefreshUI(targetIndex);
        OnSlotTransferCommitted();
        if (!ReferenceEquals(this, targetInventory)) targetInventory.OnSlotTransferCommitted();
        _touchTapFlow = TouchTapFlow.None;
        targetInventory._touchTapFlow = TouchTapFlow.None;
        // 仅在真实事务成功后按实际接收槽发布表现，网络请求已发送不算成功。
        if (quickTransferFeedback)
            InventoryQuickTransferFeedbackEvents.Publish(this, sourceIndex,
                targetInventory, targetIndex, movedItem,
                (targetSlot.itemData?.Stack?.Amount ?? 0f) - targetBefore);
        return true;
    }

    /// <summary>实际转移后由库存自身同步附属状态，输入设备不再各自补通知。</summary>
    protected virtual void OnSlotTransferCommitted() { }

    /// <summary>服务端的已校验请求复用完整拖拽与数量转移规则。</summary>
    public bool ExecuteMachineTransfer(int sourceIndex, Inventory target, int targetIndex, string operation, int amount)
    {
        if (Data?.itemSlots == null || (uint)sourceIndex >= (uint)Data.itemSlots.Count || target?.Data?.itemSlots == null) return false;
        if (operation == "quick") return TryQuickMoveSlotToInventory(sourceIndex, target);
        if ((uint)targetIndex >= (uint)target.Data.itemSlots.Count) return false;
        if (operation == "move") return TryMoveSlotTo(sourceIndex, target, targetIndex);
        return operation == "quantity" && amount > 0 &&
            TryTransferQuickQuantity(Data.itemSlots[sourceIndex], target, target.Data.itemSlots[targetIndex], amount);
    }

    /// <summary>
    /// 快速转移的目标槽接收策略。普通槽默认允许所有物品；
    /// 配置了 CanAcceptTags 时至少需匹配一个标签，特殊库存可覆写追加业务规则。
    /// </summary>
    public virtual bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot)
    {
        if (!CanAccessRuntime) return false;
        ItemData sourceItem = sourceSlot?.itemData;
        if (sourceItem == null || targetSlot == null)
            return false;

        if (Data != null && !Data.CanAcceptItem(sourceItem, targetSlot)) return false;

        if (Data?.IsDepositBlocked == true &&
            (Data.itemSlots == null || !Data.itemSlots.Contains(sourceSlot)))
            return false;

        if (targetSlot.CanAcceptTags == null || targetSlot.CanAcceptTags.Count == 0)
            return true;

        return sourceItem.Tags != null &&
               sourceItem.Tags.ContainsAnyTag(targetSlot.CanAcceptTags);
    }

    private Inventory GetPlayerHotBarInventory()
    {
        // 首选 PlayerHand：它代表当前本地玩家，与打开方式、来源容器和 UI 层级无关。
        // 合成/熔炉等独立 Inventory 没有自身 item 时，仍能稳定找到同一条快捷栏。
        Inventory hotBar = TryGetHotBarFromItem(Inventory_Hand.PlayerHand?.item);
        if (IsValidQuickTransferTarget(hotBar) && hotBar != this)
            return hotBar;

        // 以下是兼容自定义玩家库存和旧存档运行时绑定的回退路径。
        hotBar = TryGetHotBarFromItem(item);
        if (IsValidQuickTransferTarget(hotBar) && hotBar != this)
            return hotBar;

        hotBar = TryGetHotBarFromItem(DefaultTarget_Inventory?.item);
        if (IsValidQuickTransferTarget(hotBar) && hotBar != this)
            return hotBar;

        return null;
    }

    private static bool IsValidQuickTransferTarget(Inventory inventory)
    {
        return inventory != null &&
               inventory.Data != null &&
               inventory.Data.itemSlots != null &&
               inventory.Data.itemSlots.Count > 0;
    }

    private static Inventory TryGetHotBarFromItem(Item ownerItem)
    {
        if (ownerItem == null || ownerItem.itemMods == null)
            return null;

        Mod_HotBar hotbarModule = ownerItem.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        if (hotbarModule != null)
            return hotbarModule.GetDefaultTargetInventory();

        Mod_Inventory modInventory = ownerItem.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Hotbar);
        if (modInventory == null)
            return null;

        return modInventory.inventory;
    }

    private Inventory GetValidLastOpenedContainer()
    {
        if (LastOpenedContainer == null || LastOpenedContainer == this)
            return null;

        if (LastOpenedContainer._quickTransferOwnerPanel == null ||
            !LastOpenedContainer._quickTransferOwnerPanel.IsOpen())
        {
            LastOpenedContainer = null;
            return null;
        }

        return LastOpenedContainer;
    }

    /// <summary>
    /// 将当前容器同步为快捷栏 Shift+左键的目标。
    /// 组合式面板（合成台、熔炉等）应把真正接收物品的输入 Inventory 传入此统一入口。
    /// </summary>
    public void SyncQuickTransferTarget(BasePanel ownerPanel = null, bool allowDirectionalDeposit = true)
    {
        _quickTransferOwnerPanel = ownerPanel != null ? ownerPanel : basePanel;
        RegisterFlickTarget(_quickTransferOwnerPanel, allowDirectionalDeposit && !IsHandInventory());

        if (IsHotBarInventory() || IsHandInventory())
            return;

        if (_quickTransferOwnerPanel != null && _quickTransferOwnerPanel.IsOpen())
        {
            LastOpenedContainer = this;
            return;
        }

        if (LastOpenedContainer == this)
            LastOpenedContainer = null;
    }

    private void RegisterFlickTarget(BasePanel owner, bool allowed)
    {
        if (_flickTargets != null && (!allowed || owner == null || _flickTargets.gameObject != owner.gameObject))
        {
            _flickTargets.Unbind(this);
            _flickTargets = null;
        }
        if (!allowed || owner == null) return;
        _flickTargets = owner.GetComponent<InventoryFlickTargets>();
        if (_flickTargets == null) _flickTargets = owner.gameObject.AddComponent<InventoryFlickTargets>();
        _flickTargets.Bind(this, owner);
    }

    private bool IsHotBarInventory()
    {
        return Data?.Name == ModText.Hotbar;
    }

    private bool IsHandInventory()
    {
        return this is Inventory_Hand || Data?.Name == ModText.Hand;
    }

    /// <summary>玩家主动开关的库存面板参与关闭与手柄焦点链；快捷栏和手部库存不参与。</summary>
    private bool UsesTogglePanelLifecycle()
    {
        return !IsHotBarInventory() && !IsHandInventory();
    }

    [Button("同步槽位数量")]
    public void SyncSlotCount()
    {
        Data.itemSlots.Clear();
        // 编辑器同步与运行时遵守相同的槽位边界，忽略附属 UI。
        for (int i = 0; i < ItemSlot_Parent.childCount; i++)
        {
            if (ItemSlot_Parent.GetChild(i).GetComponent<ItemSlot_UI>() != null)
                Data.itemSlots.Add(new ItemSlot());
        }
    }
    public virtual void Save()
    {

    }
    #endregion
}
