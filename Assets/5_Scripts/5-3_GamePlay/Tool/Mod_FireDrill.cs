using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Mod_FireDrill : Module, IInteractable
{
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
    public Image ProgressImage;
    public ItemSlot_UI InputSlotUI;
    public ItemSlot_UI OutputSlotUI;

    [Header("钻木参数")]
    public string FireSeedItemID = "FireSeed"; // 产出火种物品ID
    public List<string> TinderItemIds = new List<string> { "Leaf", "FireTinder" }; // 允许作为火绒的物品ID
    public List<string> TinderTags = new List<string> { "火绒", "树叶" }; // 允许作为火绒的标签
    public int RequiredClickCount = 14; // 完全填满进度条所需点击次数
    public float ClickIncrement = 1f; // 每次点击增加的进度值
    public float FirstClickBonus = 5f; // 第一次点击的额外反馈
    public float DecayRatePerSecond = 1f; // 每秒自动衰减的进度值（阻力）

    float _progress; // 当前进度值（0 ~ RequiredClickCount）
    bool _hasClickedThisSession; // 本次打开是否已点击过
    bool _isActBound;

#endregion

#region 生命周期

    private void OnValidate()
    {
        _Data.ID = "钻木取火模块";
    }

    public override void Load()
    {
        ModSaveData.ReadData(ref RawData);
        EnsureRuntimeDefaults();
        InputInventory.InitData();
        OutputInventory.InitData();
        BindItemActEvent();
        BindInteractEvents();
    }

    public override void Save()
    {
        UnbindItemActEvent();
        UnbindInteractEvents();
        DestroyUI();
        ModSaveData.WriteData(RawData);
    }

    public override void ModUpdate(float deltaTime)
    {
        // 每帧持续衰减进度
        _progress = Mathf.Max(0, _progress - (DecayRatePerSecond * deltaTime));
        UpdateProgressUI();
    }

#endregion

#region 交互与UI

    public void OnInteractStart(Item playerItem)
    {
        if (basePanel == null)
        {
            OpenUI();
        }

        // 重置本次会话的点击状态
        _hasClickedThisSession = false;

        EnsureUIBindingsOnOpen();

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
    }

    public void OnInteractCancel(Item playerItem)
    {
        if (basePanel == null)
            return;

        InputInventory.DefaultTarget_Inventory = null;
        OutputInventory.DefaultTarget_Inventory = null;
        basePanel.Close();
    }

    private void DestroyUI()
    {
        if (basePanel == null)
            return;

        UIManager.Instance.DestroyPanel(basePanel);
        basePanel = null;
        InputSlotUI = null;
        OutputSlotUI = null;
        FrictionButton = null;
        CloseButton = null;
        ProgressImage = null;
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

        EnsureUIBindingsOnOpen();

        InputInventory.SyncData();
        OutputInventory.SyncData();

        basePanel.Close();
        UpdateProgressUI();
        InputInventory.RefreshUI();
        OutputInventory.RefreshUI();
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

        ProgressImage = basePanel.GetImage("Progress");
        if (ProgressImage == null)
        {
            throw new InvalidOperationException("[Mod_FireDrill] UI 缺少名为 Progress 的进度条 Image。");
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
        InputInventory.DefaultTarget_Inventory = null;
        OutputInventory.DefaultTarget_Inventory = null;
        basePanel.Close();
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
        // 检测火绒是否存在
        if (!TryGetTinderSlot(out ItemSlot tinderSlot, out int tinderIndex))
        {
            Debug.LogWarning("[Mod_FireDrill] 摩擦失败：输入槽中没有可用火绒。需要放入树叶或带火绒标签的物品。");
            return;
        }

        // 计算本次增加的进度值
        float increment = ClickIncrement;
        if (!_hasClickedThisSession)
        {
            increment = FirstClickBonus;
            _hasClickedThisSession = true;
        }

        // 增加进度
        _progress = Mathf.Min(_progress + increment, RequiredClickCount);
        UpdateProgressUI();

        // 检查是否完成
        if (_progress >= RequiredClickCount)
        {
            if (TryConvertToFireSeed(tinderSlot, tinderIndex))
            {
                Debug.Log($"[Mod_FireDrill] 钻木成功：已将火绒转化为火种，进度={_progress:F1}/{RequiredClickCount}");
            }
            ResetProgress();
        }
    }

    private bool TryGetTinderSlot(out ItemSlot slot, out int index)
    {
        slot = null;
        index = -1;

        if (InputInventory == null || InputInventory.Data == null || InputInventory.Data.itemSlots == null)
            return false;

        for (int i = 0; i < InputInventory.Data.itemSlots.Count; i++)
        {
            var current = InputInventory.Data.itemSlots[i];
            if (current?.itemData == null)
                continue;

            if (!IsValidTinder(current.itemData))
                continue;

            slot = current;
            index = i;
            return true;
        }

        return false;
    }

    private bool IsValidTinder(ItemData itemData)
    {
        if (itemData == null)
            return false;

        if (TinderItemIds != null && TinderItemIds.Contains(itemData.IDName))
            return true;

        if (itemData.Tags == null || TinderTags == null)
            return false;

        return itemData.Tags.ContainsAnyTag(TinderTags);
    }

    private bool TryConvertToFireSeed(ItemSlot tinderSlot, int tinderIndex)
    {
        if (OutputInventory == null || OutputInventory.Data == null)
            throw new InvalidOperationException("[Mod_FireDrill] 输出容器为空，无法生成火种。");

        var prefab = GameRes.Instance.GetPrefab(FireSeedItemID);
        if (prefab == null)
        {
            Debug.LogError($"[Mod_FireDrill] 找不到火种预制体: {FireSeedItemID}");
            return false;
        }

        var item = prefab.GetComponent<Item>();
        if (item == null)
            throw new InvalidOperationException($"[Mod_FireDrill] 预制体 {FireSeedItemID} 缺少 Item 组件。");

        ItemData fireSeedData = item.Get_NewItemData();
        fireSeedData.Stack.Amount = 1;
        fireSeedData.Tags ??= new List<string>();
        if (!fireSeedData.Tags.Contains("火种"))
        {
            fireSeedData.Tags.Add("火种");
        }

        if (!OutputInventory.Data.TryAddItem(fireSeedData, false))
        {
            Debug.LogWarning("[Mod_FireDrill] 输出槽空间不足，无法放入火种。");
            return false;
        }

        tinderSlot.itemData.Stack.Amount -= 1;
        if (tinderSlot.itemData.Stack.Amount <= 0)
        {
            InputInventory.Data.RemoveItemAll(tinderSlot, tinderIndex);
        }

        OutputInventory.Data.TryAddItem(fireSeedData, true);
        InputInventory.RefreshUI();
        OutputInventory.RefreshUI();
        return true;
    }

    private void ResetProgress()
    {
        _progress = 0f;
        UpdateProgressUI();
    }

    private void UpdateProgressUI()
    {
        if (ProgressImage == null)
            return;

        if (RequiredClickCount <= 0)
        {
            ProgressImage.fillAmount = 0f;
            return;
        }

        ProgressImage.fillAmount = Mathf.Clamp01(_progress / (float)RequiredClickCount);
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
            Debug.LogWarning("[Mod_FireDrill] 右键触发失败：item.Owner 为空，无法定位玩家手部背包。");
            return;
        }

        OnInteractStart(item.Owner);
    }

#endregion
}