using UnityEngine;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

    #endregion

    #region 生命周期

    public virtual void OnValidate()
    {
        if (string.IsNullOrEmpty(Data.Name))
            Data.Name = ModText.Bag;
    }

    public virtual void Awake()
    {

    }

    public virtual void ModUpdate(float deltaTime)
    {

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
            Debug.LogWarning("[Inventory.BindController] ToggleActionName 为空，取消绑定");
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
            SwitchUI();
        };

        action.performed += _toggleCallback;

        _boundController = gameController;
        _boundToggleAction = action;
    }

    /// <summary>
    /// 解除通过 BindController 建立的输入绑定
    /// </summary>
    public void UnbindController()
    {
        Data.Event_RefreshUI -= RefreshUI;

        if (_boundToggleAction != null && _toggleCallback != null)
        {
            _boundToggleAction.performed -= _toggleCallback;
        }

        _boundController = null;
        _boundToggleAction = null;
        _toggleCallback = null;
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
            TryMarkAsLastOpenedContainer();
            // 延迟一帧将面板置顶，确保不会被其他UI遮挡
            GameManager.Instance.StartCoroutine(DelayedBringToFront(basePanel.GetComponent<RectTransform>()));
            return;
        }

        basePanel.Toggle();
        if (basePanel.IsOpen())
        {
            TryMarkAsLastOpenedContainer();
            // 延迟一帧将面板置顶，确保不会被其他UI遮挡
            GameManager.Instance.StartCoroutine(DelayedBringToFront(basePanel.GetComponent<RectTransform>()));
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
        if (basePanel.GetText("窗口信息") != null)
            basePanel.GetText("窗口信息").text = Data.Name;

        // 调用UI初始化方法（此时basePanel已存在）
        InitUI();

        return true; // 成功创建了面板
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
            Data.itemSlots[i].Index = i;
            Data.itemSlots[i].SlotMaxVolume = 100;
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
        ItemSlot_Prefab = GameRes.Instance.GetPrefab("Slot_UI");

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
            itemSlotUI._OnScroll.Clear();
            itemSlotUI.OnRightClick.Clear();
            itemSlotUI.OnShiftQuickTransfer.Clear();

            itemSlotUI.OnLeftClick += OnLeftClick;
            itemSlotUI._OnScroll += OnScroll;
            itemSlotUI.OnRightClick += OnRightClick;
            itemSlotUI.OnShiftQuickTransfer += OnShiftQuickTransfer;

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
        slotUI._OnScroll.Clear();
        slotUI.OnRightClick.Clear();
        slotUI.OnShiftQuickTransfer.Clear();

        slotUI.OnLeftClick += OnLeftClick;
        slotUI._OnScroll += OnScroll;
        slotUI.OnRightClick += OnRightClick;
        slotUI.OnShiftQuickTransfer += OnShiftQuickTransfer;

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
        if (index < 0 || index >= itemSlot_UI.Count) return;
        itemSlot_UI[index].RefreshUI();
    }

    public void RefreshUI()
    {
        for (int i = 0; i < itemSlot_UI.Count; i++)
        {
            itemSlot_UI[i].RefreshUI();
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

        GameObject menuPrefab = GameRes.Instance.GetPrefab("右键菜单");
        if (menuPrefab == null)
        {
            Debug.LogError("[Inventory.OnRightClick] 未找到预制体: 右键菜单");
            return;
        }

        RightClickMenu_UI menuPrefabUI = menuPrefab.GetComponent<RightClickMenu_UI>();
        if (menuPrefabUI == null)
        {
            Debug.LogError("[Inventory.OnRightClick] 右键菜单预制体缺少 RightClickMenu_UI 组件");
            return;
        }

        RightClickMenu_UI currentMenuInstance;
        currentMenuInstance = GameObject.Instantiate(menuPrefabUI);
        currentMenuInstance.Init(itemSlot_UI[index], slot, item);

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
        if (Data == null || Data.itemSlots == null || index < 0 || index >= Data.itemSlots.Count)
            return;

        if (!TryEnsureDefaultTargetInventory())
        {
            Debug.LogWarning($"[Inventory.OnLeftClick] DefaultTarget_Inventory 未设置，当前库存={Data.Name}, 索引={index}");
            return;
        }

        //默认为手部
        if (DefaultTarget_Inventory.Data.itemSlots.Count > index)
        {
            Data.ChangeItemData_Default(index, DefaultTarget_Inventory.Data.itemSlots[index]);
            DefaultTarget_Inventory.RefreshUI(index);
        }
        else
        {
            Data.ChangeItemData_Default(index, DefaultTarget_Inventory.Data.itemSlots[0]);
            DefaultTarget_Inventory.RefreshUI(0);
        }

        RefreshUI(index);
    }

    private bool TryEnsureDefaultTargetInventory()
    {
        if (DefaultTarget_Inventory != null && DefaultTarget_Inventory.Data != null && DefaultTarget_Inventory.Data.itemSlots != null && DefaultTarget_Inventory.Data.itemSlots.Count > 0)
            return true;

        if (Inventory_Hand.PlayerHand != null && Inventory_Hand.PlayerHand.Data != null && Inventory_Hand.PlayerHand.Data.itemSlots != null && Inventory_Hand.PlayerHand.Data.itemSlots.Count > 0)
        {
            DefaultTarget_Inventory = Inventory_Hand.PlayerHand;
            return true;
        }

        if (item != null && item.itemMods != null && item.itemMods.ContainsKey_ID(ModText.Hand))
        {
            var handInventoryProvider = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>();
            var handInventory = handInventoryProvider?.GetDefaultTargetInventory();
            if (handInventory != null && handInventory.Data != null && handInventory.Data.itemSlots != null && handInventory.Data.itemSlots.Count > 0)
            {
                DefaultTarget_Inventory = handInventory;
                return true;
            }
        }

        return false;
    }

    public virtual void OnShiftQuickTransfer(int index)
    {
        Inventory targetInventory = ResolveShiftQuickTransferTarget();
        if (targetInventory == null || targetInventory == this)
            return;

        TryQuickMoveSlotToInventory(index, targetInventory);
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

            if (targetSlot.itemData.IDName != sourceSlot.itemData.IDName ||
                targetSlot.itemData.ItemSpecialData != sourceSlot.itemData.ItemSpecialData)
                continue;

            int transferCount = Mathf.CeilToInt(sourceSlot.itemData.Stack.Amount);
            if (transferCount <= 0)
                break;

            if (targetInventory.Data.TransferItemQuantity(sourceSlot, targetSlot, transferCount))
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

            if (targetInventory.Data.TransferItemQuantity(sourceSlot, targetSlot, transferCount))
                moved = true;
        }

        return moved;
    }

    private Inventory GetPlayerHotBarInventory()
    {
        Inventory hotBar = TryGetHotBarFromItem(item);
        if (hotBar != null)
            return hotBar;

        hotBar = TryGetHotBarFromItem(DefaultTarget_Inventory?.item);
        if (hotBar != null)
            return hotBar;

        hotBar = TryGetHotBarFromItem(Inventory_Hand.PlayerHand?.item);
        return hotBar;
    }

    private static Inventory TryGetHotBarFromItem(Item ownerItem)
    {
        if (ownerItem == null || ownerItem.itemMods == null)
            return null;

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
            return null;

        return LastOpenedContainer;
    }

    private void TryMarkAsLastOpenedContainer()
    {
        if (IsHotBarInventory() || IsHandInventory())
            return;

        if (basePanel == null || !basePanel.IsOpen())
            return;

        LastOpenedContainer = this;
    }

    private bool IsHotBarInventory()
    {
        return this is Inventory_HotBar || Data?.Name == ModText.Hotbar;
    }

    private bool IsHandInventory()
    {
        return this is Inventory_Hand || Data?.Name == ModText.Hand;
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

            var itemData = itemComp.Get_NewItemData();
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
            ItemData itemData = itemComponent.Get_NewItemData();
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