using UnityEngine;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Gameplay.Progress;
using UnityEngine.InputSystem;
[System.Serializable]
public class Inventory
{
    #region 字段和属性

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

    [FoldoutGroup("数据"), ShowInInspector, ReadOnly, LabelText("UI开关按键"), Tooltip("UI面板开关Action名称，对应InputSystem中的Action Name")]
    public string ToggleActionName => Data?.ToggleActionName;

    [FoldoutGroup("UI"), ReadOnly, LabelText("物品槽UI")]
    public List<ItemSlot_UI> itemSlot_UI = new List<ItemSlot_UI>();

    public static Inventory LastOpenedContainer;

    // 运行时赋值，不序列化
    GameObject ItemSlot_Prefab;
    Transform ItemSlot_Parent;

    // 输入绑定缓存，便于之后解除绑定
    private GameController _boundController;
    private InputAction _boundToggleAction;
    private Action<InputAction.CallbackContext> _toggleCallback;
    private bool _isProcessingGamepadSubmit;
    // 当前背包打开的物品上下文菜单；关闭背包时必须随背包一起回收。
    private BasePanel _activeContextMenuPanel;

    // 轻触同类物品时记录当前是连续拿取还是连续放置，不引入独立状态机。
    private enum TouchTapFlow
    {
        None,
        Pickup,
        PutDown
    }

    private TouchTapFlow _touchTapFlow;

    #endregion

    #region 生命周期

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
        // 基类只负责统一调度模块数据更新。
        UpdateModuleData(deltaTime);
    }

    #endregion

    #region 模块数据驱动

    private void UpdateModuleData(float deltaTime)
    {
        if (Data == null || Data.itemSlots == null)
        {
            return;
        }

        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            ItemSlot slot = Data.itemSlots[i];
            ItemData itemData = slot?.itemData;
            if (itemData == null || itemData.ModuleDataDic == null)
            {
                continue;
            }

            foreach (ModuleData moduleData in itemData.ModuleDataDic.Values)
            {
                if (moduleData == null)
                {
                    continue;
                }

                moduleData.RuntimeOwnerItemData = itemData;
                moduleData.RuntimeOwnerInventoryData = Data;
                moduleData.RuntimeOwnerSlot = slot;
                moduleData.RuntimeOwnerSlotIndex = i;

                moduleData.DataUpdate(deltaTime);
            }
        }
    }

    #endregion

    #region 输入绑定


    public void BindController(GameController gameController)
    {
        // 先解除之前的绑定，避免重复订阅
        UnbindController();

        // 基本防守：控制器或输入资产为空则不绑定
        if (gameController == null || gameController._inputActions == null)
        {
            Debug.LogWarning("[Inventory.BindController] GameController 或 _inputActions 为空，取消绑定");
            return;
        }


        // 未配置 ToggleActionName 时不绑定
        if (string.IsNullOrEmpty(ToggleActionName))
        {
            // 快捷栏、装备栏等常驻库存不需要独立开关按键。
            return;
        }
        // 未配置 ToggleActionName 时不绑定
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
            if (gameController.IsGameplayInputLocked &&
                (basePanel == null || !basePanel.IsOpen()) &&
                !CanToggleFromMobileMenu())
            {
                return;
            }

            // 键盘 B 是库存自身的开关键；手柄 B 已交给全局返回动作。
            SwitchUI();
        };

        action.performed += _toggleCallback;

        _boundController = gameController;
        _boundToggleAction = action;

        if (basePanel != null && basePanel.IsOpen() && UsesModalGameplayInputLock())
            AcquirePanelInputLock();
    }

    /// <summary>手机菜单抽屉内的背包按钮允许在制作面板打开时切换背包。</summary>
    private bool CanToggleFromMobileMenu()
    {
        return _boundController != null &&
               _boundController.IsUsingMobile &&
               PlayerMobileControlsHUD.IsActiveDrawerOpen;
    }

    /// <summary>
    /// 解除通过 BindController 建立的输入绑定
    /// </summary>
    public void UnbindController()
    {
        Data.Event_RefreshUI -= RefreshUI;
        UnbindSlotDataEvents();
        _boundController?.ReleaseGameplayInputLock(this);

        if (_boundToggleAction != null && _toggleCallback != null)
        {
            _boundToggleAction.performed -= _toggleCallback;
        }

        _boundController = null;
        _boundToggleAction = null;
        _toggleCallback = null;
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

    #endregion

    #region 面板开关
    public virtual void SwitchUI()
    {
        // 确保面板已创建
        if (basePanel == null)
        {
            if (!EnsurePanelCreated())
            {
                Debug.LogError("[Inventory.SwitchUI] EnsurePanelCreated 失败，取消切换");
                return;
            }
            basePanel.Open();
            SyncQuickTransferTarget(basePanel);
            PublishPlayerBagOpened();
            // 延迟一帧将面板置顶，确保不会被其他UI遮挡
            GameManager.Instance.StartCoroutine(DelayedBringToFront(basePanel.GetComponent<RectTransform>()));
            return;
        }

        if (basePanel.IsOpen())
            CloseActiveContextMenu();

        basePanel.Toggle();
        if (basePanel.IsOpen())
        {
            PublishPlayerBagOpened();
            // 延迟一帧将面板置顶，确保不会被其他UI遮挡
            GameManager.Instance.StartCoroutine(DelayedBringToFront(basePanel.GetComponent<RectTransform>()));
        }

        SyncQuickTransferTarget(basePanel);
    }

    private void PublishPlayerBagOpened()
    {
        if (item is not Player player || player.itemMods == null)
            return;

        Mod_Inventory bag = player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        if (bag?.InventoryInstances == null)
            return;

        for (int i = 0; i < bag.InventoryInstances.Count; i++)
        {
            if (!ReferenceEquals(bag.InventoryInstances[i], this))
                continue;

            GameplayProgressEvents.PublishInventoryOpened(player);
            return;
        }
    }

    private static IEnumerator DelayedBringToFront(RectTransform rectTransform)
    {
        yield return null;
        BasePanel.BringToFront(rectTransform);
    }

    #endregion

    #region 面板创建

    /// <summary>
    /// 确保当前 Inventory 的面板已创建，如果未创建则在此时创建
    /// </summary>
    /// <param name="inventoryId">当前 Inventory 在字典中的 ID（用于日志和预制体缺失提示）</param>
    /// <param name="inventoryIndex">当前 Inventory 在同组 Inventory 中的索引（用于控制关闭按钮显隐）</param>
    /// <param name="inventoryBasePanelCache">Inventory 到 BasePanel 的缓存字典，用于统一管理关闭逻辑</param>
    /// <returns>成功创建了面板返回 true，没有创建或失败返回 false</returns>
    public bool EnsurePanelCreated()
    {
        // 如果面板已创建，直接返回 false（表示没有创建新面板）
        if (basePanel != null)
            return false;
        // 如果预制体存在，创建面板
        GameObject panelPrefab = InventoryPanel_Prefab;

        if (panelPrefab == null)
        {
            Debug.LogWarning("[Inventory.EnsurePanelCreated] InventoryPanel_Prefab 未设置，无法创建面板");
            return false;
        }


        basePanel = UIManager.Instance.CreatePanelFromGameObject(panelPrefab).GetComponentInChildren<BasePanel>();
        ResolvePanelInputController();
        bool usesModalGameplayInputLock = UsesModalGameplayInputLock();
        basePanel.SetGameplayInputBlocking(usesModalGameplayInputLock);
        // 快捷栏与手部库存属于常驻/内部 HUD，不进入模态焦点链，也不锁定玩家输入。
        if (usesModalGameplayInputLock)
        {
            basePanel.PrepareForGamepadNavigation(closeOnCancel: true);
            basePanel.Opened += AcquirePanelInputLock;
            basePanel.Closed += ReleasePanelInputLock;
        }
        basePanel.Closed += CloseActiveContextMenu;

        // 普通背包 Prefab 可能以可见状态保存；先统一为关闭态，确保随后 Open 能触发输入锁事件。
        // 快捷栏与手部库存保留 Prefab 的显示状态，不参与这次归一化。
        if (usesModalGameplayInputLock)
        {
            basePanel.Close();
        }

        // 如果此 inventory 中保存了面板位置，则尝试在创建时恢复位置
        if (Data != null)
        {
            RectTransform rt = null;
            if (basePanel.Dragger != null)
                rt = basePanel.Dragger.GetComponent<RectTransform>();
            if (rt == null)
                rt = basePanel.GetComponent<RectTransform>();

            if (rt != null)
            {
                var savedPos = Data.PanelPosition;
                var savedPos2 = new Vector2(savedPos.x, savedPos.y);
                if (IsValidVector2(savedPos2) && (savedPos2.x != 0 || savedPos2.y != 0))
                {
                    rt.anchoredPosition = savedPos2;
                }
            }
        }

        // 设置窗口信息
        if (basePanel.TryGetText("窗口信息", out TMPro.TextMeshProUGUI titleText))
            titleText.text = Data.Name;

        // 调用UI初始化方法（此时basePanel已存在）
        InitUI();

        return true; // 成功创建了面板
    }

    private void AcquirePanelInputLock()
    {
        if (UsesModalGameplayInputLock())
            _boundController?.AcquireGameplayInputLock(this);
    }

    private void ReleasePanelInputLock()
    {
        _boundController?.ReleaseGameplayInputLock(this);
    }

    /// <summary>
    /// 回收当前库存派生的右键菜单及其物品详情子面板，避免父背包关闭后留下悬空 UI。
    /// </summary>
    private void CloseActiveContextMenu()
    {
        if (_activeContextMenuPanel == null)
            return;

        _activeContextMenuPanel.Destroy();
        _activeContextMenuPanel = null;
    }

    private void ResolvePanelInputController()
    {
        if (_boundController != null || item == null)
            return;

        _boundController = item.itemMods?.GetMod_ByID<GameController>(ModText.Controller);
        _boundController ??= item.GetComponent<GameController>();
    }

    // 辅助方法：检查 Vector2 是否有效
    private bool IsValidVector2(Vector2 vector)
    {
        return !float.IsNaN(vector.x) && !float.IsNaN(vector.y) &&
               !float.IsInfinity(vector.x) && !float.IsInfinity(vector.y);
    }

    #endregion

    #region 初始化和同步


    [Tooltip("在Load时调用此函数进行数据初始化（仅初始化数据和逻辑，不涉及UI）")]
    public virtual void InitData()
    {
        if (DefaultTarget_Inventory == null)
            DefaultTarget_Inventory = Inventory_Hand.PlayerHand;

        // 初始化物品槽位数据
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            ItemSlot slot = Data.itemSlots[i];
            if (slot == null)
                continue;

            // Inventory 可能复用存档中的 ItemSlot，先清理上一轮玩家/UI 的旧监听。
            slot.onSlotDataChanged.Clear();
            slot.Index = i;
            slot.SlotMaxVolume = 100;
        }

        // 初始化事件系统
        Data.Event_RefreshUI = new();
        Data.Event_RefreshUI.Clear();
        Data.Event_RefreshUI += RefreshUI;
    }

    /// <summary>
    /// 在UI面板创建后调用此函数进行UI初始化
    /// 该方法应在EnsurePanelCreated中调用，确保basePanel已存在
    /// </summary>
    public virtual void InitUI()
    {
        if (basePanel == null)
        {
            Debug.LogError("Prefab_BasePanel 未设置,请在Inspector中的Mod_Inventory中设置对应Inventory的面板预制体");
            return;
        }
        ItemSlot_Parent = basePanel.transform.GetComponentInChildren<UI_Content>().transform;

        if (ItemSlot_Parent == null)
        {
            Debug.LogError("ItemSlot_Parent 未设置");
            return;
        }

        // 加载Slot UI预制体
        ItemSlot_Prefab = GameRes.Instance.GetPrefab("UI_Slot");

        // 同步槽位数量与 itemSlots 保持一致
        int currentCount = ItemSlot_Parent.childCount;
        int targetCount = Data.itemSlots.Count;

        // 运行时不直接销毁多余槽位，避免在触发器/动画等回调链里触发 DestroyImmediate 报错。
        for (int i = 0; i < currentCount; i++)
        {
            ItemSlot_Parent.GetChild(i).gameObject.SetActive(i < targetCount);
        }

        // 创建缺少的槽位
        for (int i = currentCount; i < targetCount; i++)
        {
            GameObject item = GameObject.Instantiate(ItemSlot_Prefab, ItemSlot_Parent, false);
            item.SetActive(true);
        }

        // 重建UI列表并绑定数据
        itemSlot_UI.Clear();
        for (int i = 0; i < targetCount; i++)
        {
            var ui = ItemSlot_Parent.GetChild(i).GetComponent<ItemSlot_UI>();
            if (ui != null)
                itemSlot_UI.Add(ui);
        }

        // 同步 UI 数据
        SyncData();

        //初始化时自动同步UI显示
        RefreshUI();

        InventorySortButton.EnsureFor(this);

        // 槽位是运行时动态创建的，必须在创建完成后重新收集组件并补齐导航图。
        Canvas.ForceUpdateCanvases();
        basePanel.RefreshUIComponents();
        if (UsesModalGameplayInputLock())
        {
            basePanel.PrepareForGamepadNavigation(
                preferredControlName: "UI_Slot",
                closeOnCancel: true);
        }
    }
    //同步UI与Data
    public void SyncData()
    {
        if (Data == null || Data.itemSlots == null)
        {
            Debug.LogError($"[Inventory.SyncData] Data 或 Data.itemSlots 为空！");
            return;
        }

        // 空检查：确保数据和UI列表都初始化
        if (itemSlot_UI == null || itemSlot_UI.Count == 0)
        {
            Debug.LogWarning($"[Inventory.SyncData] itemSlotUIs 为空或未初始化！InventoryName: {Data?.Name}");
            return;
        }

        // 检查数量匹配
        if (itemSlot_UI.Count != Data.itemSlots.Count)
        {
            Debug.LogWarning($"[Inventory.SyncData] UI槽位数({itemSlot_UI.Count}) 与 Data槽位数({Data.itemSlots.Count}) 不匹配！");
        }

        int bindCount = Mathf.Min(itemSlot_UI.Count, Data.itemSlots.Count);
        for (int i = 0; i < bindCount; i++)
        {
            ItemSlot_UI itemSlotUI = itemSlot_UI[i];

            // 检查UI是否存在
            if (itemSlotUI == null)
            {
                Debug.LogError($"[Inventory.SyncData] itemSlotUIs[{i}] 为空！");
                continue;
            }

            // 检查 Data.itemSlots[i] 是否为 null
            if (Data.itemSlots[i] == null)
            {
                Debug.LogError($"[Inventory.SyncData] Data.itemSlots[{i}] 为空！");
                continue;
            }

            // 初始化UI槽位（替代 itemSlotUI.Data = Data.itemSlots[i]）
            itemSlotUI.InitializeSlot(i,
                index => Data.itemSlots[index],  // GetSlotDataFunc
                index =>
                {
                    if (Data.itemSlots[index] != null)
                    {
                        Data.itemSlots[index].ClearData();
                    }
                }  // ClearSlotDataAction
            );

            itemSlotUI.OnLeftClick.Clear();
            itemSlotUI.OnGamepadSubmit.Clear();
            itemSlotUI._OnScroll.Clear();
            itemSlotUI.OnRightClick.Clear();
            itemSlotUI.OnShiftQuickTransfer.Clear();
            itemSlotUI.OnMouseDragBegin = null;
            itemSlotUI.OnMouseDragDrop = null;
            itemSlotUI.OnTouchTap = null;
            itemSlotUI.OnTouchLongPress = null;
            itemSlotUI.OnDesktopTap = null;

            itemSlotUI.OnLeftClick += OnLeftClick;
            itemSlotUI.OnGamepadSubmit += OnGamepadSubmit;
            itemSlotUI._OnScroll += OnScroll;
            itemSlotUI.OnRightClick += OnRightClick;
            itemSlotUI.OnShiftQuickTransfer += OnShiftQuickTransfer;
            itemSlotUI.OnMouseDragBegin = OnMouseDragBegin;
            itemSlotUI.OnMouseDragDrop = OnMouseDragDrop;
            itemSlotUI.OnTouchTap = OnTouchTap;
            itemSlotUI.OnTouchLongPress = OnTouchLongPress;
            itemSlotUI.OnDesktopTap = OnDesktopTap;

            // 修复 Belong_Inventory 的逻辑，将其设置为当前 Inventory 实例
            if (Data.itemSlots[i].onSlotDataChanged != null)
            {
                Data.itemSlots[i].onSlotDataChanged.Clear();
                Data.itemSlots[i].onSlotDataChanged += OnItemSlotChanged;
            }
            else
            {
                Debug.LogWarning($"[Inventory.SyncData] Data.itemSlots[{i}].onSlotDataChanged 为空！");
            }
        }

        for (int i = bindCount; i < itemSlot_UI.Count; i++)
        {
            if (itemSlot_UI[i] != null)
                itemSlot_UI[i].gameObject.SetActive(false);
        }
    }

    public void BindSlotUI(ItemSlot_UI slotUI, int bindIndex = -1)
    {
        if (slotUI == null)
        {
            Debug.LogError("[Inventory.BindSlotUI] slotUI 为空");
            return;
        }

        if (Data == null || Data.itemSlots == null || Data.itemSlots.Count == 0)
        {
            Debug.LogError("[Inventory.BindSlotUI] Data 无效或没有可绑定的槽位");
            return;
        }

        if (bindIndex == -1)
            bindIndex = 0;

        int fixedIndex = Mathf.Clamp(bindIndex, 0, Data.itemSlots.Count - 1);

        RegisterSlotUI(slotUI, fixedIndex);

        slotUI.InitializeSlot(
            fixedIndex,
            index => index < 0 ? Data.itemSlots[fixedIndex] : Data.GetItemSlot(index),
            index =>
            {
                int targetIndex = index < 0 ? fixedIndex : index;
                ItemSlot targetSlot = Data.GetItemSlot(targetIndex);
                Data.RemoveItemAll(targetSlot, targetIndex);
            });

        slotUI.OnLeftClick.Clear();
        slotUI.OnGamepadSubmit.Clear();
        slotUI._OnScroll.Clear();
        slotUI.OnRightClick.Clear();
        slotUI.OnShiftQuickTransfer.Clear();
        slotUI.OnMouseDragBegin = null;
        slotUI.OnMouseDragDrop = null;
        slotUI.OnTouchTap = null;
        slotUI.OnTouchLongPress = null;
        slotUI.OnDesktopTap = null;

        slotUI.OnLeftClick += OnLeftClick;
        slotUI.OnGamepadSubmit += OnGamepadSubmit;
        slotUI._OnScroll += OnScroll;
        slotUI.OnRightClick += OnRightClick;
        slotUI.OnShiftQuickTransfer += OnShiftQuickTransfer;
        slotUI.OnMouseDragBegin = OnMouseDragBegin;
        slotUI.OnMouseDragDrop = OnMouseDragDrop;
        slotUI.OnTouchTap = OnTouchTap;
        slotUI.OnTouchLongPress = OnTouchLongPress;
        slotUI.OnDesktopTap = OnDesktopTap;

        slotUI.RefreshUI();
    }

    private void RegisterSlotUI(ItemSlot_UI slotUI, int bindIndex)
    {
        if (bindIndex < itemSlot_UI.Count)
        {
            itemSlot_UI[bindIndex] = slotUI;
            return;
        }

        if (bindIndex == itemSlot_UI.Count)
        {
            itemSlot_UI.Add(slotUI);
            return;
        }

        Debug.LogError($"[Inventory.BindSlotUI] UI注册顺序错误，当前列表长度: {itemSlot_UI.Count}，尝试注册索引: {bindIndex}");
    }

    // 当物品槽数据发生变化时的回调
    private void OnItemSlotChanged(ItemSlot slot)
    {
        // 防守性编程：检查slot和Data是否为空
        if (slot == null || Data == null || Data.itemSlots == null)
        {
            Debug.LogWarning($"[Inventory.OnItemSlotChanged] slot、Data 或 Data.itemSlots 为空！");
            return;
        }

        // 找到对应的UI并刷新
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            if (Data.itemSlots[i] != null && Data.itemSlots[i] == slot)
            {
                RefreshUI(i);
                break;
            }
        }
    }

    #endregion

    #region 物品初始化

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

    #endregion

    #region UI

    //TODO 基于新输入系统实现按下B键打开和关闭背包UI




    public void RefreshUI(int index)
    {
        if (itemSlot_UI == null || index < 0 || index >= itemSlot_UI.Count)
            return;

        // 数据事件可能晚于面板销毁到达，Unity 已销毁的组件必须跳过。
        ItemSlot_UI slotUI = itemSlot_UI[index];
        if (slotUI == null)
            return;

        slotUI.RefreshUI();
    }

    public void RefreshUI()
    {
        if (itemSlot_UI == null)
            return;

        for (int i = 0; i < itemSlot_UI.Count; i++)
        {
            ItemSlot_UI slotUI = itemSlot_UI[i];
            if (slotUI != null)
                slotUI.RefreshUI();
        }
    }

    public virtual void Interact_Start(Item item_)
    {
        SwitchUI();
    }

    #endregion

    #region 鼠标事件处理

    void OnRightClick(int index)
    {
        if (Data == null || Data.itemSlots == null)
        {
            Debug.LogError("[Inventory.OnRightClick] Data 或 itemSlots 为空");
            return;
        }

        if (itemSlot_UI == null)
        {
            Debug.LogError("[Inventory.OnRightClick] itemSlot_UI 为空");
            return;
        }

        if (index < 0 || index >= Data.itemSlots.Count)
        {
            Debug.LogError($"[Inventory.OnRightClick] 索引越界: {index}");
            return;
        }

        if (index >= itemSlot_UI.Count || itemSlot_UI[index] == null)
        {
            Debug.LogError($"[Inventory.OnRightClick] itemSlot_UI 索引无效: {index}");
            return;
        }

        ItemSlot slot = Data.itemSlots[index];
        if (slot == null || slot.itemData == null)
        {
            return;
        }

        GameObject menuPrefab = GameRes.Instance.GetPrefab("UI_ItemContextMenu");
        if (menuPrefab == null)
        {
            Debug.LogError("[Inventory.OnRightClick] 未找到预制体: UI_ItemContextMenu");
            return;
        }

        RightClickMenu_UI menuPrefabUI = menuPrefab.GetComponent<RightClickMenu_UI>();
        if (menuPrefabUI == null)
        {
            Debug.LogError("[Inventory.OnRightClick] 右键菜单预制体缺少 RightClickMenu_UI 组件");
            return;
        }

        CloseActiveContextMenu();

        BasePanel menuPanel = UIManager.Instance.CreatePanelFromGameObject(menuPrefab);
        RightClickMenu_UI currentMenuInstance = menuPanel != null
            ? menuPanel.GetComponent<RightClickMenu_UI>()
            : null;
        if (currentMenuInstance == null)
        {
            Debug.LogError("[Inventory.OnRightClick] 右键菜单实例缺少 RightClickMenu_UI 组件");
            return;
        }

        currentMenuInstance.Init(itemSlot_UI[index], slot, item);
        _activeContextMenuPanel = menuPanel;

        RectTransform menuRect = currentMenuInstance.GetComponent<RectTransform>();
        if (currentMenuInstance.basePanel != null && currentMenuInstance.basePanel.Dragger != null && currentMenuInstance.basePanel.Dragger.rectTransform != null)
        {
            currentMenuInstance.basePanel.Dragger.rectTransform.position = itemSlot_UI[index].transform.position;
        }
        else if (menuRect != null)
        {
            menuRect.position = itemSlot_UI[index].transform.position;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnScroll(int index, float direction)
    {
        if (!TryEnsureDefaultTargetInventory())
            return;

        if (Data == null || Data.itemSlots == null || index < 0 || index >= Data.itemSlots.Count)
            return;

        if (direction > 0)
        {
            Data.TransferItemQuantity(DefaultTarget_Inventory.Data.itemSlots[0], Data.itemSlots[index], 1);
        }
        else if (direction < 0)
        {
            Data.TransferItemQuantity(Data.itemSlots[index], DefaultTarget_Inventory.Data.itemSlots[0], 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void OnLeftClick(int index)
    {
        ProcessSlotInteraction(
            index,
            _isProcessingGamepadSubmit
                ? SlotInteractionSource.Gamepad
                : SlotInteractionSource.KeyboardMouse);
    }

    /// <summary>触屏轻触执行单件取放、同类追加或异类交换。</summary>
    public virtual void OnTouchTap(int index)
    {
        if (!TryGetPlayerHandSlots(index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot))
            return;

        bool handWasEmpty = handSlot.itemData == null;
        bool localHadItem = localSlot.itemData != null;
        bool sameType = localHadItem && !handWasEmpty &&
                        localSlot.itemData.CanStackWith(handSlot.itemData);
        bool preferPickupSameType = _touchTapFlow == TouchTapFlow.Pickup && sameType;

        // 手上物品放入带限制的目标槽位前，复用库存接收规则。
        if (handSlot.itemData != null && !CanAcceptQuickTransfer(handSlot, localSlot))
            return;

        if (!Data.TouchTapItem(localSlot, handInventory.Data, handSlot, preferPickupSameType))
            return;

        RefreshUI(index);
        handInventory.RefreshUI(handSlot.Index);

        if (!HasHeldItem(handInventory))
        {
            _touchTapFlow = TouchTapFlow.None;
        }
        else if ((handWasEmpty && localHadItem) || preferPickupSameType)
        {
            // 空手拿起或在拿取方向点击同类槽后，继续点击可继续拿取。
            _touchTapFlow = TouchTapFlow.Pickup;
        }
        else if (!localHadItem || sameType)
        {
            // 点击空槽或放置方向的同类槽后，继续点击可连续分发。
            _touchTapFlow = TouchTapFlow.PutDown;
        }
        else
        {
            // 异类交换完成后重新等待下一次明确的取放方向。
            _touchTapFlow = TouchTapFlow.None;
        }
    }

    /// <summary>触屏长按空槽或同类槽时，把玩家手上整组物品一次性放入目标槽。</summary>
    public virtual bool OnTouchLongPress(int index)
    {
        if (!TryGetPlayerHandSlots(index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot) ||
            handSlot.itemData == null || !CanAcceptQuickTransfer(handSlot, localSlot))
            return false;

        // 长按只允许整组放入空槽或同类槽，异类槽继续交给物品菜单处理。
        if (localSlot.itemData != null && !localSlot.itemData.CanStackWith(handSlot.itemData))
            return false;

        if (!Data.DropDraggedItem(localSlot, handInventory.Data, handSlot))
            return false;

        RefreshUI(index);
        handInventory.RefreshUI(handSlot.Index);

        if (this is Inventory_HotBar.HotBarRuntimeInventory hotBarInventory)
            hotBarInventory.SyncHeldItemImmediately();

        _touchTapFlow = HasHeldItem(handInventory)
            ? TouchTapFlow.PutDown
            : TouchTapFlow.None;

        return true;
    }

    /// <summary>桌面空手轻触不改变库存；拖拽后手上有整组物品时，轻触执行单件取放。</summary>
    public virtual void OnDesktopTap(int index)
    {
        if (HasTouchHeldItem())
            OnTouchTap(index);
    }

    /// <summary>判断当前库存是否存在可继续执行单件取放的手持物品。</summary>
    public bool HasTouchHeldItem()
    {
        return HasHeldItem(GetPlayerHandInventory());
    }

    /// <summary>解析当前槽位、玩家手部库存和手部槽位。</summary>
    private bool TryGetPlayerHandSlots(int index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot)
    {
        handInventory = GetPlayerHandInventory();
        localSlot = null;
        handSlot = null;

        if (!IsValidQuickTransferTarget(handInventory) || Data == null || Data.itemSlots == null ||
            index < 0 || index >= Data.itemSlots.Count || handInventory == this)
            return false;

        localSlot = Data.itemSlots[index];
        handSlot = handInventory.Data.itemSlots[0];
        return localSlot != null && handSlot != null;
    }

    /// <summary>
    /// 鼠标拖拽必须固定使用玩家手上槽位，避免玩家行囊的普通点击规则把物品送入快捷栏。
    /// </summary>
    public virtual bool OnMouseDragBegin(int index)
    {
        if (Data == null || Data.itemSlots == null || index < 0 || index >= Data.itemSlots.Count)
            return false;

        Inventory handInventory = GetPlayerHandInventory();
        if (!IsValidQuickTransferTarget(handInventory))
        {
            Debug.LogWarning($"[Inventory.OnMouseDragBegin] 玩家手上库存不可用，当前库存={Data.Name}, 索引={index}");
            return false;
        }

        DefaultTarget_Inventory = handInventory;
        ProcessSlotInteraction(index, SlotInteractionSource.MouseDrag);
        bool hasHeldItem = HasHeldItem(handInventory);
        _touchTapFlow = hasHeldItem
            ? TouchTapFlow.PutDown
            : TouchTapFlow.None;
        return hasHeldItem;
    }

    /// <summary>拖拽结束时把玩家手上整堆物品定向放入目标槽位。</summary>
    public virtual void OnMouseDragDrop(int index)
    {
        if (!TryGetPlayerHandSlots(index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot) ||
            handSlot.itemData == null || !CanAcceptQuickTransfer(handSlot, localSlot))
            return;

        if (!Data.DropDraggedItem(localSlot, handInventory.Data, handSlot))
            return;

        RefreshUI(index);
        handInventory.RefreshUI(handSlot.Index);

        if (this is Inventory_HotBar.HotBarRuntimeInventory hotBarInventory)
            hotBarInventory.SyncHeldItemImmediately();

        _touchTapFlow = HasHeldItem(handInventory)
            ? TouchTapFlow.PutDown
            : TouchTapFlow.None;
    }

    /// <summary>
    /// 手柄 A/Submit 的槽位操作入口，使用独立的快捷栏目标，不改写键鼠手上槽位状态。
    /// </summary>
    public virtual void OnGamepadSubmit(int index)
    {
        bool previousValue = _isProcessingGamepadSubmit;
        _isProcessingGamepadSubmit = true;
        try
        {
            // 通过虚拟标记调用虚拟 OnLeftClick，保留装备栏、快捷栏等派生库存的专用校验。
            OnLeftClick(index);
        }
        finally
        {
            _isProcessingGamepadSubmit = previousValue;
        }
    }

    private enum SlotInteractionSource
    {
        KeyboardMouse,
        Gamepad,
        MouseDrag
    }

    /// <summary>
    /// 执行一次槽位交换，并让输入设备决定本次使用的目标库存。
    /// </summary>
    private void ProcessSlotInteraction(int index, SlotInteractionSource source)
    {
        if (Data == null || Data.itemSlots == null || index < 0 || index >= Data.itemSlots.Count)
            return;

        if (!TryResolveSlotInteractionTarget(source))
        {
            Debug.LogWarning($"[Inventory.ProcessSlotInteraction] DefaultTarget_Inventory 未设置，当前库存={Data.Name}, 索引={index}, 输入={source}");
            return;
        }

        int targetIndex = GetLeftClickTargetSlotIndex(index);
        Data.ChangeItemData_Default(index, DefaultTarget_Inventory.Data.itemSlots[targetIndex]);
        DefaultTarget_Inventory.RefreshUI(targetIndex);

        // 玩家背包转入快捷栏后，立即刷新当前手持实例，避免等待下一次模块 Tick 才显示。
        if (DefaultTarget_Inventory is Inventory_HotBar.HotBarRuntimeInventory hotBarInventory)
            hotBarInventory.SyncHeldItemImmediately();

        RefreshUI(index);
    }

    /// <summary>
    /// 键鼠始终使用鼠标携带槽；手柄确认玩家行囊时才固定使用当前快捷栏槽位。
    /// </summary>
    private bool TryResolveSlotInteractionTarget(SlotInteractionSource source)
    {
        if (source == SlotInteractionSource.MouseDrag)
        {
            Inventory handInventory = GetPlayerHandInventory();
            if (IsValidQuickTransferTarget(handInventory))
            {
                DefaultTarget_Inventory = handInventory;
                return true;
            }
        }

        if (IsPlayerBagInventory() && source == SlotInteractionSource.Gamepad)
        {
            Inventory hotBar = GetPlayerHotBarInventory();
            if (IsValidQuickTransferTarget(hotBar))
            {
                DefaultTarget_Inventory = hotBar;
                return true;
            }
        }

        if (IsPlayerBagInventory() && source == SlotInteractionSource.KeyboardMouse)
        {
            Inventory handInventory = GetPlayerHandInventory();
            if (IsValidQuickTransferTarget(handInventory))
            {
                DefaultTarget_Inventory = handInventory;
                return true;
            }
        }

        return TryEnsureDefaultTargetInventory();
    }

    private bool TryEnsureDefaultTargetInventory()
    {
        // 玩家背包的左键操作应进入快捷栏，否则物品只会落入不可见的手部缓冲槽。
        if (IsPlayerBagInventory())
        {
            if (DefaultTarget_Inventory?.Data?.Name == ModText.Hotbar &&
                IsValidQuickTransferTarget(DefaultTarget_Inventory))
                return true;

            Inventory hotBar = GetPlayerHotBarInventory();
            if (IsValidQuickTransferTarget(hotBar))
            {
                DefaultTarget_Inventory = hotBar;
                return true;
            }
        }

        if (DefaultTarget_Inventory != null && DefaultTarget_Inventory.Data != null && DefaultTarget_Inventory.Data.itemSlots != null && DefaultTarget_Inventory.Data.itemSlots.Count > 0)
            return true;

        Inventory handInventory = GetPlayerHandInventory();
        if (IsValidQuickTransferTarget(handInventory))
        {
            DefaultTarget_Inventory = handInventory;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取当前玩家的手上库存，优先使用背包所属玩家，避免静态引用残留到旧玩家。
    /// </summary>
    private Inventory GetPlayerHandInventory()
    {
        Inventory handInventory = item?.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (IsValidQuickTransferTarget(handInventory))
            return handInventory;

        if (item != null && item.itemMods != null && item.itemMods.ContainsKey_ID(ModText.Hand))
        {
            IInventory handInventoryProvider = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>();
            handInventory = handInventoryProvider?.GetDefaultTargetInventory();
            if (IsValidQuickTransferTarget(handInventory))
                return handInventory;
        }

        return IsValidQuickTransferTarget(Inventory_Hand.PlayerHand)
            ? Inventory_Hand.PlayerHand
            : null;
    }

    /// <summary>
    /// 判断手上槽位是否确实持有可交换物品。
    /// </summary>
    private static bool HasHeldItem(Inventory handInventory)
    {
        if (!IsValidQuickTransferTarget(handInventory))
            return false;

        for (int i = 0; i < handInventory.Data.itemSlots.Count; i++)
        {
            ItemSlot slot = handInventory.Data.itemSlots[i];
            if (slot?.itemData != null && slot.itemData.Stack != null && slot.itemData.Stack.Amount > 0f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 玩家背包点击时使用快捷栏当前选中槽；普通容器仍沿用原有手部槽位映射。
    /// </summary>
    private int GetLeftClickTargetSlotIndex(int sourceIndex)
    {
        if (DefaultTarget_Inventory is Inventory_HotBar.HotBarRuntimeInventory hotBarInventory &&
            hotBarInventory.Data?.itemSlots != null &&
            hotBarInventory.Data.itemSlots.Count > 0)
        {
            return Mathf.Clamp(
                hotBarInventory.Data.Index,
                0,
                hotBarInventory.Data.itemSlots.Count - 1);
        }

        return DefaultTarget_Inventory.Data.itemSlots.Count > sourceIndex ? sourceIndex : 0;
    }

    /// <summary>
    /// 仅识别本地玩家的主背包，避免改变箱子、工作台和其他独立库存的手部交换规则。
    /// </summary>
    private bool IsPlayerBagInventory()
    {
        return item is Player && Data?.Name == ModText.Bag;
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
        if (!IsValidQuickTransferTarget(targetInventory) || targetInventory == this)
            return false;

        return TryQuickMoveSlotToInventory(index, targetInventory);
    }

    private Inventory ResolveShiftQuickTransferTarget()
    {
        if (IsHotBarInventory())
            return GetValidLastOpenedContainer();

        return GetPlayerHotBarInventory();
    }

    private bool TryQuickMoveSlotToInventory(int sourceIndex, Inventory targetInventory)
    {
        if (Data == null || Data.itemSlots == null || targetInventory == null || targetInventory.Data == null || targetInventory.Data.itemSlots == null)
            return false;

        if (sourceIndex < 0 || sourceIndex >= Data.itemSlots.Count)
            return false;

        ItemSlot sourceSlot = Data.itemSlots[sourceIndex];
        if (sourceSlot == null || sourceSlot.itemData == null)
            return false;

        bool moved = false;
        moved |= TryTransferToMatchedSlots(sourceSlot, targetInventory);
        moved |= TryTransferToEmptySlots(sourceSlot, targetInventory);

        if (moved)
        {
            RefreshUI(sourceIndex);
            targetInventory.RefreshUI();
        }

        return moved;
    }

    private bool TryTransferToMatchedSlots(ItemSlot sourceSlot, Inventory targetInventory)
    {
        bool moved = false;
        List<ItemSlot> targetSlots = targetInventory.Data.itemSlots;

        for (int i = 0; i < targetSlots.Count; i++)
        {
            if (sourceSlot.itemData == null)
                break;

            ItemSlot targetSlot = targetSlots[i];
            if (targetSlot == null || targetSlot.itemData == null)
                continue;

            if (!targetSlot.itemData.CanStackWith(sourceSlot.itemData))
                continue;

            int transferCount = Mathf.CeilToInt(sourceSlot.itemData.Stack.Amount);
            if (transferCount <= 0)
                break;

            if (TryTransferQuickQuantity(sourceSlot, targetInventory, targetSlot, transferCount))
                moved = true;
        }

        return moved;
    }

    private bool TryTransferToEmptySlots(ItemSlot sourceSlot, Inventory targetInventory)
    {
        bool moved = false;
        List<ItemSlot> targetSlots = targetInventory.Data.itemSlots;

        for (int i = 0; i < targetSlots.Count; i++)
        {
            if (sourceSlot.itemData == null)
                break;

            ItemSlot targetSlot = targetSlots[i];
            if (targetSlot == null || targetSlot.itemData != null)
                continue;

            int transferCount = Mathf.CeilToInt(sourceSlot.itemData.Stack.Amount);
            if (transferCount <= 0)
                break;

            if (TryTransferQuickQuantity(sourceSlot, targetInventory, targetSlot, transferCount))
                moved = true;
        }

        return moved;
    }

    private bool TryTransferQuickQuantity(
        ItemSlot sourceSlot,
        Inventory targetInventory,
        ItemSlot targetSlot,
        int transferCount)
    {
        if (!targetInventory.CanAcceptQuickTransfer(sourceSlot, targetSlot))
            return false;

        return Data.TransferItemQuantityTo(
            sourceSlot,
            targetInventory.Data,
            targetSlot,
            transferCount);
    }

    /// <summary>
    /// 快速转移的目标槽接收策略。普通槽默认允许所有物品；
    /// 配置了 CanAcceptTags 时至少需匹配一个标签，特殊库存可覆写追加业务规则。
    /// </summary>
    public virtual bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot)
    {
        ItemData sourceItem = sourceSlot?.itemData;
        if (sourceItem == null || targetSlot == null)
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

        Inventory_HotBar hotbarModule = ownerItem.itemMods.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
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

        if (LastOpenedContainer.basePanel == null || !LastOpenedContainer.basePanel.IsOpen())
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
    public void SyncQuickTransferTarget(BasePanel ownerPanel = null)
    {
        if (ownerPanel != null)
            basePanel = ownerPanel;

        if (IsHotBarInventory() || IsHandInventory())
            return;

        if (basePanel != null && basePanel.IsOpen())
        {
            LastOpenedContainer = this;
            return;
        }

        if (LastOpenedContainer == this)
            LastOpenedContainer = null;
    }

    private bool IsHotBarInventory()
    {
        return Data?.Name == ModText.Hotbar;
    }

    private bool IsHandInventory()
    {
        return this is Inventory_Hand || Data?.Name == ModText.Hand;
    }

    /// <summary>只有玩家主动打开的库存面板才参与模态输入锁与手柄焦点链。</summary>
    private bool UsesModalGameplayInputLock()
    {
        return !IsHotBarInventory() && !IsHandInventory();
    }

    #endregion

    #region 运行时容量调整

    /// <summary>
    /// 在游戏运行时为背包动态添加额外槽位
    /// 例如传入 3 则在当前基础上再增加 3 个空槽位
    /// </summary>
    /// <param name="extraSlotCount">需要增加的槽位数量（必须 > 0）</param>
    public void AddSlotsAtRuntime(int extraSlotCount)
    {
        if (extraSlotCount <= 0)
        {
            Debug.LogWarning($"[Inventory.AddSlotsAtRuntime] 额外槽位数量({extraSlotCount}) <= 0，已忽略。");
            return;
        }

        if (Data == null)
        {
            Debug.LogError("[Inventory.AddSlotsAtRuntime] Data 为空，无法添加槽位。");
            return;
        }

        if (Data.itemSlots == null)
        {
            Debug.LogError("[Inventory.AddSlotsAtRuntime] Data.itemSlots 为空，无法添加槽位。");
            return;
        }

        // 在数据层面增加空槽位
        for (int i = 0; i < extraSlotCount; i++)
        {
            Data.itemSlots.Add(new ItemSlot());
        }

        // 重新初始化数据（索引、容量、事件等）
        InitData();

        // 如果 UI 已创建，则重新初始化 UI，同步槽位数量和所有监听
        if (basePanel != null)
        {
            InitUI();
        }
    }

    #endregion

    #region 编辑器功能

    [Button("同步槽位数量")]
    public void SyncSlotCount()
    {
        Data.itemSlots.Clear();
        int currentCount = ItemSlot_Parent.childCount;
        for (int i = 0; i < ItemSlot_Parent.childCount; i++)
        {
            Data.itemSlots.Add(new ItemSlot());
        }
    }

    #endregion

    #region 注入物品逻辑（从Inventory_Data移动过来）

    /// <summary>
    /// 随机顺序自动注入物品列表到容器中
    /// </summary>
    public void RandomOrderAutoInjectItemDataList(List<GameObject> prefabList, List<int> countList)
    {
        if (prefabList == null || countList == null) return;
        if (prefabList.Count != countList.Count) return;

        // --- Step1: 打乱物品顺序 ---
        List<int> itemIndices = new List<int>();
        for (int i = 0; i < prefabList.Count; i++)
        {
            itemIndices.Add(i);
        }

        for (int i = itemIndices.Count - 1; i > 0; i--)
        {
            int r = UnityEngine.Random.Range(0, i + 1);
            int temp = itemIndices[i];
            itemIndices[i] = itemIndices[r];
            itemIndices[r] = temp;
        }

        // --- Step2: 收集所有空槽位并打乱 ---
        List<int> emptySlots = new List<int>();
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            if (Data.itemSlots[i].itemData == null)
                emptySlots.Add(i);
        }

        for (int i = emptySlots.Count - 1; i > 0; i--)
        {
            int r = UnityEngine.Random.Range(0, i + 1);
            int temp = emptySlots[i];
            emptySlots[i] = emptySlots[r];
            emptySlots[r] = temp;
        }

        // --- Step3: 按随机顺序把物品塞进随机槽位 ---
        int successCount = 0;
        int failCount = 0;

        for (int i = 0; i < itemIndices.Count && i < emptySlots.Count; i++)
        {
            int randomItemIndex = itemIndices[i];
            int slotIndex = emptySlots[i];

            GameObject prefab = prefabList[randomItemIndex];
            int count = countList[randomItemIndex];

            if (prefab == null || count <= 0) { failCount++; continue; }

            var itemComp = prefab.GetComponent<Item>();
            if (itemComp == null) { failCount++; continue; }

            string itemId = itemComp.itemData?.IDName ?? prefab.name;
            ItemData itemData = GameRes.Instance?.CreateItemData(itemId) ?? itemComp.Get_NewItemData();
            if (itemData == null) { failCount++; continue; }

            itemData.Stack.Amount = count;
            itemData.Stack.CanBePickedUp = false;

            Data.SetOne_ItemData(slotIndex, itemData);
            Data.Event_RefreshUI.Invoke(slotIndex);

            successCount++;
        }

        Debug.Log($"随机注入完成：成功 {successCount}，失败 {failCount}");
    }

    /// <summary>
    /// 自动注入物品列表到容器中，智能查找空槽位或可堆叠槽位，避免覆盖已有物品
    /// </summary>
    /// <param name="prefabList">物品预制体列表</param>
    /// <param name="countList">对应物品数量列表</param>
    [Button("自动注入物品列表")]
    public void AutoInjectItemDataList(
        [LabelText("物品预制体列表")] List<GameObject> prefabList,
        [LabelText("数量列表")] List<int> countList)
    {
        // 参数验证
        if (prefabList == null || countList == null)
        {
            Debug.LogError("自动注入失败：Prefab列表或数量列表不能为空");
            return;
        }

        if (prefabList.Count != countList.Count)
        {
            Debug.LogError($"自动注入失败：Prefab列表数量({prefabList.Count})与数量列表数量({countList.Count})不匹配");
            return;
        }

        if (prefabList.Count == 0)
        {
            Debug.LogWarning("自动注入失败：Prefab列表为空");
            return;
        }

        int successCount = 0;
        int failCount = 0;

        // 遍历并自动注入每个物品
        for (int i = 0; i < prefabList.Count; i++)
        {
            GameObject prefab = prefabList[i];
            int count = countList[i];

            if (prefab == null)
            {
                Debug.LogWarning($"跳过空的Prefab（索引 {i}）");
                failCount++;
                continue;
            }

            if (count <= 0)
            {
                Debug.LogWarning($"跳过无效数量 {count} 的物品 {prefab.name}（索引 {i}）");
                failCount++;
                continue;
            }

            // 获取Prefab上的Item组件
            Item itemComponent = prefab.GetComponent<Item>();
            if (itemComponent == null)
            {
                Debug.LogError($"自动注入失败：Prefab {prefab.name} 上找不到Item组件（索引 {i}）");
                failCount++;
                continue;
            }

            // 克隆ItemData
            string itemId = itemComponent.itemData?.IDName ?? prefab.name;
            ItemData itemData = GameRes.Instance?.CreateItemData(itemId) ?? itemComponent.Get_NewItemData();
            if (itemData == null)
            {
                Debug.LogError($"自动注入失败：无法克隆 {prefab.name} 的ItemData（索引 {i}）");
                failCount++;
                continue;
            }

            // 设置数量
            itemData.Stack.Amount = count;
            itemData.Stack.CanBePickedUp = false;

            // 尝试添加物品
            if (Data.TryAddItem(itemData, true))
            {
                Debug.Log($"成功自动注入物品 {prefab.name} x{count}");
                successCount++;
            }
            else
            {
                Debug.LogError($"自动注入失败：容器空间不足，无法注入物品 {prefab.name} x{count}");
                failCount++;
            }
        }

        Debug.Log($"自动注入物品列表完成：成功 {successCount} 个，失败 {failCount} 个");
    }

    // 重载方法：支持统一数量
    [Button("自动注入物品列表(统一数量)")]
    public void AutoInjectItemDataList(
        [LabelText("物品预制体列表")] List<GameObject> prefabList,
        [LabelText("统一数量"), MinValue(1)] int uniformCount = 1)
    {
        if (prefabList == null)
        {
            Debug.LogError("自动注入失败：Prefab列表不能为空");
            return;
        }

        // 创建统一数量列表
        List<int> countList = new List<int>();
        for (int i = 0; i < prefabList.Count; i++)
        {
            countList.Add(uniformCount);
        }

        AutoInjectItemDataList(prefabList, countList);
    }

    #endregion

    #region 保存
    public virtual void Save()
    {

    }

    #endregion
}
