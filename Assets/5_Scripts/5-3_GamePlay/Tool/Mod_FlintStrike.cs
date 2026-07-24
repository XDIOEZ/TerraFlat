using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class Mod_FlintStrike : Module, IInteractable
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.1f;

#region 基础参数

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
    public Image ProgressImage;
    public ItemSlot_UI InputSlotUI;
    public ItemSlot_UI OutputSlotUI;

    [Header("材料")]
    public string FireSeedItemID = "FireSeed"; // 产出火种物品ID
    public List<string> TinderItemIds = new List<string> { "Leaf", "FireTinder" }; // 允许作为火绒的物品ID
    public List<string> TinderTags = new List<string> { "火绒", "树叶" }; // 允许作为火绒的标签
    public int RequiredClickCount = 10; // 视觉进度需要的点击次数
    public float ClickIncrement = 1f; // 每次点击增加的进度值
    public float SuccessChancePerClick = 0.18f; // 每次点击的点火概率
    public float DecayRatePerSecond = 0.5f; // 每秒自动衰减的进度值
    public float SuccessFillDuration = 0.35f; // 成功时进度条补满动画时长

    float _progress; // 当前进度值（0 ~ RequiredClickCount）
    bool _isActBound; // 是否已绑定右键触发
    bool _isResolvingSuccess; // 是否正在播放成功动画
    Tween _progressTween; // 进度条动画
    ItemSlot _pendingTinderSlot; // 待消耗的火绒槽位
    int _pendingTinderIndex = -1; // 待消耗的火绒索引

#endregion

#region 生命周期

    private void OnValidate()
    {
        if (_Data != null)
        {
            _Data.ID = "打火石模块";
        }
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
        if (_isResolvingSuccess)
            return;

        _progress = Mathf.Max(0f, _progress - (DecayRatePerSecond * deltaTime));
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
    }

    public void OnInteractCancel(Item playerItem)
    {
        if (basePanel == null)
            return;

        InputInventory.DefaultTarget_Inventory = null;
        OutputInventory.DefaultTarget_Inventory = null;
        CancelSuccessAnimation();
        basePanel.Close();
    }

    public void OpenUI()
    {
        if (UI_Prefab == null)
        {
            UI_Prefab = GameRes.Instance.GetPrefab("UI_FlintStrike") ?? GameRes.Instance.GetPrefab("UI_FireDrill");
        }

        if (UI_Prefab == null)
        {
            throw new InvalidOperationException("[Mod_FlintStrike] 未配置 UI_Prefab，且无法通过名称 UI_FlintStrike 或 UI_FireDrill 自动获取。请在组件中指定 UI。");
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

    private void DestroyUI()
    {
        CancelSuccessAnimation();

        if (basePanel == null)
            return;

        UIManager.Instance.DestroyPanel(basePanel);
        basePanel = null;
        InputSlotUI = null;
        OutputSlotUI = null;
        StrikeButton = null;
        CloseButton = null;
        ProgressImage = null;
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
            throw new InvalidOperationException("[Mod_FlintStrike] 缺少输入/输出槽位，请检查 UI_FlintStrike 或 UI_FireDrill 中输入_1 和 输出_1 的命名。");
        }

        InputInventory.itemSlot_UI.Clear();
        OutputInventory.itemSlot_UI.Clear();
        InputInventory.BindSlotUI(InputSlotUI, 0);
        OutputInventory.BindSlotUI(OutputSlotUI, 0);

        StrikeButton = basePanel.GetButton("合成按钮");
        if (StrikeButton == null)
        {
            throw new InvalidOperationException("[Mod_FlintStrike] UI 缺少名为'合成按钮'的按钮。");
        }
        StrikeButton.onClick.RemoveListener(OnStrikeButtonClick);
        StrikeButton.onClick.AddListener(OnStrikeButtonClick);

        var strikeText = StrikeButton.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (strikeText != null)
        {
            strikeText.text = "打火";
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
            throw new InvalidOperationException("[Mod_FlintStrike] UI 缺少名为 Progress 的进度条 Image。");
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
        CancelSuccessAnimation();
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
        if (_isResolvingSuccess)
            return;

        if (!TryGetTinderSlot(out ItemSlot tinderSlot, out int tinderIndex))
        {
            Debug.LogWarning("[Mod_FlintStrike] 打火失败：输入槽中没有可用火绒。请先通过游戏合成系统获得打火石本体，再在本模块放入火绒。");
            return;
        }

        _progress = Mathf.Min(_progress + ClickIncrement, RequiredClickCount);
        UpdateProgressUI();

        if (UnityEngine.Random.value > SuccessChancePerClick)
            return;

        BeginSuccessSequence(tinderSlot, tinderIndex);
    }

    private void BeginSuccessSequence(ItemSlot tinderSlot, int tinderIndex)
    {
        if (_isResolvingSuccess)
            return;

        if (OutputInventory == null || OutputInventory.Data == null)
            throw new InvalidOperationException("[Mod_FlintStrike] 输出容器为空，无法生成火种。");

        if (!HasOutputSpaceForFireSeed())
        {
            Debug.LogWarning("[Mod_FlintStrike] 输出槽空间不足，无法完成打火。");
            return;
        }

        _isResolvingSuccess = true;
        _pendingTinderSlot = tinderSlot;
        _pendingTinderIndex = tinderIndex;

        StrikeButton.interactable = false;
        CancelProgressTween();

        if (ProgressImage == null || SuccessFillDuration <= 0f)
        {
            CompleteSuccessSequence();
            return;
        }

        float targetFill = 1f;
        _progressTween = DOTween.To(
            () => ProgressImage.fillAmount,
            value => ProgressImage.fillAmount = value,
            targetFill,
            SuccessFillDuration)
            .SetEase(Ease.OutCubic)
            .OnComplete(CompleteSuccessSequence);
    }

    private void CompleteSuccessSequence()
    {
        if (_pendingTinderSlot == null)
        {
            Debug.LogError("[Mod_FlintStrike] 成功结算时，火绒槽位已失效。");
            ResetAfterFailure();
            return;
        }

        ItemData fireSeedData = CreateFireSeedData();
        if (!OutputInventory.Data.TryAddItem(fireSeedData, false))
        {
            Debug.LogWarning("[Mod_FlintStrike] 输出槽在结算前被占满，已取消本次打火。");
            ResetAfterFailure();
            return;
        }

        ConsumeOneMaterial(_pendingTinderSlot, _pendingTinderIndex);
        OutputInventory.Data.TryAddItem(fireSeedData, true);
        InputInventory.RefreshUI();
        OutputInventory.RefreshUI();

        Debug.Log($"[Mod_FlintStrike] 打火成功：已生成火种 {FireSeedItemID}，进度={_progress:F1}/{RequiredClickCount}");
        ResetAfterSuccess();
    }

    private void ResetAfterSuccess()
    {
        _isResolvingSuccess = false;
        _pendingTinderSlot = null;
        _pendingTinderIndex = -1;
        _progress = 0f;
        UpdateProgressUI();
        if (StrikeButton != null)
        {
            StrikeButton.interactable = true;
        }
    }

    private void ResetAfterFailure()
    {
        _isResolvingSuccess = false;
        _pendingTinderSlot = null;
        _pendingTinderIndex = -1;
        _progress = 0f;
        UpdateProgressUI();
        if (StrikeButton != null)
        {
            StrikeButton.interactable = true;
        }
    }

    private void CancelSuccessAnimation()
    {
        CancelProgressTween();
        _isResolvingSuccess = false;
        if (StrikeButton != null)
        {
            StrikeButton.interactable = true;
        }
    }

    private void CancelProgressTween()
    {
        if (_progressTween == null)
            return;

        _progressTween.Kill();
        _progressTween = null;
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
            if (current?.itemData == null)
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

    private bool HasOutputSpaceForFireSeed()
    {
        ItemData fireSeedData = CreateFireSeedData();
        return OutputInventory.Data.TryAddItem(fireSeedData, false);
    }

    private ItemData CreateFireSeedData()
    {
        var prefab = GameRes.Instance.GetPrefab(FireSeedItemID);
        if (prefab == null)
        {
            throw new InvalidOperationException($"[Mod_FlintStrike] 找不到火种预制体: {FireSeedItemID}");
        }

        var item = prefab.GetComponent<Item>();
        if (item == null)
        {
            throw new InvalidOperationException($"[Mod_FlintStrike] 预制体 {FireSeedItemID} 缺少 Item 组件。");
        }

        ItemData fireSeedData = item.Get_NewItemData();
        fireSeedData.Stack.Amount = 1;
        fireSeedData.Tags ??= new List<string>();
        if (!fireSeedData.Tags.Contains("火种"))
        {
            fireSeedData.Tags.Add("火种");
        }

        return fireSeedData;
    }

    private void ConsumeOneMaterial(ItemSlot slot, int index)
    {
        if (slot == null || slot.itemData == null)
            throw new InvalidOperationException("[Mod_FlintStrike] 尝试消耗材料时，目标槽位为空。");

        slot.itemData.Stack.Amount -= 1;
        if (slot.itemData.Stack.Amount <= 0)
        {
            InputInventory.Data.RemoveItemAll(slot, index);
        }
    }

    private void UpdateProgressUI()
    {
        if (ProgressImage == null || _isResolvingSuccess)
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
            Debug.LogWarning("[Mod_FlintStrike] 右键触发失败：item.Owner 为空，无法定位玩家手部背包。");
            return;
        }

        OnInteractStart(item.Owner);
    }

#endregion
}
