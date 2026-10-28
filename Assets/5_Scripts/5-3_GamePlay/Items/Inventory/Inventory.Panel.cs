using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Localization;
using TMPro;

// 库存面板、槽位绑定与容量显示。
public partial class Inventory
{
    #region 库存面板、槽位绑定与容量显示
    /// <summary>库存退出运行时由拥有者销毁自己的面板，防止旧玩家 UI 留在跨场景根节点。</summary>
    public void ReleaseRuntimePanel()
    {
        foreach (ItemSlot_UI slot in itemSlot_UI)
            if (slot != null) slot.ClearInventoryBinding(this);
        if (_flickTargets != null) _flickTargets.Unbind(this);
        _flickTargets = null;
        BasePanel panel = basePanel;
        if (panel != null)
            panel.Opened -= PositionBagAfterFirstOpen;
        defaultBagPlacementPending = false;
        basePanel = null;
        if (panel != null)
        {
            UIManager manager = UIManager.ExistingInstance;
            if (manager != null)
                manager.DestroyPanel(panel);
            else
                UnityEngine.Object.Destroy(panel.gameObject);
        }

        itemSlot_UI.Clear();
        ItemSlot_Prefab = null;
        ItemSlot_Parent = null;
        _virtualizedSlotGrid = null;
        _quickTransferOwnerPanel = null;
        _carryWeightValueText = null;
        _carryVolumeValueText = null;
        _carryWeightProgress = null;
        _carryVolumeProgress = null;
        if (ReferenceEquals(LastOpenedContainer, this))
            LastOpenedContainer = null;
    }
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
        // Unity 已销毁对象仍可能保留托管引用，先走 UnityEngine.Object 的空判断。
        if (item == null || item is not Player player || player.itemMods == null)
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

    /// <summary>首次可见布局和缩放完成后只定位一次，之后由玩家拖动位置负责。</summary>
    private void PositionBagAfterFirstOpen()
    {
        if (defaultBagPlacementPending && basePanel != null)
            basePanel.StartCoroutine(PositionFirstBagLayout(basePanel));
    }

    private IEnumerator PositionFirstBagLayout(BasePanel panel)
    {
        int dragRevision = panel.Dragger != null ? panel.Dragger.PositionRevision : 0;
        yield return null;
        while (panel != null && panel.IsOpen() && panel.IsVisualTransitioning)
        {
            if (panel.Dragger != null && panel.Dragger.IsDragging)
            {
                defaultBagPlacementPending = false;
                yield break;
            }
            yield return null;
        }
        if (panel == null || !panel.IsOpen() || !defaultBagPlacementPending)
            yield break;
        defaultBagPlacementPending = false;
        if (panel.Dragger != null && (panel.Dragger.IsDragging || panel.Dragger.PositionRevision != dragRevision))
            yield break;
        RectTransform rect = panel.Dragger != null ? panel.Dragger.rectTransform : panel.rectTransform;
        InventoryPanelLayout.ApplyDefaultBagPosition(rect,
            GetPlayerHotBarInventory()?.basePanel?.transform as RectTransform);
    }

    private static IEnumerator DelayedBringToFront(RectTransform rectTransform)
    {
        yield return null;
        BasePanel.BringToFront(rectTransform);
    }

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


        // 玩家库存跨场景保留，其他实体的库存面板跟随当前场景销毁。
        UIManager uiManager = UIManager.Instance;
        BasePanel createdPanel = item is Player
            ? uiManager.CreatePersistentPanelFromGameObject(panelPrefab)
            : uiManager.CreatePanelFromGameObject(panelPrefab);
        basePanel = createdPanel.GetComponentInChildren<BasePanel>();
        basePanel.Opened += PositionBagAfterFirstOpen;
        bool usesTogglePanelLifecycle = UsesTogglePanelLifecycle();
        // 可开关窗口统一配置，快捷栏和手部只保留常驻 HUD 行为。
        if (usesTogglePanelLifecycle)
            basePanel.PrepareForGameplayWindow();
        else
        {
            basePanel.SetGameplayInputBlocking(false);
            basePanel.SetHeldItemWorldDropAllowed(false);
        }
        // 可开关库存 Prefab 可能以可见状态保存；先统一为关闭态，确保随后 Open 走正常面板生命周期。
        // 快捷栏与手部库存保留 Prefab 的显示状态，不参与这次归一化。
        if (usesTogglePanelLifecycle)
        {
            basePanel.Close();
        }

        // 如果此 inventory 中保存了面板位置，则尝试在创建时恢复位置
        if (Data != null)
        {
            bool hasSavedPanelPosition = false;
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
                    hasSavedPanelPosition = true;
                }

                if (!hasSavedPanelPosition &&
                    string.Equals(panelPrefab.name, "UI_Bag", StringComparison.Ordinal))
                {
                    defaultBagPlacementPending = true;
                }
            }
        }

        // 容器标题读取物品名称，玩家行囊保留自身标题。
        if (item != null && item is not Player &&
            basePanel.TryGetText("FWUI_标题", out TextMeshProUGUI titleText))
            LocalizedTextRuntime.SetProvider(titleText, ResolveContainerPanelTitle);

        // 调用UI初始化方法（此时basePanel已存在）
        InitUI();

        return true; // 成功创建了面板
    }

    /// <summary>通过物品定义读取容器标题，语言切换时重新获取名称。</summary>
    private string ResolveContainerPanelTitle()
    {
        ItemData ownerData = item != null ? item.itemData : null;
        if (ownerData == null)
            return string.Empty;

        RuntimeItemDefinition definition = null;
        GameRes.ExistingInstance?.TryGetItemDefinition(ownerData.IDName, out definition);
        return definition?.DisplayName ??
            (!string.IsNullOrWhiteSpace(ownerData.GameName) ? ownerData.GameName : ownerData.IDName);
    }

    // 辅助方法：检查 Vector2 是否有效
    private bool IsValidVector2(Vector2 vector)
    {
        return !float.IsNaN(vector.x) && !float.IsNaN(vector.y) &&
               !float.IsInfinity(vector.x) && !float.IsInfinity(vector.y);
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
        UI_Content content = basePanel.GetComponentInChildren<UI_Content>(true);
        if (content == null)
            throw new InvalidOperationException($"库存面板 {basePanel.name} 缺少 UI_Content 槽位容器。");
        ItemSlot_Parent = content.transform;

        // 加载Slot UI预制体
        ItemSlot_Prefab = GameRes.Instance.GetPrefab("UI_Slot");

        // 只有创造背包复用虚拟化网格，生存背包使用固定槽位对象。
        InventorySlotVisualProfile visualProfile = basePanel.GetComponentInChildren<InventorySlotVisualProfile>(true);
        _virtualizedSlotGrid = basePanel.GetComponent<InventoryVirtualizedSlotGrid>();
        if (_virtualizedSlotGrid != null && Data.HasUnlimitedSlots)
        {
            _virtualizedSlotGrid.enabled = true;
            _virtualizedSlotGrid.Bind(this, ItemSlot_Parent, ItemSlot_Prefab, visualProfile);
            CompleteUIInitialization(syncData: false);
            return;
        }

        if (_virtualizedSlotGrid != null)
        {
            _virtualizedSlotGrid.Unbind();
            _virtualizedSlotGrid.enabled = false;
            _virtualizedSlotGrid = null;
        }

        // 只管理实际槽位，名称提示等附属 UI 不参与数量同步。
        int targetCount = Data.itemSlots.Count;
        int currentCount = 0;
        itemSlot_UI.Clear();

        // 运行时不直接销毁多余槽位，避免在触发器/动画等回调链里触发 DestroyImmediate 报错。
        for (int i = 0; i < ItemSlot_Parent.childCount; i++)
        {
            ItemSlot_UI slot = ItemSlot_Parent.GetChild(i).GetComponent<ItemSlot_UI>();
            if (slot == null)
                continue;

            bool required = currentCount++ < targetCount;
            slot.gameObject.SetActive(required);
            if (required)
                itemSlot_UI.Add(slot);
        }

        // 创建缺少的槽位
        for (int i = currentCount; i < targetCount; i++)
        {
            GameObject item = GameObject.Instantiate(ItemSlot_Prefab, ItemSlot_Parent, false);
            item.SetActive(true);
            itemSlot_UI.Add(item.GetComponent<ItemSlot_UI>());
        }

        // 面板可声明自己的槽位视觉主题；仅覆盖表现，不改变通用 UI_Slot 或库存交互逻辑。
        if (visualProfile != null)
        {
            foreach (ItemSlot_UI slot in itemSlot_UI)
                visualProfile.Apply(slot);
        }

        CompleteUIInitialization(syncData: true);
    }

    /// <summary>统一完成库存面板的容量、搜索、排序和手柄导航绑定。</summary>
    private void CompleteUIInitialization(bool syncData)
    {
        RegisterFlickTarget(basePanel, !IsHandInventory());
        BindCarryCapacityUI();
        if (syncData)
            SyncData();

        RefreshUI();
        InventorySortButton.EnsureFor(this);
        basePanel.GetComponent<InventoryBagSearch>()?.Bind(this);

        // 槽位是运行时动态创建的，必须在创建完成后重新收集组件并补齐导航图。
        Canvas.ForceUpdateCanvases();
        basePanel.RefreshUIComponents();
        if (UsesTogglePanelLifecycle())
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
                }, // ClearSlotDataAction
                this
            );

            BindSlotInteractionEvents(itemSlotUI);

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
            {
                itemSlot_UI[i].ClearInventoryBinding(this);
                itemSlot_UI[i].gameObject.SetActive(false);
            }
        }
    }

    public void BindSlotUI(ItemSlot_UI slotUI, int bindIndex = -1, bool registerPrimary = true)
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

        if (registerPrimary) RegisterSlotUI(slotUI, fixedIndex);

        slotUI.InitializeSlot(
            fixedIndex,
            index => Data.GetItemSlot(index < 0 ? slotUI.slotIndex : index),
            index =>
            {
                int targetIndex = index < 0 ? slotUI.slotIndex : index;
                ItemSlot targetSlot = Data.GetItemSlot(targetIndex);
                // 虚拟化槽位的旧回调不得误删第0格，也不能对无效槽执行扣除。
                if (targetSlot == null) return;
                Data.RemoveItemAll(targetSlot, targetIndex);
            }, this);

        BindSlotInteractionEvents(slotUI);

        slotUI.RefreshUI();
    }

    // 固定槽和虚拟槽复用同一份输入绑定，解绑与重绑保持一致。
    private void BindSlotInteractionEvents(ItemSlot_UI slotUI)
    {
        slotUI.OnLeftClick.Clear();
        slotUI.OnGamepadSubmit.Clear();
        slotUI._OnScroll.Clear();
        slotUI._OnShiftScroll.Clear();
        slotUI.OnShiftQuickTransfer.Clear();
        slotUI.OnFlickTransfer = null;
        slotUI.HasFlickTarget = null;
        slotUI.OnMouseDragBegin = null;
        slotUI.OnMouseDragDrop = null;
        slotUI.OnTouchTap = null;
        slotUI.OnTouchLongPress = null;
        slotUI.CanTouchLongPressPutDown = null;
        slotUI.CanTouchInspectItem = null;
        slotUI.OnTouchLongPressProgress = null;
        slotUI.OnTouchWorldLongPress = null;
        slotUI.OnTouchHalfDragBegin = null;
        slotUI.OnDesktopTap = null;

        slotUI.OnLeftClick += OnLeftClick;
        slotUI.OnGamepadSubmit += OnGamepadSubmit;
        slotUI._OnScroll += OnScroll;
        slotUI._OnShiftScroll += OnShiftScroll;
        slotUI.OnShiftQuickTransfer += OnShiftQuickTransfer;
        slotUI.OnFlickTransfer = OnFlickTransfer;
        slotUI.HasFlickTarget = HasVisibleFlickTarget;
        slotUI.OnMouseDragBegin = OnMouseDragBegin;
        slotUI.OnMouseDragDrop = OnMouseDragDrop;
        slotUI.OnTouchTap = OnTouchTap;
        slotUI.OnTouchLongPress = OnTouchLongPress;
        slotUI.CanTouchLongPressPutDown = CanTouchLongPressPutDown;
        slotUI.CanTouchInspectItem = CanTouchInspectItem;
        slotUI.OnTouchLongPressProgress = ForwardTouchLongPressProgressToHand;
        slotUI.OnTouchWorldLongPress = OnTouchWorldLongPress;
        slotUI.OnTouchHalfDragBegin = OnTouchHalfDragBegin;
        slotUI.OnDesktopTap = OnDesktopTap;
    }

    /// <summary>同一库存的虚拟槽只切换索引，复用首次绑定的交互回调。</summary>
    public void RebindVirtualSlotUI(ItemSlot_UI slotUI, int bindIndex)
    {
        if (slotUI == null || Data?.GetItemSlot(bindIndex) == null)
            return;

        RegisterSlotUI(slotUI, bindIndex);
        slotUI.InitializeSlot(bindIndex, slotUI.GetSlotDataFunc, slotUI.ClearSlotDataAction, this);
        slotUI.RefreshUI();
    }

    /// <summary>虚拟化 UI 预分配索引映射，但不创建与数据槽位等量的 GameObject。</summary>
    public void PrepareVirtualizedSlotUIMap(int slotCount)
    {
        itemSlot_UI.Clear();
        for (int i = 0; i < Mathf.Max(0, slotCount); i++)
            itemSlot_UI.Add(null);

        if (Data?.itemSlots == null)
            return;

        // 数据槽刷新监听与 UI 实例数量解耦，未渲染槽位也保持原有刷新语义。
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            ItemSlot slot = Data.itemSlots[i];
            if (slot == null)
                continue;

            slot.Index = i;
            slot.onSlotDataChanged.Clear();
            slot.onSlotDataChanged += OnItemSlotChanged;
        }
    }

    /// <summary>复用槽位离开原数据索引时解除映射，避免旧索引刷新到错误的 UI。</summary>
    public void UnbindVirtualSlotUI(ItemSlot_UI slotUI, int bindIndex)
    {
        if (slotUI == null || bindIndex < 0 || bindIndex >= itemSlot_UI.Count)
            return;

        if (ReferenceEquals(itemSlot_UI[bindIndex], slotUI))
        {
            itemSlot_UI[bindIndex] = null;
            slotUI.ClearInventoryBinding(this);
        }
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
    protected virtual void OnItemSlotChanged(ItemSlot slot)
    {
        // 防守性编程：检查slot和Data是否为空
        if (slot == null || Data == null || Data.itemSlots == null)
        {
            Debug.LogWarning($"[Inventory.OnItemSlotChanged] slot、Data 或 Data.itemSlots 为空！");
            return;
        }

        // 动态大背包优先直接使用稳定索引，避免每次槽位变化都线性扫描整个库存。
        int directIndex = slot.Index;
        if (directIndex >= 0 && directIndex < Data.itemSlots.Count &&
            ReferenceEquals(Data.itemSlots[directIndex], slot))
        {
            RefreshUI(directIndex);
            return;
        }

        // 兼容外部槽位索引尚未同步的情况。
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            if (Data.itemSlots[i] != null && Data.itemSlots[i] == slot)
            {
                RefreshUI(i);
                break;
            }
        }
    }

    //TODO 基于新输入系统实现按下B键打开和关闭背包UI




    public void RefreshUI(int index)
    {
        if (_virtualizedSlotGrid != null)
        {
            _virtualizedSlotGrid.RefreshSlot(index);
            RefreshCarryCapacityUI();
            return;
        }

        if (itemSlot_UI == null || index < 0 || index >= itemSlot_UI.Count)
            return;

        // 数据事件可能晚于面板销毁到达，Unity 已销毁的组件必须跳过。
        ItemSlot_UI slotUI = itemSlot_UI[index];
        if (slotUI == null)
            return;

        slotUI.RefreshUI();
        RefreshCarryCapacityUI();
    }

    public void RefreshUI()
    {
        if (SyncDynamicSlotUI())
        {
            RefreshCarryCapacityUI();
            return;
        }

        if (itemSlot_UI == null)
        {
            RefreshCarryCapacityUI();
            return;
        }

        if (_virtualizedSlotGrid != null)
        {
            _virtualizedSlotGrid.RefreshVisibleSlots();
            RefreshCarryCapacityUI();
            return;
        }

        for (int i = 0; i < itemSlot_UI.Count; i++)
        {
            ItemSlot_UI slotUI = itemSlot_UI[i];
            if (slotUI != null)
                slotUI.RefreshUI();
        }

        RefreshCarryCapacityUI();
    }

    /// <summary>绑定库存面板中的重量/体积数值和进度条，并监听其它随身库存变化。</summary>
    private void BindCarryCapacityUI()
    {
        _carryWeightValueText = null;
        _carryVolumeValueText = null;
        _carryWeightProgress = null;
        _carryVolumeProgress = null;
        if ((!IsPlayerBagInventory() && Data?.HasCarryCapacity != true) || basePanel == null)
            return;

        basePanel.TryGetText("FWUI_CarryWeightValue", out _carryWeightValueText);
        basePanel.TryGetText("FWUI_CarryVolumeValue", out _carryVolumeValueText);
        if (_carryWeightValueText != null)
            _carryWeightProgress = basePanel.GetSlider("FWUI_CarryWeightProgress");
        if (_carryVolumeValueText != null)
            _carryVolumeProgress = basePanel.GetSlider("FWUI_CarryVolumeProgress");
        BindCarryCapacityLinkedInventory();
        RefreshCarryCapacityUI();
    }

    /// <summary>快捷栏和手工制作库存变化时，也要刷新行囊的携带量显示。</summary>
    private void BindCarryCapacityLinkedInventory()
    {
        UnbindCarryCapacityLinkedInventory();
        if (!IsPlayerBagInventory() || item is not Player player)
            return;

        _linkedCarryUsageData ??= new List<Inventory_Data>();
        PlayerCarryCapacityUtility.CollectCarriedInventories(player, _linkedCarryUsageData);
        _linkedCarryUsageData.Remove(Data);
        foreach (Inventory_Data data in _linkedCarryUsageData)
        {
            data.Event_OnDataChanged += HandleLinkedCarryDataChanged;
            data.Event_RefreshUI += HandleLinkedCarryUIRefresh;
        }
    }

    private void UnbindCarryCapacityLinkedInventory()
    {
        if (_linkedCarryUsageData == null)
            return;

        foreach (Inventory_Data data in _linkedCarryUsageData)
        {
            data.Event_OnDataChanged -= HandleLinkedCarryDataChanged;
            data.Event_RefreshUI -= HandleLinkedCarryUIRefresh;
        }
        _linkedCarryUsageData.Clear();
    }

    private void HandleLinkedCarryDataChanged(ItemSlot _)
    {
        RefreshCarryCapacityUI();
    }

    private void HandleLinkedCarryUIRefresh(int _)
    {
        RefreshCarryCapacityUI();
    }

    /// <summary>刷新“当前 / 上限”的重量与体积；行囊显示全部随身库存，容器显示自身库存。</summary>
    private void RefreshCarryCapacityUI()
    {
        if (_carryWeightValueText == null && _carryVolumeValueText == null)
        {
            return;
        }

        float currentWeight;
        float currentVolume;
        float weightLimit;
        float volumeLimit;
        string maxWeight;
        string maxVolume;

        if (IsPlayerBagInventory() && item is Player player &&
            PlayerCarryCapacityUtility.TryGetSnapshot(player, out PlayerCarryCapacitySnapshot snapshot))
        {
            currentWeight = snapshot.CurrentWeight;
            currentVolume = snapshot.CurrentVolume;
            weightLimit = snapshot.IsUnlimited ? float.PositiveInfinity : snapshot.MaxWeight;
            volumeLimit = snapshot.IsUnlimited ? float.PositiveInfinity : snapshot.MaxVolume;
            maxWeight = snapshot.IsUnlimited ? "INF" : snapshot.MaxWeight.ToString("0.##");
            maxVolume = snapshot.IsUnlimited
                ? "INF"
                : snapshot.MaxVolume.ToString("0.##");
        }
        else if (Data?.HasCarryCapacity == true)
        {
            Data.GetCarryUsage(out currentWeight, out currentVolume);
            weightLimit = Data.HasUnlimitedCarryCapacity ? float.PositiveInfinity : Data.MaxCarryWeight;
            volumeLimit = Data.HasUnlimitedCarryCapacity ? float.PositiveInfinity : Data.MaxCarryVolume;
            maxWeight = Data.MaxCarryWeight.ToString("0.##");
            maxVolume = Data.MaxCarryVolume.ToString("0.##");
        }
        else
        {
            return;
        }

        if (_carryWeightValueText != null)
            _carryWeightValueText.text = $"{currentWeight:0.##} / {maxWeight} kg";
        if (_carryVolumeValueText != null)
            _carryVolumeValueText.text = $"{currentVolume:0.##} / {maxVolume} L";

        // 进度条复用同一份权威容量读数，超量只封顶填充，数值继续保留真实携带量。
        if (_carryWeightProgress != null)
            _carryWeightProgress.SetValueWithoutNotify(weightLimit > 0f ? Mathf.Clamp01(currentWeight / weightLimit) : 0f);
        if (_carryVolumeProgress != null)
            _carryVolumeProgress.SetValueWithoutNotify(volumeLimit > 0f ? Mathf.Clamp01(currentVolume / volumeLimit) : 0f);
    }

    /// <summary>动态槽位增减后只同步表现层，不重置库存数据与业务监听。</summary>
    private bool SyncDynamicSlotUI()
    {
        if (basePanel == null || Data == null || !Data.HasUnlimitedSlots ||
            itemSlot_UI.Count == Data.itemSlots.Count)
            return false;

        InitUI();
        return true;
    }

    /// <summary>
    /// 将外部装备提供的槽位临时挂入当前库存。槽位对象本身由装备持有，
    /// 因此保存时可以把内容写回装备，而不会复制出第二份物品数据。
    /// </summary>
    public void AppendExternalSlots(IList<ItemSlot> externalSlots)
    {
        if (Data?.itemSlots == null || !Data.HasUnlimitedSlots || externalSlots == null || externalSlots.Count == 0)
            return;

        for (int i = 0; i < externalSlots.Count; i++)
            Data.itemSlots.Add(externalSlots[i] ?? new ItemSlot());

        InitData();
        if (basePanel != null)
            InitUI();
    }

    /// <summary>按槽位对象引用移除外部槽位，避免多个装备扩展槽位时误删普通背包格。</summary>
    public bool RemoveExternalSlots(IList<ItemSlot> externalSlots)
    {
        if (Data?.itemSlots == null || externalSlots == null || externalSlots.Count == 0)
            return false;

        int startIndex = -1;
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            if (ReferenceEquals(Data.itemSlots[i], externalSlots[0]))
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0 || startIndex + externalSlots.Count > Data.itemSlots.Count)
            return false;

        for (int i = 0; i < externalSlots.Count; i++)
        {
            if (!ReferenceEquals(Data.itemSlots[startIndex + i], externalSlots[i]))
                return false;
        }

        Data.itemSlots.RemoveRange(startIndex, externalSlots.Count);
        InitData();
        if (basePanel != null)
            InitUI();
        return true;
    }

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

        // 显式扩容同样遵守容量策略，固定库存不能通过此入口增加格子。
        if (!Data.HasUnlimitedSlots)
            return;

        // 显式批量扩容只负责追加槽位，不能调用 InitData：
        // 玩家主背包的动态容量自检会立刻把这些尚未填充的空槽收缩掉，
        // 同时 InitData 还会重建库存事件。调用方会在同一事务内继续填充这些槽位。
        for (int i = 0; i < extraSlotCount; i++)
        {
            Data.itemSlots.Add(new ItemSlot(Data.itemSlots.Count)
            {
                SlotMaxVolume = Inventory_Data.DefaultSlotVolume
            });
        }

        // 仅同步表现层；库存业务事件保持原有绑定。
        if (basePanel != null)
        {
            InitUI();
        }
    }
    #endregion
}
