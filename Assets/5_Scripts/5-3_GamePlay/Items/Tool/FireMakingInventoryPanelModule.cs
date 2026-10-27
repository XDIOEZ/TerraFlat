using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>取火工具共用库存面板与交互生命周期，具体取火规则留给子模块。</summary>
public abstract class FireMakingInventoryPanelModule : Module, IInteractable
{
    #region 公共字段

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get => ModSaveData; set => ModSaveData = (Ex_ModData_MemoryPackable)value; }

    [SerializeReference] public List<string> RawData = new List<string>();
    [Header("容器")]
    public Inventory InputInventory = new Inventory();
    public Inventory OutputInventory = new Inventory();
    [Header("UI")]
    public BasePanel basePanel;
    public GameObject UI_Prefab;
    public Button CloseButton;
    public ItemSlot_UI InputSlotUI;
    public ItemSlot_UI OutputSlotUI;

    protected CraftingOutputPreview _outputPreview;
    private bool isActBound;
    protected abstract Button FireMakingActionButton { get; set; }
    protected virtual bool ObserveOutputInventoryForPreview => false;
    protected virtual bool SuppressOutputPreviewRefresh => false;

    #endregion

    #region 库存与生命周期

    protected void EnsureFireMakingInventories(bool requireSingleInputSlot)
    {
        InputInventory ??= new Inventory();
        OutputInventory ??= new Inventory();
        InputInventory.item = item;
        OutputInventory.item = item;
        InputInventory.Data ??= new Inventory_Data(new List<ItemSlot> { new ItemSlot(0) }, "输入");
        OutputInventory.Data ??= new Inventory_Data(new List<ItemSlot> { new ItemSlot(0) }, "输出");
        if (InputInventory.Data.itemSlots == null ||
            (requireSingleInputSlot ? InputInventory.Data.itemSlots.Count != 1 : InputInventory.Data.itemSlots.Count == 0))
            InputInventory.Data.itemSlots = new List<ItemSlot> { new ItemSlot(0) };
        if (OutputInventory.Data.itemSlots == null || OutputInventory.Data.itemSlots.Count == 0)
            OutputInventory.Data.itemSlots = new List<ItemSlot> { new ItemSlot(0) };
    }

    protected void BindFireMakingInteractionEvents()
    {
        if (!isActBound)
        {
            item.OnAct += OnItemAct;
            isActBound = true;
        }
        if (item.itemMods.GetMod_ByID(ModText.Interact, out Mod_InteractReciver interactMod))
        {
            interactMod.OnAction_Start += OnInteractStart;
            interactMod.OnAction_Stop += OnInteractCancel;
        }
    }

    protected override void OnUnload()
    {
        if (isActBound && item != null)
        {
            item.OnAct -= OnItemAct;
            isActBound = false;
        }
        if (item != null && item.itemMods.GetMod_ByID(ModText.Interact, out Mod_InteractReciver interactMod))
        {
            interactMod.OnAction_Start -= OnInteractStart;
            interactMod.OnAction_Stop -= OnInteractCancel;
        }
        if (InputInventory?.Data != null)
            InputInventory.Data.Event_OnDataChanged -= OnFireMakingInventoryChanged;
        if (ObserveOutputInventoryForPreview && OutputInventory?.Data != null)
            OutputInventory.Data.Event_OnDataChanged -= OnFireMakingInventoryChanged;
        DestroyFireMakingPanel();
    }

    protected void OnDestroy() => Unload();

    private void OnItemAct()
    {
        if (TryHandleFireMakingPlacementAction())
            return;
        if (item.Owner == null)
        {
            Debug.LogWarning($"[{GetType().Name}] 右键触发失败：item.Owner 为空，无法定位玩家手部背包。");
            return;
        }
        OnInteractStart(item.Owner);
    }

    #endregion

    #region 面板和交互

    public void OnInteractStart(Item playerItem)
    {
        OnFireMakingInteractionBeginning(playerItem);
        if (basePanel == null) OpenUI();
        EnsureFireMakingPanelBindings();
        BindFireMakingBuildingActionsOnInteraction();
        Inventory handInventory = playerItem.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (handInventory == null)
            throw new InvalidOperationException($"[{GetType().Name}] 玩家手部容器为空，无法打开取火面板。");

        InputInventory.DefaultTarget_Inventory = handInventory;
        OutputInventory.DefaultTarget_Inventory = handInventory;
        InputInventory.RefreshUI();
        OutputInventory.RefreshUI();
        basePanel.Toggle();
        InputInventory.SyncQuickTransferTarget(basePanel);
        if (!basePanel.IsOpen())
        {
            InputInventory.DefaultTarget_Inventory = null;
            OutputInventory.DefaultTarget_Inventory = null;
            OnFireMakingInteractionEnded();
        }
    }

    public void OnInteractCancel(Item playerItem)
    {
        if (basePanel != null) CloseFireMakingPanel();
    }

    public void OpenUI()
    {
        if (UI_Prefab == null) UI_Prefab = GameRes.Instance.GetPrefab("UI_FireDrill");
        if (UI_Prefab == null)
            throw new InvalidOperationException($"[{GetType().Name}] 无法获取共用取火面板 UI_FireDrill。");
        basePanel = UIManager.Instance.CreatePanelFromGameObject(UI_Prefab);
        RectTransform panelRect = basePanel.Dragger != null ? basePanel.Dragger.rectTransform : basePanel.rectTransform;
        InventoryPanelLayout.ApplyDefaultCraftingPosition(panelRect);
        EnsureFireMakingPanelBindings();
        InputInventory.SyncData();
        OutputInventory.SyncData();
        basePanel.Close();
        InputInventory.RefreshUI();
        OutputInventory.RefreshUI();
        RefreshOutputPreview();
    }

    private void EnsureFireMakingPanelBindings()
    {
        if (basePanel == null)
            throw new InvalidOperationException($"[{GetType().Name}] basePanel 为空，无法绑定取火面板。");
        InputSlotUI = FindFireMakingSlot("输入_1", "输入 1");
        OutputSlotUI = FindFireMakingSlot("输出_1", "输出 1");
        if (InputSlotUI == null || OutputSlotUI == null)
            throw new InvalidOperationException($"[{GetType().Name}] 共用取火面板缺少输入或输出槽位。");

        InputInventory.itemSlot_UI.Clear();
        OutputInventory.itemSlot_UI.Clear();
        InputInventory.BindSlotUI(InputSlotUI, 0);
        OutputInventory.BindSlotUI(OutputSlotUI, 0);
        BindFireMakingOutputPreview();
        FireMakingActionButton = basePanel.GetButton("合成按钮");
        if (FireMakingActionButton == null)
            throw new InvalidOperationException($"[{GetType().Name}] UI 缺少名为'合成按钮'的按钮。");
        FireMakingActionButton.onClick.RemoveListener(OnFireMakingActionButtonClick);
        FireMakingActionButton.onClick.AddListener(OnFireMakingActionButtonClick);
        ConfigureFireMakingPanel();
        CloseButton = basePanel.GetButton("关闭");
        if (CloseButton != null)
        {
            CloseButton.onClick.RemoveListener(CloseFireMakingPanel);
            CloseButton.onClick.AddListener(CloseFireMakingPanel);
        }
    }

    private ItemSlot_UI FindFireMakingSlot(params string[] names)
    {
        foreach (string name in names)
        {
            Button button = basePanel.GetButton(name);
            if (button != null && button.TryGetComponent(out ItemSlot_UI slot)) return slot;
        }
        return null;
    }

    private void CloseFireMakingPanel()
    {
        OnFireMakingInteractionEnded();
        InputInventory.DefaultTarget_Inventory = null;
        OutputInventory.DefaultTarget_Inventory = null;
        if (basePanel != null) basePanel.Close();
        InputInventory.SyncQuickTransferTarget(basePanel);
    }

    private void DestroyFireMakingPanel()
    {
        if (basePanel == null) return;
        CloseFireMakingPanel();
        UIManager.ExistingInstance?.DestroyPanel(basePanel);
        basePanel = null;
        InputSlotUI = null;
        OutputSlotUI = null;
        FireMakingActionButton = null;
        CloseButton = null;
        _outputPreview = null;
        OnFireMakingPanelDestroyed();
    }

    #endregion

    #region 输出预览与扩展点

    private void BindFireMakingOutputPreview()
    {
        _outputPreview = CraftingOutputPreview.Attach(basePanel, OutputSlotUI);
        InputInventory.Data.Event_OnDataChanged -= OnFireMakingInventoryChanged;
        InputInventory.Data.Event_OnDataChanged += OnFireMakingInventoryChanged;
        if (ObserveOutputInventoryForPreview)
        {
            OutputInventory.Data.Event_OnDataChanged -= OnFireMakingInventoryChanged;
            OutputInventory.Data.Event_OnDataChanged += OnFireMakingInventoryChanged;
        }
        RefreshOutputPreview();
    }

    private void OnFireMakingInventoryChanged(ItemSlot slot)
    {
        if (!SuppressOutputPreviewRefresh) RefreshOutputPreview();
    }

    protected abstract void OnFireMakingActionButtonClick();
    protected abstract void RefreshOutputPreview();
    protected virtual void ConfigureFireMakingPanel() { }
    protected virtual void OnFireMakingInteractionBeginning(Item playerItem) { }
    protected virtual void OnFireMakingInteractionEnded() { }
    protected virtual void OnFireMakingPanelDestroyed() { }
    protected virtual void BindFireMakingBuildingActionsOnInteraction() { }
    protected virtual bool TryHandleFireMakingPlacementAction() => false;

    #endregion
}
