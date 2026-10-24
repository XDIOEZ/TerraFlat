using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

/// <summary>手动加工把工具库存和材料加工状态分别保存，工具不会参加材料配方匹配。</summary>
[Serializable, MemoryPackable]
public partial class ManualProcessingState
{
    #region 手动加工状态
    public RecipeProcessingState Processing = new();
    public Inventory_Data Tool = MachineInventory.NewData("工具", 1);
    #endregion
}

/// <summary>按通用工具能力和材质标签限制加工工具，每次有效工作复用正式耐久事务。</summary>
public sealed class ManualProcessingToolRequirement
{
    #region 工具资格与磨损
    public ResourceToolKind Kind { get; }
    public string RequiredTag { get; }
    public float WearPerClick { get; }
    private readonly Dictionary<RuntimeItemDefinition, ResourceToolKind> kinds = new();

    public ManualProcessingToolRequirement(ResourceToolKind kind, string requiredTag, float wearPerClick)
    {
        if (kind == ResourceToolKind.None || !float.IsFinite(wearPerClick) || wearPerClick <= 0f)
            throw new ArgumentException("手动加工工具类别和每击磨损必须有效。");
        Kind = kind;
        RequiredTag = requiredTag?.Trim() ?? string.Empty;
        WearPerClick = wearPerClick;
    }

    public bool Matches(ItemData data)
    {
        if (data == null || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition)) return false;
        if (!kinds.TryGetValue(definition, out ResourceToolKind actual))
        {
            actual = ResourceToolKind.None;
            foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
            {
                if (!module.Enabled) continue;
                GameObject prefab = GameRes.ExistingInstance.GetPrefab(module.PrefabId, false);
                Mod_ResourceToolBase tool = prefab != null ? prefab.GetComponentInChildren<Mod_ResourceToolBase>(true) : null;
                if (tool == null) continue;
                actual = new MachineModuleConfiguration(module, tool).Value("toolKind", tool.HarvestKind);
                break;
            }
            kinds.Add(definition, actual);
        }
        return Matches(data, actual);
    }

    public bool Matches(ItemData data, ResourceToolKind actualKind) => data != null && actualKind == Kind &&
        (RequiredTag.Length == 0 || data?.Tags?.Contains(RequiredTag) == true);

    public bool CanUse(Inventory inventory)
    {
        ItemData tool = inventory?.Data?.GetItemSlot(0)?.itemData;
        return Matches(tool) && ItemDurabilityRules.CanConsume(tool, WearPerClick);
    }

    public bool TryConsume(Inventory inventory) => CanUse(inventory) &&
        inventory.Data.TryConsumeDurability(inventory.Data.GetItemSlot(0).itemData, WearPerClick, out _);
    #endregion
}
