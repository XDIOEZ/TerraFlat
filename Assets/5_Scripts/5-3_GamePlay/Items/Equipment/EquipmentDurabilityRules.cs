using System;
using UnityEngine;

/// <summary>装备材质决定耐久，攻击结算只消费真实造成伤害的一点磨损。</summary>
public static class EquipmentDurabilityRules
{
    #region 材质耐久与攻击磨损
    public const string AttackEquipmentTag = "DurableAttackEquipment";
    public const string EquipmentTag = "DurableEquipment";

    public static void ApplyDefaults(ItemDefinitionDto definition, ItemData template)
    {
        bool attack = false, equipment = false;
        if (definition.Modules != null) foreach (var module in definition.Modules.Values)
        {
            if (module == null || module.Enabled == false) continue;
            string prefab = module.Prefab ?? "";
            if (prefab.Contains("AnimationAction", StringComparison.OrdinalIgnoreCase) ||
                prefab.Contains("_Bow", StringComparison.OrdinalIgnoreCase) ||
                prefab.Contains("ResourceTool", StringComparison.OrdinalIgnoreCase) ||
                prefab.Contains("Shovel", StringComparison.OrdinalIgnoreCase)) attack = true;
            if (prefab.Contains("EquipmentStore", StringComparison.OrdinalIgnoreCase)) equipment = true;
        }
        equipment |= template.Tags?.Contains("Equipment") == true || template.Tags?.Contains("Tool") == true;
        if (!attack && !equipment) return;
        string materials = definition.Id + " " + string.Join(" ", template.Tags ?? new());
        float maximum = ResolveMaterialMaximum(materials, template.MaxDurability);
        template.MaxDurability = template.Durability = maximum;
        template.Tags ??= new();
        if (!template.Tags.Contains(EquipmentTag)) template.Tags.Add(EquipmentTag);
        if (attack && !template.Tags.Contains(AttackEquipmentTag)) template.Tags.Add(AttackEquipmentTag);
    }

    // 以有效材质标签或稳定物品ID选档，黑曜石优先于一般矿石档。
    public static float ResolveMaterialMaximum(string materialIdentity, float fallback = 150f)
    {
        string value = materialIdentity ?? "";
        bool Has(string token) => value.Contains(token, StringComparison.OrdinalIgnoreCase);
        if (Has("Obsidian")) return 50f;
        if (Has("Steel")) return 2500f;
        if (Has("Iron")) return 1800f;
        if (Has("Bronze")) return 1000f;
        if (Has("Copper")) return 700f;
        if (Has("Gold")) return 500f;
        if (Has("Diamond")) return 3000f;
        if (Has("Flint")) return 200f;
        if (Has("Stone")) return 300f;
        if (Has("Bone")) return 150f;
        if (Has("Wood")) return 30f;
        if (Has("Cloth") || Has("Leather") || Has("Straw")) return 80f;
        return Mathf.Max(150f, fallback);
    }

    public static bool IsBroken(ItemData data) => data?.Tags?.Contains(AttackEquipmentTag) == true && data.Durability <= 0f;

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
