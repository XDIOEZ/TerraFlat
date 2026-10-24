using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class EquipmentSlotRule
{
    public string Id;
    public string DisplayName;
    public string RequiredTag;
    public string LabelObjectName;
    public Vector2 UiPosition;

    public EquipmentSlotRule(string id, string displayName, string requiredTag, string labelObjectName, Vector2 uiPosition)
    {
        Id = id;
        DisplayName = displayName;
        RequiredTag = requiredTag;
        LabelObjectName = labelObjectName;
        UiPosition = uiPosition;
    }
}

/// <summary>装备槽位使用稳定 ASCII ID/Tag，具体生物只需要复用同一套规则。</summary>
public static class EquipmentSlotCatalog
{
    #region 装备槽定义

    public const string Head = "Head";
    public const string TorsoOuter = "TorsoOuter";
    public const string Hands = "Hands";
    public const string FeetOuter = "FeetOuter";
    public const string TorsoInner = "TorsoInner";
    public const string Legs = "Legs";
    public const string FeetInner = "FeetInner";
    public const string OffHand = "OffHand";
    public const string Eyes = "Eyes";

    public const string HeadTag = "EquipmentSlot.Head";
    public const string TorsoOuterTag = "EquipmentSlot.TorsoOuter";
    public const string HandsTag = "EquipmentSlot.Hands";
    public const string FeetOuterTag = "EquipmentSlot.FeetOuter";
    public const string TorsoInnerTag = "EquipmentSlot.TorsoInner";
    public const string LegsTag = "EquipmentSlot.Legs";
    public const string FeetInnerTag = "EquipmentSlot.FeetInner";
    public const string OffHandTag = "EquipmentSlot.OffHand";
    public const string EyesTag = "EquipmentSlot.Eyes";
    public const string AnyTag = "EquipmentSlot.Any";
    public const string BlocksSlotTagPrefix = "EquipmentBlocksSlot.";

    public static List<EquipmentSlotRule> CreateDefaultRules()
    {
        // 前四个索引保持旧槽语义，避免开发期现有测试装备突然换位。
        return new List<EquipmentSlotRule>
        {
            new(Head, "头部 / 帽·盔", HeadTag, "FWUI_SlotLabel_Head", new Vector2(-225f, 180f)),
            new(TorsoOuter, "外衬 / 护甲", TorsoOuterTag, "FWUI_SlotLabel_Chest", new Vector2(-225f, 55f)),
            new(Hands, "手部", HandsTag, "FWUI_SlotLabel_Hand", new Vector2(225f, 180f)),
            new(FeetOuter, "鞋子", FeetOuterTag, "FWUI_SlotLabel_Feet", new Vector2(225f, -185f)),
            new(TorsoInner, "内衬 / 衣物", TorsoInnerTag, "FWUI_SlotLabel_TorsoInner", new Vector2(-225f, -70f)),
            new(Legs, "腿部 / 裤装", LegsTag, "FWUI_SlotLabel_Legs", new Vector2(225f, 55f)),
            new(FeetInner, "袜子", FeetInnerTag, "FWUI_SlotLabel_FeetInner", new Vector2(225f, -70f)),
            new(OffHand, "副手", OffHandTag, "FWUI_SlotLabel_OffHand", new Vector2(-225f, -185f)),
            new(Eyes, "眼部 / 眼镜", EyesTag, "FWUI_SlotLabel_Eyes", new Vector2(0f, 180f))
        };
    }

    /// <summary>装备通过槽位标签声明互斥关系，不依赖头盔或眼镜的具体类型。</summary>
    public static bool BlocksSlot(ItemData equipment, string slotId)
    {
        return equipment?.Tags != null && !string.IsNullOrWhiteSpace(slotId) &&
               equipment.Tags.ContainsTag(BlocksSlotTagPrefix + slotId);
    }

    #endregion
}

[Serializable]
public class Inventory_Equipment : Inventory
{
    #region 槽位规则

    public List<EquipmentSlotRule> SlotRules = EquipmentSlotCatalog.CreateDefaultRules();

    public override void OnValidate()
    {
        EnsureSlotSchema();
        Data.Name = ModText.Equipment_Module;
    }

    public override void InitData()
    {
        EnsureSlotSchema();
        Data.Name = ModText.Equipment_Module;
        base.InitData();

        for (int i = 0; i < Data.itemSlots.Count; i++)
            Data.itemSlots[i].SlotMaxVolume = 1f;
    }

    public void EnsureSlotSchema()
    {
        SlotRules ??= EquipmentSlotCatalog.CreateDefaultRules();
        if (SlotRules.Count == 0)
            SlotRules = EquipmentSlotCatalog.CreateDefaultRules();

        // 具体生物沿用自己的布局，仅补齐当前装备系统新增的独立槽。
        List<EquipmentSlotRule> defaults = null;
        if (GetSlotIndex(EquipmentSlotCatalog.OffHand) < 0)
        {
            defaults = EquipmentSlotCatalog.CreateDefaultRules();
            SlotRules.Add(defaults.Find(rule => rule.Id == EquipmentSlotCatalog.OffHand));
        }
        if (GetSlotIndex(EquipmentSlotCatalog.Eyes) < 0)
        {
            defaults ??= EquipmentSlotCatalog.CreateDefaultRules();
            SlotRules.Add(defaults.Find(rule => rule.Id == EquipmentSlotCatalog.Eyes));
        }

        Data ??= new Inventory_Data(new List<ItemSlot>(), ModText.Equipment_Module);
        Data.itemSlots ??= new List<ItemSlot>();
        while (Data.itemSlots.Count < SlotRules.Count)
            Data.itemSlots.Add(new ItemSlot(Data.itemSlots.Count) { SlotMaxVolume = 1f });
    }

    public int GetSlotIndex(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId) || SlotRules == null)
            return -1;

        for (int i = 0; i < SlotRules.Count; i++)
        {
            if (string.Equals(SlotRules[i]?.Id, slotId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    public bool CanEquipAt(int index, ItemData itemData)
    {
        if (itemData == null || SlotRules == null || index < 0 || index >= SlotRules.Count)
            return false;

        bool hasEquipmentStore = itemData.ModuleDataDic != null &&
                                 itemData.ModuleDataDic.Values.Any(module =>
                                     module != null && module.ID == ModText.Equipment_Store &&
                                     module is Ex_ModData_MemoryPackable);
        if (!hasEquipmentStore)
            return false;

        EquipmentSlotRule rule = SlotRules[index];
        if (rule == null || !string.IsNullOrWhiteSpace(rule.RequiredTag) &&
            (itemData.Tags == null ||
             !itemData.Tags.ContainsTag(rule.RequiredTag) && !itemData.Tags.ContainsTag(EquipmentSlotCatalog.AnyTag)))
            return false;

        if (Data?.itemSlots == null)
            return true;

        int count = Mathf.Min(SlotRules.Count, Data.itemSlots.Count);
        for (int otherIndex = 0; otherIndex < count; otherIndex++)
        {
            ItemData otherItem = Data.itemSlots[otherIndex]?.itemData;
            // 替换本槽会先卸下原件；同一件装备在槽间移动也不与自己冲突。
            if (otherIndex == index || otherItem == null || ReferenceEquals(otherItem, itemData))
                continue;

            if (EquipmentSlotCatalog.BlocksSlot(otherItem, rule.Id) ||
                EquipmentSlotCatalog.BlocksSlot(itemData, SlotRules[otherIndex]?.Id))
                return false;
        }

        return true;
    }

    /// <summary>按装备标签寻找第一个兼容槽位，供 AI 与脚本直接使用。</summary>
    public int FindCompatibleSlot(ItemData itemData, bool preferEmpty = true)
    {
        if (itemData == null || Data?.itemSlots == null)
            return -1;

        int fallback = -1;
        int count = Mathf.Min(SlotRules.Count, Data.itemSlots.Count);
        for (int i = 0; i < count; i++)
        {
            if (!CanEquipAt(i, itemData))
                continue;

            if (Data.itemSlots[i]?.itemData == null)
                return i;

            if (fallback < 0)
                fallback = i;
        }

        return preferEmpty ? -1 : fallback;
    }

    public override bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot)
    {
        if (!base.CanAcceptQuickTransfer(sourceSlot, targetSlot) || Data?.itemSlots == null)
            return false;

        int index = Data.itemSlots.IndexOf(targetSlot);
        ItemData sourceItem = sourceSlot?.itemData;
        if (!CanEquipAt(index, sourceItem))
            return false;

        int sourceIndex = Data.itemSlots.IndexOf(sourceSlot);
        ItemData displacedItem = targetSlot.itemData;
        if (sourceIndex < 0 || displacedItem == null || displacedItem.CanStackWith(sourceItem))
            return true;
        if (sourceIndex >= SlotRules.Count)
            return false;

        // 装备栏内部交换后两件仍会同时穿戴，额外核对双方最终占用的槽。
        return !EquipmentSlotCatalog.BlocksSlot(sourceItem, SlotRules[sourceIndex]?.Id) &&
               !EquipmentSlotCatalog.BlocksSlot(displacedItem, SlotRules[index]?.Id);
    }

    #endregion

    #region 交互

    public override void OnLeftClick(int index)
    {
        // 过期的装备 UI 点击不得映射到其他槽位或触发库存越界错误。
        if (Data?.itemSlots == null || index < 0 || index >= Data.itemSlots.Count)
            return;

        if (DefaultTarget_Inventory?.Data?.itemSlots == null || DefaultTarget_Inventory.Data.itemSlots.Count == 0)
        {
            Debug.LogWarning($"[{Data.Name}] 没有可用的交互库存，无法操作装备槽 [{index}]");
            return;
        }

        int inputIndex = DefaultTarget_Inventory.Data.itemSlots.Count > index ? index : 0;
        ItemSlot inputSlot = DefaultTarget_Inventory.Data.itemSlots[inputIndex];
        if (inputSlot == null)
            return;

        if (inputSlot.itemData != null && !CanEquipAt(index, inputSlot.itemData))
        {
            string slotName = index >= 0 && index < SlotRules.Count ? SlotRules[index].DisplayName : index.ToString();
            Debug.LogWarning($"[{Data.Name}] {inputSlot.itemData.IDName} 不适用于装备槽 [{slotName}]");
            return;
        }

        Data.ChangeItemData_Default(index, inputSlot);
        DefaultTarget_Inventory.RefreshUI(inputIndex);
        RefreshUI(index);
    }

    #endregion

    #region 面板表现

    public override void InitUI()
    {
        base.InitUI();
        ApplyEquipmentPanelLayout();
        BindEquipmentConfiguration();
        // 装备窗口的所有打开入口统一登记快捷转移，避免直接 Open/Toggle 绕过 SwitchUI。
        basePanel.Opened -= SyncEquipmentQuickTransferTarget;
        basePanel.Closed -= SyncEquipmentQuickTransferTarget;
        basePanel.Opened += SyncEquipmentQuickTransferTarget;
        basePanel.Closed += SyncEquipmentQuickTransferTarget;
        SyncEquipmentQuickTransferTarget();
    }

    private void SyncEquipmentQuickTransferTarget() => SyncQuickTransferTarget(basePanel);

    private void BindEquipmentConfiguration()
    {
        Transform control = FindChildByName(basePanel.transform, "FWUI_ConfigureEquipment");
        Button button = control != null ? control.GetComponent<Button>() : null;
        if (button == null) throw new InvalidOperationException("装备正式面板缺少通用装备配置按钮。");
        var entry = button.GetComponent<EquipmentConfigurationButton>() ??
                    button.gameObject.AddComponent<EquipmentConfigurationButton>();
        entry.Bind(item?.itemMods.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module));
    }

    private void ApplyEquipmentPanelLayout()
    {
        if (basePanel == null)
            return;

        int count = Mathf.Min(itemSlot_UI.Count, SlotRules.Count);
        for (int i = 0; i < count; i++)
        {
            RectTransform rect = itemSlot_UI[i]?.GetComponent<RectTransform>();
            if (rect == null)
                continue;

            // 动态补出的装备槽也统一以容器中心为锚点，避免沿用通用槽位的左下角锚点后整体飞出面板。
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(92f, 92f);
            rect.anchoredPosition = SlotRules[i].UiPosition;
        }

        RefreshOwnerPortrait();
        RefreshSlotLabels();

        foreach (Transform child in basePanel.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("FWUI_Link_", StringComparison.Ordinal))
                child.gameObject.SetActive(false);
        }

        if (basePanel.TryGetText("窗口信息", out TextMeshProUGUI title))
        {
            var binder = title.GetComponent<LocalizedTextBinder>() ?? title.gameObject.AddComponent<LocalizedTextBinder>();
            binder.ConfigureRuntime(GetEquipmentTitle);
        }
    }

    private string GetEquipmentTitle()
    {
        string ownerName = item?.itemData?.GameName;
        if (item?.itemData != null && GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition))
            ownerName = definition.DisplayName;

        return string.IsNullOrWhiteSpace(ownerName)
            ? FlatWorldLocalizationService.GetUiText("装备")
            : FlatWorldLocalizationService.GetUiFormat("{0} · 装备", ownerName);
    }

    private void RefreshOwnerPortrait()
    {
        Transform avatar = FindChildByName(basePanel.transform, "FWUI_Avatar");
        Image image = avatar?.GetComponent<Image>();
        if (image == null || item == null)
            return;

        Sprite sprite = item.Sprite != null ? item.Sprite.sprite : null;
        if (sprite == null)
        {
            SpriteRenderer[] renderers = item.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i]?.sprite == null)
                    continue;
                sprite = renderers[i].sprite;
                break;
            }
        }

        if (sprite == null)
            return;

        image.sprite = sprite;
        image.preserveAspect = true;
        image.type = Image.Type.Simple;
        image.color = new Color(0.06f, 0.07f, 0.08f, 0.9f);
    }

    private void RefreshSlotLabels()
    {
        TextMeshProUGUI template = null;
        TextMeshProUGUI[] texts = basePanel.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i].name.StartsWith("FWUI_SlotLabel_", StringComparison.Ordinal))
            {
                template = texts[i];
                break;
            }
        }
        if (template == null)
            return;

        for (int i = 0; i < SlotRules.Count; i++)
        {
            EquipmentSlotRule rule = SlotRules[i];
            Transform labelTransform = FindChildByName(basePanel.transform, rule.LabelObjectName);
            TextMeshProUGUI label = labelTransform?.GetComponent<TextMeshProUGUI>();
            if (label == null)
            {
                label = UnityEngine.Object.Instantiate(template, template.transform.parent, false);
                label.name = rule.LabelObjectName;
            }

            var binder = label.GetComponent<LocalizedTextBinder>() ?? label.gameObject.AddComponent<LocalizedTextBinder>();
            binder.Configure(FlatWorldLocalizationService.UiTable,
                FlatWorldLocalizationService.GetUiTextKey(rule.DisplayName), rule.DisplayName);
            RectTransform rect = label.rectTransform;
            rect.anchoredPosition = rule.UiPosition + new Vector2(0f, -58f);
            rect.sizeDelta = new Vector2(132f, 24f);
            label.gameObject.SetActive(true);
        }
    }

    private static Transform FindChildByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName))
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (string.Equals(children[i].name, targetName, StringComparison.Ordinal))
                return children[i];
        }
        return null;
    }

    #endregion
}
