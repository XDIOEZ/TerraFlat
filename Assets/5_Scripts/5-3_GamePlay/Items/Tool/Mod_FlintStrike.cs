using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using UnityEngine;
using UnityEngine.UI;

public class Mod_FlintStrike : Module, IInteractable
{
#region 基础参数

    public const float DefaultSuccessChancePerClick = 0.1f;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    [SerializeReference]
    public List<string> RawData = new List<string>();

    [Header("容器")]
    public Inventory InputInventory = new Inventory(); // 输入容器：火绒
    public Inventory OutputInventory = new Inventory(); // 输出容器：火种

    [Header("UI")]
    public BasePanel basePanel;
    public GameObject UI_Prefab;
    public Button StrikeButton;
    public Button CloseButton;
    public ItemSlot_UI InputSlotUI;
    public ItemSlot_UI OutputSlotUI;

    [Header("材料")]
    public string FireSeedItemID = "FireSeed"; // 产出火种物品ID
    public List<string> TinderItemIds = new List<string> { "Leaf", "FireTinder" }; // 允许作为火绒的物品ID
    public List<string> TinderTags = new List<string> { "火绒", "树叶" }; // 允许作为火绒的标签
    [Range(0f, 1f)] public float SuccessChancePerClick = DefaultSuccessChancePerClick; // 每次点击独立判定，不累计保底进度。

    bool _isActBound; // 是否已绑定右键触发
    bool _isCommitting;
    CraftingOutputPreview _outputPreview;

#endregion

#region 生命周期

    private void OnValidate()
    {
        ModSaveData ??= new Ex_ModData_MemoryPackable();
        ModSaveData.ID = "打火石模块";
    }

    protected override void OnLoad()
    {
        ModSaveData.ReadData(ref RawData);
        EnsureRuntimeDefaults();
        InputInventory.InitData();
        OutputInventory.InitData();
        BindItemActEvent();
        BindInteractEvents();
    }

    protected override void OnSave()
    {
        ModSaveData.WriteData(RawData);
    }

    protected override void OnUnload()
    {
        UnbindItemActEvent();
        UnbindInteractEvents();
        if (InputInventory?.Data != null)
            InputInventory.Data.Event_OnDataChanged -= OnInventoryChanged;
        if (OutputInventory?.Data != null)
            OutputInventory.Data.Event_OnDataChanged -= OnInventoryChanged;
        DestroyUI();
    }

    private void OnDestroy()
    {
        Unload();
    }

#endregion

#region 交互与UI

    public void OnInteractStart(Item playerItem)
    {
        if (basePanel == null)
        {
            OpenUI();
        }

        EnsureUIBindingsOnOpen();

        var handInventory = playerItem.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (handInventory == null)
        {
            throw new InvalidOperationException("[Mod_FlintStrike] 玩家手部容器为空，无法打开打火石面板。");
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
        }
    }

    public void OnInteractCancel(Item playerItem)
    {
        if (basePanel == null)
            return;

        ClosePanelAndClearTransferContext();
    }

    public void OpenUI()
    {
        if (UI_Prefab == null)
        {
            UI_Prefab = GameRes.Instance.GetPrefab("UI_FireDrill");
        }

        if (UI_Prefab == null)
        {
            throw new InvalidOperationException("[Mod_FlintStrike] 无法获取共用取火面板 UI_FireDrill。");
        }

        basePanel = UIManager.Instance.CreatePanelFromGameObject(UI_Prefab);
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
        StrikeButton = null;
        CloseButton = null;
        _outputPreview = null;
    }

    private void EnsureUIBindingsOnOpen()
    {
        if (basePanel == null)
        {
            throw new InvalidOperationException("[Mod_FlintStrike] basePanel 为空，无法绑定 UI 元素。");
        }

        InputSlotUI = FindSlotUI("输入_1", "输入 1");
        OutputSlotUI = FindSlotUI("输出_1", "输出 1");
        if (InputSlotUI == null || OutputSlotUI == null)
        {
            throw new InvalidOperationException("[Mod_FlintStrike] 共用取火面板缺少输入_1 或 输出_1 槽位。");
        }

        InputInventory.itemSlot_UI.Clear();
        OutputInventory.itemSlot_UI.Clear();
        InputInventory.BindSlotUI(InputSlotUI, 0);
        OutputInventory.BindSlotUI(OutputSlotUI, 0);
        BindOutputPreview();

        StrikeButton = basePanel.GetButton("合成按钮");
        if (StrikeButton == null)
        {
            throw new InvalidOperationException("[Mod_FlintStrike] UI 缺少名为'合成按钮'的按钮。");
        }
        StrikeButton.onClick.RemoveListener(OnStrikeButtonClick);
        StrikeButton.onClick.AddListener(OnStrikeButtonClick);

        BindText(StrikeButton.GetComponentInChildren<TMPro.TMP_Text>(true),
            () => FlatWorldLocalizationService.GetUiText("打火"));
        BindText(basePanel.GetText("FWUI_标题"),
            () => FlatWorldLocalizationService.GetUiText("燧石取火"));
        BindText(basePanel.GetText("FWUI_眉题"), () => "FIRECRAFT / SPARK");
        BindText(basePanel.GetText("FWUI_FooterHint"), () => FlatWorldLocalizationService.GetUiFormat(
            "每次点击有 {0:0}% 概率产生火种", Mathf.Clamp01(SuccessChancePerClick) * 100f));
        // 共用建筑面板时按真实物品能力隐藏放置、拆除操作。
        basePanel.GetComponent<BuildingPanelActions>()?.Bind(item);

        CloseButton = basePanel.GetButton("关闭");
        if (CloseButton != null)
        {
            CloseButton.onClick.RemoveListener(OnCloseButtonClick);
            CloseButton.onClick.AddListener(OnCloseButtonClick);
        }

        RefreshOutputPreview();
    }

    private static void BindText(TMPro.TMP_Text text, Func<string> provider)
    {
        if (text == null) return;
        var binder = text.GetComponent<LocalizedTextBinder>() ?? text.gameObject.AddComponent<LocalizedTextBinder>();
        binder.ConfigureRuntime(provider);
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
        InputInventory.DefaultTarget_Inventory = null;
        OutputInventory.DefaultTarget_Inventory = null;

        if (basePanel != null)
            basePanel.Close();

        InputInventory.SyncQuickTransferTarget(basePanel);
    }

    private void EnsureRuntimeDefaults()
    {
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

        if (InputInventory.Data.itemSlots == null || InputInventory.Data.itemSlots.Count != 1)
        {
            InputInventory.Data.itemSlots = new List<ItemSlot> { new ItemSlot(0) };
        }
        if (OutputInventory.Data.itemSlots == null || OutputInventory.Data.itemSlots.Count == 0)
        {
            OutputInventory.Data.itemSlots = new List<ItemSlot> { new ItemSlot(0) };
        }
    }

#endregion

#region 打火流程

    private void OnStrikeButtonClick()
    {
        if (_isCommitting ||
            MachineInventory.IsBeingDragged(InputInventory) || MachineInventory.IsBeingDragged(OutputInventory))
            return;

        if (!TryGetTinderSlot(out _, out int tinderIndex))
        {
            Debug.LogWarning("[Mod_FlintStrike] 打火失败：输入槽中没有可用火绒。请先通过游戏合成系统获得打火石本体，再在本模块放入火绒。");
            return;
        }

        if (!IsSuccessfulRoll(UnityEngine.Random.value, SuccessChancePerClick))
            return;

        ItemData fireSeedData = CreateFireSeedData();
        var match = new CraftingRecipeMatch(new RuntimeRecipe { Id = "flint-strike" }, false,
            new[] { new CraftingConsumption(tinderIndex, 1f) });
        _isCommitting = true;
        try
        {
            // 成功后用正式制作事务一次提交扣料与产物，满输出时整笔拒绝。
            if (!CraftingTransaction.TryCreate(InputInventory, OutputInventory, match, new[] { fireSeedData }, false,
                    out CraftingTransaction transaction, out _) || !transaction.Commit(out _))
                return;
            transaction.Complete();
            InputInventory.RefreshUI();
            OutputInventory.RefreshUI();
            _outputPreview?.PlaySuccess();
        }
        finally
        {
            _isCommitting = false;
            RefreshOutputPreview();
        }
    }

    internal static bool IsSuccessfulRoll(float roll, float successChance)
    {
        return successChance >= 1f || roll < Mathf.Clamp01(successChance);
    }

    private bool TryGetTinderSlot(out ItemSlot tinderSlot, out int tinderIndex)
    {
        tinderSlot = null;
        tinderIndex = -1;

        if (InputInventory == null || InputInventory.Data == null || InputInventory.Data.itemSlots == null)
            return false;

        for (int i = 0; i < InputInventory.Data.itemSlots.Count; i++)
        {
            var current = InputInventory.Data.itemSlots[i];
            if (current?.itemData?.Stack == null || current.itemData.Stack.Amount < 1f)
                continue;

            if (!IsValidTinder(current.itemData))
                continue;

            tinderSlot = current;
            tinderIndex = i;
            return true;
        }

        return false;
    }

    private bool IsValidTinder(ItemData itemData)
    {
        return IsValidMaterial(itemData, TinderItemIds, TinderTags);
    }

    private bool IsValidMaterial(ItemData itemData, List<string> itemIds, List<string> tags)
    {
        if (itemData == null)
            return false;

        if (itemIds != null && itemIds.Contains(itemData.IDName))
            return true;

        if (itemData.Tags == null || tags == null)
            return false;

        return itemData.Tags.ContainsAnyTag(tags);
    }

    private ItemData CreateFireSeedData()
    {
        var prefab = GameRes.Instance.GetPrefab(FireSeedItemID);
        if (prefab == null)
        {
            throw new InvalidOperationException($"[Mod_FlintStrike] 找不到火种预制体: {FireSeedItemID}");
        }

        ItemData fireSeedData = GameRes.Instance.CreateItemData(FireSeedItemID);
        if (fireSeedData == null)
            throw new InvalidOperationException($"[Mod_FlintStrike] 无法创建物品数据: {FireSeedItemID}");
        fireSeedData.Stack.Amount = 1;
        fireSeedData.Tags ??= new List<string>();
        if (!fireSeedData.Tags.Contains(Tag.CombustionTinder))
        {
            fireSeedData.Tags.Add(Tag.CombustionTinder);
        }

        return fireSeedData;
    }

    private void BindOutputPreview()
    {
        _outputPreview = CraftingOutputPreview.Attach(basePanel, OutputSlotUI);
        InputInventory.Data.Event_OnDataChanged -= OnInventoryChanged;
        InputInventory.Data.Event_OnDataChanged += OnInventoryChanged;
        OutputInventory.Data.Event_OnDataChanged -= OnInventoryChanged;
        OutputInventory.Data.Event_OnDataChanged += OnInventoryChanged;
        RefreshOutputPreview();
    }

    private void OnInventoryChanged(ItemSlot _)
    {
        if (_isCommitting)
            return;

        RefreshOutputPreview();
    }

    private void RefreshOutputPreview()
    {
        if (_outputPreview == null)
            return;

        if (TryGetTinderSlot(out _, out _) &&
            CreateFireSeedData() is ItemData fireSeedData &&
            OutputInventory.Data.TryAddItem(fireSeedData, false))
            _outputPreview.Show(fireSeedData);
        else
            _outputPreview.Clear();
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
        if (item.Owner == null)
        {
            Debug.LogWarning("[Mod_FlintStrike] 右键触发失败：item.Owner 为空，无法定位玩家手部背包。");
            return;
        }

        OnInteractStart(item.Owner);
    }

#endregion
}
