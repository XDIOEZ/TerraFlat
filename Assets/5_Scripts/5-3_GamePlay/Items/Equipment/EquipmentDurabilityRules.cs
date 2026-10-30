using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>装备材质决定耐久，攻击命中后消耗一点耐久，不依赖实际伤害数值。</summary>
public static partial class EquipmentDurabilityRules
{
    #region 材质耐久与攻击磨损
    public const string AttackEquipmentTag = "DurableAttackEquipment";
    public const string EquipmentTag = "DurableEquipment";
    private static MaterialCatalog materialCatalog;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear() => materialCatalog = null;

    internal static void ConfigureResourceReload(ResourceReloadContext context)
        => context.Add(() => materialCatalog, value => materialCatalog = value, null);

    [MemoryPack.MemoryPackable]
    private sealed partial class MaterialCatalog
    {
        public List<MaterialRule> Materials;
    }

    [MemoryPack.MemoryPackable]
    private sealed partial class MaterialRule
    {
        public string Identity;
        public float Maximum;
        public bool AttackOnly;
    }

    public static void ApplyDefaults(ItemDefinitionDto definition, ItemData template)
    {
        bool attack = false, weapon = false, equipment = false;
        if (definition.Modules != null) foreach (var module in definition.Modules.Values)
        {
            if (module == null || module.Enabled == false) continue;
            string prefab = module.Prefab ?? "";
            if (prefab.Contains("AnimationAction", StringComparison.OrdinalIgnoreCase) ||
                prefab.Contains("ColdWeapon", StringComparison.OrdinalIgnoreCase) ||
                prefab.Contains("Module_Shield", StringComparison.OrdinalIgnoreCase) ||
                prefab.Contains("_Bow", StringComparison.OrdinalIgnoreCase)) weapon = true;
            if (weapon ||
                prefab.Contains("ResourceTool", StringComparison.OrdinalIgnoreCase) ||
                prefab.Contains("Shovel", StringComparison.OrdinalIgnoreCase)) attack = true;
            if (prefab.Contains("EquipmentStore", StringComparison.OrdinalIgnoreCase)) equipment = true;
        }
        equipment |= template.Tags?.Contains("Equipment") == true || template.Tags?.Contains("Tool") == true;
        if (!attack && !equipment) return;
        string materials = definition.Id + " " + string.Join(" ", template.Tags ?? new());
        // 显式物品数值优先，只有未填写时才按材质补充默认值。
        float maximum = definition.MaxDurability ?? ResolveMaterialMaximum(materials, template.MaxDurability, weapon);
        template.MaxDurability = maximum;
        template.Durability = definition.Durability ?? maximum;
        template.Tags ??= new();
        if (!template.Tags.Contains(EquipmentTag)) template.Tags.Add(EquipmentTag);
        if (attack && !template.Tags.Contains(AttackEquipmentTag)) template.Tags.Add(AttackEquipmentTag);
    }

    // 配置顺序决定材质优先级，限制仅武器的档位不外溢到其他装备。
    public static float ResolveMaterialMaximum(string materialIdentity, float fallback = 150f, bool weapon = true)
    {
        string value = materialIdentity ?? "";
        if (materialCatalog == null)
        {
            TextAsset asset = Resources.Load<TextAsset>("Config/Equipment/durability-materials");
            if (asset == null) throw new InvalidOperationException("缺少装备材质耐久配置");
            MaterialCatalog catalog = JsonConfigurationCache.Deserialize<MaterialCatalog>(asset.text);
            if (catalog?.Materials == null || catalog.Materials.Count == 0)
                throw new InvalidOperationException("装备材质耐久配置为空");
            foreach (MaterialRule rule in catalog.Materials)
                if (rule == null || string.IsNullOrWhiteSpace(rule.Identity) || !float.IsFinite(rule.Maximum) || rule.Maximum <= 0f)
                    throw new InvalidOperationException("装备材质耐久配置包含无效档位");
            materialCatalog = catalog;
        }
        foreach (MaterialRule rule in materialCatalog.Materials)
            if (value.Contains(rule.Identity, StringComparison.OrdinalIgnoreCase))
                return rule.AttackOnly && !weapon ? fallback : rule.Maximum;
        return fallback;
    }

    public static bool IsBroken(ItemData data) => data?.Tags?.Contains(AttackEquipmentTag) == true &&
        (data.Stack?.Amount <= 0f || ItemDurabilityRules.IsEnabled(data) && data.Durability <= 0f);

    public static bool RejectBrokenAttack(Item source)
    {
        if (!IsBroken(source?.itemData)) return false;
        if (source.Owner is Player { IsLocalProfile: true })
            CombatFeedbackEvents.PublishEquipmentBroken(source.Owner.transform.position);
        return true;
    }

    public static float ResolveRemaining(float durability, bool hit) => hit
        ? Mathf.Max(0f, durability - 1f) : Mathf.Max(0f, durability);

    public static void ConsumeSuccessfulHit(Item source, ItemData launchedWeaponData, Item inventoryOwner = null)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority) return;
        ItemData data = launchedWeaponData ?? source?.itemData;
        if (data?.Tags?.Contains(AttackEquipmentTag) != true) return;
        ItemDurabilityService.TryConsume(source, 1f, data, inventoryOwner);
    }
    #endregion
}
