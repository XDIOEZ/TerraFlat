using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using UnityEngine;
using UnityEngine.UI;

public class Mod_FlintStrike : FireMakingInventoryPanelModule
{
#region 基础参数

    public const float DefaultSuccessChancePerClick = 0.1f;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 1f;

    [Header("UI")]
    public Button StrikeButton;

    [Header("材料")]
    public string FireSeedItemID = "FireSeed"; // 产出火种物品ID
    public List<string> TinderItemIds = new List<string> { "Leaf", "FireTinder" }; // 允许作为火绒的物品ID
    public List<string> TinderTags = new List<string> { "火绒", "树叶" }; // 允许作为火绒的标签
    [Range(0f, 1f)] public float SuccessChancePerClick = DefaultSuccessChancePerClick; // 每次点击独立判定，不累计保底进度。

    bool _isCommitting;

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
        EnsureFireMakingInventories(requireSingleInputSlot: true);
        InputInventory.InitData();
        OutputInventory.InitData();
        BindFireMakingInteractionEvents();
    }

    protected override void OnSave()
    {
        ModSaveData.WriteData(RawData);
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority) return;
        InputInventory?.ModUpdate(deltaTime);
        OutputInventory?.ModUpdate(deltaTime);
    }

#endregion

#region 打火石面板扩展

    protected override Button FireMakingActionButton { get => StrikeButton; set => StrikeButton = value; }
    protected override bool ObserveOutputInventoryForPreview => true;
    protected override bool SuppressOutputPreviewRefresh => _isCommitting;

    protected override void ConfigureFireMakingPanel()
    {
        BindText(StrikeButton.GetComponentInChildren<TMPro.TMP_Text>(true),
            () => FlatWorldLocalizationService.GetUiText("打火"));
        BindText(basePanel.GetText("FWUI_标题"),
            () => FlatWorldLocalizationService.GetUiText("燧石取火"));
        BindText(basePanel.GetText("FWUI_FooterHint"), () => FlatWorldLocalizationService.GetUiFormat(
            "每次点击有 {0:0}% 概率产生火种", Mathf.Clamp01(SuccessChancePerClick) * 100f));
        // 共用建筑面板按真实物品能力显示操作。
        basePanel.GetComponent<BuildingPanelActions>()?.Bind(item);
        RefreshOutputPreview();
    }

    private static void BindText(TMPro.TMP_Text text, Func<string> provider)
    {
        if (text == null) return;
        var binder = text.GetComponent<LocalizedTextBinder>() ?? text.gameObject.AddComponent<LocalizedTextBinder>();
        binder.ConfigureRuntime(provider);
    }

#endregion

#region 打火流程

    protected override void OnFireMakingActionButtonClick()
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

    public bool IsValidTinder(ItemData itemData)
    {
        // 原材料火绒与已经点着的火种分开标记，新增纤维只需声明材料能力。
        return itemData?.Tags?.Contains("Material.Tinder") == true || IsValidMaterial(itemData, TinderItemIds, TinderTags);
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

    protected override void RefreshOutputPreview()
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

}
