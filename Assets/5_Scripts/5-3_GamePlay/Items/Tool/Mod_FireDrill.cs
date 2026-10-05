using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.UI;

public class Mod_FireDrill : Module, IInteractable
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.1f;

#region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    [SerializeReference]
    public List<string> RawData = new List<string>();

    [Header("容器")]
    public Inventory InputInventory = new Inventory(); // 火绒输入容器
    public Inventory OutputInventory = new Inventory(); // 火种输出容器

    [Header("UI")]
    public BasePanel basePanel;
    public GameObject UI_Prefab;
    public Button FrictionButton;
    public Button CloseButton;
    public ItemSlot_UI InputSlotUI;
    public ItemSlot_UI OutputSlotUI;

    [Header("钻木参数")]
    [Range(100f, 200f)] public float HeatSourceTemperature = 150f; // 摩擦热源的最高温度。
    [Min(0.01f)] public float TemperaturePerClick = 10f; // 每次摩擦提供一份材料的升温量。

    public MaterialHeatingProcessor Heater { get; private set; }
    bool _isActBound;
    CraftingOutputPreview _outputPreview;
    TMPro.TMP_Text _temperatureLabel;
    Player _currentInteractingPlayer;

#endregion

#region 生命周期

    private void OnValidate()
    {
        ModSaveData ??= new Ex_ModData_MemoryPackable();
        ModSaveData.ID = "钻木取火模块";
    }

    public override void Load()
    {
        EnsureRuntimeDefaults();
        if (ModSaveData.BitData?.Length > 0)
        {
            FireDrillRuntimeState saved = ModSaveData.GetData<FireDrillRuntimeState>();
            if (saved?.Input == null || saved.Output == null) throw new InvalidOperationException("取火器库存快照无效。");
            InputInventory.Data = saved.Input;
            OutputInventory.Data = saved.Output;
        }
        MachineInventory.Rebase(InputInventory.Data);
        MachineInventory.Rebase(OutputInventory.Data);
        InputInventory.InitData();
        OutputInventory.InitData();
        Heater = new MaterialHeatingProcessor(InputInventory, OutputInventory);
        SyncHeatingSettings();
        BindItemActEvent();
        BindInteractEvents();
    }

    public override void Save()
    {
        ModSaveData.WriteData(new FireDrillRuntimeState
        {
            Input = InputInventory.Data, Output = OutputInventory.Data
        });
    }

    public override void Unload()
    {
        UnbindItemActEvent();
        UnbindInteractEvents();
        if (InputInventory?.Data != null)
            InputInventory.Data.Event_OnDataChanged -= OnInputSlotChanged;
        DestroyUI();
        Heater = null;
    }

    private void OnDestroy()
    {
        Unload();
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || Heater == null) return;
        SyncHeatingSettings();
        Heater.Tick(GetAmbientTemperature(), deltaTime);
        UpdateOutputPreviewProgress();
    }

#endregion

#region 交互与UI

    public void OnInteractStart(Item playerItem)
    {
        _currentInteractingPlayer = ResolvePlayer(playerItem);
        if (basePanel == null)
        {
            OpenUI();
        }

        EnsureUIBindingsOnOpen();

        BuildingPanelActions buildingActions = basePanel.GetComponent<BuildingPanelActions>();
        if (buildingActions == null)
        {
            throw new InvalidOperationException("[Mod_FireDrill] 钻木取火面板缺少 BuildingPanelActions，正式 Prefab 未完成建筑操作绑定。");
        }
        buildingActions.Bind(item);

        var handInventory = playerItem.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (handInventory == null)
        {
            throw new InvalidOperationException("[Mod_FireDrill] 玩家手部容器为空，无法打开钻木取火面板。");
        }

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
            _currentInteractingPlayer = null;
        }
    }

    public void OnInteractCancel(Item playerItem)
    {
        if (basePanel == null)
            return;

        ClosePanelAndClearTransferContext();
    }

    private void DestroyUI()
    {
        if (basePanel == null)
            return;

        ClosePanelAndClearTransferContext();
        // 模块销毁时只清理已经存在的 UI 管理器，禁止触发惰性单例重建 PanelRoot。
        UIManager.ExistingInstance?.DestroyPanel(basePanel);
        basePanel = null;
        InputSlotUI = null;
        OutputSlotUI = null;
        FrictionButton = null;
        CloseButton = null;
        _outputPreview = null;
        _temperatureLabel = null;
    }

    public void OpenUI()
    {
        if (UI_Prefab == null)
        {
            UI_Prefab = GameRes.Instance.GetPrefab("UI_FireDrill");
        }

        if (UI_Prefab == null)
        {
            throw new InvalidOperationException("[Mod_FireDrill] 未配置 UI_Prefab，且无法通过名称 UI_FireDrill 自动获取。\n请在组件中指定 UI。 ");
        }

        basePanel = UIManager.Instance.CreatePanelFromGameObject(UI_Prefab);

        // 专用制作面板也必须进入统一左右布局，避免置顶背包覆盖移动端输入槽。
        RectTransform panelRect = basePanel.Dragger != null
            ? basePanel.Dragger.rectTransform
            : basePanel.rectTransform;
        InventoryPanelLayout.ApplyDefaultCraftingPosition(panelRect);

        EnsureUIBindingsOnOpen();

        InputInventory.SyncData();
        OutputInventory.SyncData();

        basePanel.Close();
        InputInventory.RefreshUI();
        OutputInventory.RefreshUI();
        RefreshOutputPreview();
    }

    private void EnsureUIBindingsOnOpen()
    {
        if (basePanel == null)
        {
            throw new InvalidOperationException("[Mod_FireDrill] basePanel 为空，无法绑定 UI 元素。");
        }

        // 绑定槽位（支持输入_1/输入 1 两种命名）
        InputSlotUI = FindSlotUI("输入_1", "输入 1");
        OutputSlotUI = FindSlotUI("输出_1", "输出 1");
        if (InputSlotUI == null || OutputSlotUI == null)
        {
            throw new InvalidOperationException("[Mod_FireDrill] 缺少输入/输出槽位，请检查 UI_FireDrill 中输入_1 和 输出_1 命名。");
        }

        InputInventory.itemSlot_UI.Clear();
        OutputInventory.itemSlot_UI.Clear();
        InputInventory.BindSlotUI(InputSlotUI, 0);
        OutputInventory.BindSlotUI(OutputSlotUI, 0);
        BindOutputPreview();
        _temperatureLabel = basePanel.GetText("FWUI_FooterHint");

        FrictionButton = basePanel.GetButton("合成按钮");
        if (FrictionButton == null)
        {
            throw new InvalidOperationException("[Mod_FireDrill] UI 缺少名为'合成按钮'的按钮。");
        }
        FrictionButton.onClick.RemoveListener(OnFrictionButtonClick);
        FrictionButton.onClick.AddListener(OnFrictionButtonClick);

        var frictionText = FrictionButton.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (frictionText != null)
        {
            frictionText.text = "摩擦";
        }

        CloseButton = basePanel.GetButton("关闭");
        if (CloseButton != null)
        {
            CloseButton.onClick.RemoveListener(OnCloseButtonClick);
            CloseButton.onClick.AddListener(OnCloseButtonClick);
        }

    }

    private ItemSlot_UI FindSlotUI(params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            var button = basePanel.GetButton(names[i]);
            if (button == null)
                continue;

            var slot = button.GetComponent<ItemSlot_UI>();
            if (slot != null)
                return slot;
        }

        return null;
    }

    private void OnCloseButtonClick()
    {
        ClosePanelAndClearTransferContext();
    }

    private void ClosePanelAndClearTransferContext()
    {
        _currentInteractingPlayer = null;
        InputInventory.DefaultTarget_Inventory = null;
        OutputInventory.DefaultTarget_Inventory = null;

        if (basePanel != null)
            basePanel.Close();

        InputInventory.SyncQuickTransferTarget(basePanel);
    }

    private void EnsureRuntimeDefaults()
    {
        ModSaveData ??= new Ex_ModData_MemoryPackable { ID = FireDrillLogic.ModuleId };
        if (InputInventory == null)
        {
            InputInventory = new Inventory();
        }

        if (OutputInventory == null)
        {
            OutputInventory = new Inventory();
        }

        InputInventory.item = item;
        OutputInventory.item = item;

        if (InputInventory.Data == null)
        {
            InputInventory.Data = new Inventory_Data(new List<ItemSlot> { new ItemSlot(0) }, "输入");
        }
        if (OutputInventory.Data == null)
        {
            OutputInventory.Data = new Inventory_Data(new List<ItemSlot> { new ItemSlot(0) }, "输出");
        }

        if (InputInventory.Data.itemSlots == null || InputInventory.Data.itemSlots.Count == 0)
        {
            InputInventory.Data.itemSlots = new List<ItemSlot> { new ItemSlot(0) };
        }
        if (OutputInventory.Data.itemSlots == null || OutputInventory.Data.itemSlots.Count == 0)
        {
            OutputInventory.Data.itemSlots = new List<ItemSlot> { new ItemSlot(0) };
        }
    }

#endregion

#region 钻木流程

    private void OnFrictionButtonClick()
    {
        if (!GameNetwork.HasStateAuthority || Heater == null) return;
        SyncHeatingSettings();
        bool transformed = Heater.Rub(GetAmbientTemperature(), _currentInteractingPlayer);
        RefreshOutputPreview();
        Save();
        if (transformed) _outputPreview?.PlaySuccess();
    }

    private void SyncHeatingSettings()
    {
        Heater.SourceTemperature = HeatSourceTemperature;
        Heater.TemperaturePerClick = TemperaturePerClick;
    }

    private float GetAmbientTemperature()
    {
        Vector3 position = item.Owner != null ? item.Owner.transform.position : item.transform.position;
        TemperatureMgr.Instance.TryGetAmbientTemperature(position, out float temperature);
        return temperature;
    }

    private static Player ResolvePlayer(Item actorItem)
    {
        return actorItem as Player ?? actorItem?.GetComponentInParent<Player>();
    }

    private void BindOutputPreview()
    {
        _outputPreview = CraftingOutputPreview.Attach(basePanel, OutputSlotUI);
        InputInventory.Data.Event_OnDataChanged -= OnInputSlotChanged;
        InputInventory.Data.Event_OnDataChanged += OnInputSlotChanged;
        RefreshOutputPreview();
    }

    private void OnInputSlotChanged(ItemSlot _)
    {
        RefreshOutputPreview();
    }

    private void RefreshOutputPreview()
    {
        if (_outputPreview == null || Heater == null)
            return;

        if (Heater.PreviewOutput() is ItemData preview)
            _outputPreview.Show(preview, Heater.Progress01);
        else
            _outputPreview.Clear();
        UpdateOutputPreviewProgress();
    }

    private void UpdateOutputPreviewProgress()
    {
        if (Heater == null) return;
        _outputPreview?.SetProgress(Heater.Progress01);
        if (_temperatureLabel != null) _temperatureLabel.text = Heater.Status;
        if (FrictionButton != null) FrictionButton.interactable = Heater.CanRub;
    }

#endregion

#region 交互事件绑定

    private void BindInteractEvents()
    {
        if (item.itemMods.GetMod_ByID(ModText.Interact, out Mod_InteractReciver interactMod))
        {
            interactMod.OnAction_Start += OnInteractStart;
            interactMod.OnAction_Stop += OnInteractCancel;
        }
    }

    private void UnbindInteractEvents()
    {
        if (item == null)
            return;

        if (item.itemMods.GetMod_ByID(ModText.Interact, out Mod_InteractReciver interactMod))
        {
            interactMod.OnAction_Start -= OnInteractStart;
            interactMod.OnAction_Stop -= OnInteractCancel;
        }
    }

    private void BindItemActEvent()
    {
        if (_isActBound)
            return;

        item.OnAct += OnItemAct;
        _isActBound = true;
    }

    private void UnbindItemActEvent()
    {
        if (!_isActBound || item == null)
            return;

        item.OnAct -= OnItemAct;
        _isActBound = false;
    }

    private void OnItemAct()
    {
        if (item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.TryHandlePlacementAction() == true)
            return;

        if (item.Owner == null)
        {
            Debug.LogWarning("[Mod_FireDrill] 右键触发失败：item.Owner 为空，无法定位玩家手部背包。");
            return;
        }

        OnInteractStart(item.Owner);
    }

#endregion
}
