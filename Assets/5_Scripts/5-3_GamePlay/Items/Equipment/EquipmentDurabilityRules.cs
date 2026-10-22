using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>装备材质决定耐久，攻击结算只消费真实造成伤害的一点磨损。</summary>
public static class EquipmentDurabilityRules
{
    #region 材质耐久与攻击磨损
    public const string AttackEquipmentTag = "DurableAttackEquipment";
    public const string EquipmentTag = "DurableEquipment";
    private static MaterialCatalog materialCatalog;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear() => materialCatalog = null;

    internal static void ConfigureResourceReload(ResourceReloadContext context)
        => context.Add(() => materialCatalog, value => materialCatalog = value, null);

    private sealed class MaterialCatalog
    {
        public List<MaterialRule> Materials;
    }

    private sealed class MaterialRule
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
                prefab.Contains("_Bow", StringComparison.OrdinalIgnoreCase)) weapon = true;
            if (weapon ||
                prefab.Contains("ResourceTool", StringComparison.OrdinalIgnoreCase) ||
                prefab.Contains("Shovel", StringComparison.OrdinalIgnoreCase)) attack = true;
            if (prefab.Contains("EquipmentStore", StringComparison.OrdinalIgnoreCase)) equipment = true;
        }
        equipment |= template.Tags?.Contains("Equipment") == true || template.Tags?.Contains("Tool") == true;
        if (!attack && !equipment) return;
        string materials = definition.Id + " " + string.Join(" ", template.Tags ?? new());
        float maximum = ResolveMaterialMaximum(materials, template.MaxDurability, weapon);
        template.MaxDurability = template.Durability = maximum;
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
            MaterialCatalog catalog = JsonConvert.DeserializeObject<MaterialCatalog>(asset.text);
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

    public static bool IsBroken(ItemData data) => data?.Tags?.Contains(AttackEquipmentTag) == true && data.Durability <= 0f;

    public static bool RejectBrokenAttack(Item source)
    {
        if (!IsBroken(source?.itemData)) return false;
        if (source.Owner is Player { IsLocalProfile: true })
            CombatFeedbackEvents.PublishEquipmentBroken(source.Owner.transform.position);
        return true;
    }

    public static float ResolveRemaining(float durability, float actualDamage) => actualDamage > 0f
        ? Mathf.Max(0f, durability - 1f) : Mathf.Max(0f, durability);

    public static void ConsumeSuccessfulDamage(Item source, ItemData launchedWeaponData, float actualDamage, Item inventoryOwner = null)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || !(actualDamage > 0f)) return;
        ItemData data = launchedWeaponData ?? source?.itemData;
        if (data?.Tags?.Contains(AttackEquipmentTag) != true || data.Durability <= 0f) return;
        if (source != null && ReferenceEquals(source.itemData, data)) source.DecreaseDurability(1);
        else data.Durability = ResolveRemaining(data.Durability, actualDamage);
        Item owner = inventoryOwner != null ? inventoryOwner : source?.Owner;
        Mod_HotBar hotbar = owner?.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        if (hotbar?.RuntimeInventory?.Data?.NotifyItemStateChanged(data) != true && owner != null &&
            InventoryContextResolver.TryResolveContainingInventory(owner, data, out Inventory inventory))
            inventory.Data.NotifyItemStateChanged(data);
        hotbar?.NotifyOwnerNetworkStateChanged();
    }
    #endregion
}
