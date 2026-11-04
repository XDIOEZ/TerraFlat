using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>
/// 武器攻击声音分类。Auto 会根据物品 ID、名称和标签推断，
/// 也可以在 Mod_Damage 上显式指定。
/// </summary>
public enum CombatWeaponAudioClass
{
    Auto,
    Generic,
    Knife,
    Axe,
    Pickaxe,
    Spear,
    Blunt,
    None,
    Bow,
    Crossbow,
    Sling,
    Blowpipe
}

/// <summary>
/// 受击对象材质。Auto 会根据物品 ID、名称和标签推断，
/// 新对象可直接在 Mod_DamageReceiver 上显式指定。
/// </summary>
public enum CombatImpactMaterial
{
    Auto,
    Default,
    Foliage,
    Wood,
    Stone,
    Metal,
    Flesh,
    None,
    Ice
}

[Serializable]
public sealed class CombatImpactAudioOverride
{
    public CombatImpactMaterial Material = CombatImpactMaterial.Default;

    [Tooltip("命中该材质时使用的 AudioCue ID。留空则继续使用内置路由。")]
    public string CueId;
    public string BlockedCueId;
}

/// <summary>
/// 战斗音效只负责路由，不参与伤害计算。
/// 一次攻击由“武器动作层”与“受击材质层”组成，材质层支持武器×材质覆盖。
/// </summary>
public static class CombatAudioRouter
{
    #region 武器动作声音
    public static void PlayWeaponAttack(Mod_Damage sender)
    {
        if (sender == null || sender.SuppressProjectileAttackAudio || !GameNetwork.HasStateAuthority)
            return;

        CombatWeaponAudioClass weaponClass = ResolveWeaponClass(sender);
        if (weaponClass == CombatWeaponAudioClass.None)
            return;

        Item attacker = sender.item;
        string cueId = string.IsNullOrWhiteSpace(sender.AttackAudioCueId)
            ? GetWeaponAttackCue(weaponClass)
            : sender.AttackAudioCueId.Trim();

        if (string.IsNullOrWhiteSpace(cueId))
            return;

        Transform origin = attacker != null ? attacker.transform : sender.transform;
        CombatAudioFeedback.Publish(cueId, CombatAudioLocation.Capture(origin.position,
            GameplayWorldRegistry.GetItemWorldKey(attacker), attacker));
    }

    public static void PlayProjectileLaunch(Item weapon)
    {
        CombatWeaponAudioClass kind = ResolveWeaponClass(weapon);
        Mod_Bow bow = weapon.itemMods?.GetMod_ByID<Mod_Bow>(Mod_Bow.PersistedModuleId);
        string cue = !string.IsNullOrWhiteSpace(bow?.LaunchAudioCueId) ? bow.LaunchAudioCueId : GetWeaponAttackCue(kind);
        CombatAudioFeedback.Publish(cue, CombatAudioLocation.Capture(
            weapon.transform.position, GameplayWorldRegistry.GetItemWorldKey(weapon), weapon));
    }
    #endregion

    #region 命中音效路由

    public static void PlayImpact(Mod_DamageReceiver receiver, DamageReceiverDamageInfo damageInfo)
    {
        if (receiver == null ||
            damageInfo == null ||
            damageInfo.Context.Attack.Source.Backend == FlatWorld.Combat.CombatBackend.Environment ||
            (damageInfo.DamageSender == null && !damageInfo.Context.Attack.Source.IsValid))
        {
            return;
        }

        Mod_Damage sender = damageInfo.DamageSender as Mod_Damage;
        CombatWeaponAudioClass weaponClass = sender != null
            ? ResolveWeaponClass(sender)
            : ResolveWeaponClass(damageInfo.Attacker);
        Vector3 hitPosition = damageInfo.HitPosition;

        PlayImpact(receiver.item?.itemData, receiver.ImpactAudioMaterial, receiver.HurtAudioCueId,
            damageInfo.DamageValue, hitPosition, sender, weaponClass, receiver.BlockedAudioCueId,
            CombatAudioLocation.Capture(hitPosition, GameplayWorldRegistry.GetItemWorldKey(receiver.item), receiver.item));
    }

    // 无外壳资源在权威生命提交后复用同一选音，不要求创建受击模块或依赖树的具体类型。
    internal static void PlayImpact(ItemData target, CombatImpactMaterial configuredMaterial, string targetCueId,
        float actualDamage, Vector3 hitPosition, Mod_Damage sender = null,
        CombatWeaponAudioClass weaponClass = CombatWeaponAudioClass.Generic, string blockedCueId = null,
        CombatAudioLocation? location = null)
    {
        CombatImpactMaterial material = ResolveImpactMaterial(target, configuredMaterial);
        string weaponCueId = null;
        string weaponBlockedCueId = null;
        if (sender != null)
        {
            weaponClass = ResolveWeaponClass(sender);
            sender.TryGetImpactAudioOverride(material, out weaponCueId);
            sender.TryGetImpactAudioOverride(material, out weaponBlockedCueId, true);
        }

        string cueId = ResolveImpactCue(weaponClass, material, actualDamage, targetCueId, weaponCueId,
            blockedCueId, weaponBlockedCueId);
        if (!string.IsNullOrWhiteSpace(cueId))
            CombatAudioFeedback.Publish(cueId, location ?? CombatAudioLocation.Capture(hitPosition,
                GameplayWorldRegistry.ExecutionWorldKey, sender?.item));
    }

    /// <summary>全部后端共用已结算命中的选音，保留武器覆盖、对象覆盖和内置材质的顺序。</summary>
    internal static string ResolveImpactCue(CombatWeaponAudioClass weaponClass, CombatImpactMaterial material,
        float actualDamage, string targetCueId = null, string weaponCueId = null,
        string targetBlockedCueId = null, string weaponBlockedCueId = null)
    {
        if (!float.IsFinite(actualDamage) || actualDamage < 0f ||
            material == CombatImpactMaterial.None)
            return null;

        if (actualDamage == 0f)
        {
            string blocked = !string.IsNullOrWhiteSpace(weaponBlockedCueId) ? weaponBlockedCueId : targetBlockedCueId;
            return string.IsNullOrWhiteSpace(blocked) ? GetMaterialBlockedCue(material) : blocked.Trim();
        }

        string cueId = weaponCueId;
        if (string.IsNullOrWhiteSpace(cueId))
            cueId = targetCueId;
        if (string.IsNullOrWhiteSpace(cueId))
            cueId = GetBuiltInPairCue(weaponClass, material);
        if (string.IsNullOrWhiteSpace(cueId))
            cueId = GetMaterialImpactCue(material);
        return string.IsNullOrWhiteSpace(cueId) ? null : cueId.Trim();
    }

    #endregion

    #region 纯数据目标声音
    public static string ResolveDefinitionImpactCue(RuntimeItemDefinition definition, Mod_Damage sender, float actualDamage)
    {
        CombatImpactMaterial material = definition.CombatImpactAudioMaterial;
        string weaponCue = null, weaponBlockedCue = null;
        sender?.TryGetImpactAudioOverride(material, out weaponCue);
        sender?.TryGetImpactAudioOverride(material, out weaponBlockedCue, true);
        return ResolveImpactCue(sender != null ? ResolveWeaponClass(sender) : CombatWeaponAudioClass.Generic,
            material, actualDamage, definition.CombatHurtAudioCueId, weaponCue,
            definition.CombatBlockedAudioCueId, weaponBlockedCue);
    }

    private static string GetMaterialBlockedCue(CombatImpactMaterial material)
        => material switch
        {
            CombatImpactMaterial.Foliage => "combat.blocked.foliage",
            CombatImpactMaterial.Wood => "combat.blocked.wood",
            CombatImpactMaterial.Stone => "combat.blocked.stone",
            CombatImpactMaterial.Metal => "combat.blocked.metal",
            CombatImpactMaterial.Flesh => "combat.blocked.flesh",
            CombatImpactMaterial.Ice => "combat.blocked.ice",
            _ => "combat.blocked.default"
        };
    #endregion

    private static CombatWeaponAudioClass ResolveWeaponClass(Mod_Damage sender)
    {
        if (sender.WeaponAudioClass != CombatWeaponAudioClass.Auto)
            return sender.WeaponAudioClass;

        return ResolveWeaponClass(sender.item);
    }

    private static CombatWeaponAudioClass ResolveWeaponClass(Item attacker)
    {
        if (ContainsAny(attacker, "crossbow")) return CombatWeaponAudioClass.Crossbow;
        if (ContainsAny(attacker, "blowpipe", "blowgun")) return CombatWeaponAudioClass.Blowpipe;
        if (ContainsAny(attacker, "sling")) return CombatWeaponAudioClass.Sling;
        if (ContainsAny(attacker, "bow")) return CombatWeaponAudioClass.Bow;
        if (ContainsAny(attacker, "pickaxe", "pixkaxe", "miningpick"))
            return CombatWeaponAudioClass.Pickaxe;
        if (ContainsAny(attacker, "knife", "dagger", "小刀", "匕首"))
            return CombatWeaponAudioClass.Knife;
        if (ContainsAny(attacker, "axe", "hatchet", "斧"))
            return CombatWeaponAudioClass.Axe;
        if (ContainsAny(attacker, "spear", "lance", "矛", "长枪"))
            return CombatWeaponAudioClass.Spear;
        if (ContainsAny(attacker, "hammer", "club", "mace", "staff", "fist", "锤", "棍"))
            return CombatWeaponAudioClass.Blunt;

        return CombatWeaponAudioClass.Generic;
    }

    internal static CombatImpactMaterial ResolveImpactMaterial(ItemData target,
        CombatImpactMaterial configuredMaterial = CombatImpactMaterial.Auto)
    {
        if (configuredMaterial != CombatImpactMaterial.Auto)
            return configuredMaterial;

        if (ContainsAny(target, "iceberg", "ice_", "冰")) return CombatImpactMaterial.Ice;

        if (ContainsAny(target, "bush", "shrub", "grass", "leaf", "foliage", "plant", "vine", "fern", "flower", "灌木", "草", "叶"))
            return CombatImpactMaterial.Foliage;

        // 生物名称可能带有皮肤/生态后缀（例如 WildBoar_Variant），因此优先识别生物。
        if (ContainsAny(target, "player", "wildboar", "boar", "wolf", "chicken", "animal", "deer", "pig", "fish", "monster", "玩家", "野猪", "狼", "鸡"))
            return CombatImpactMaterial.Flesh;

        // 矿脉表面按石材处理；金属锭和金属建筑才按金属处理。
        if (ContainsAny(target, "stone", "rock", "flint", "ore_", "mine_", "coal", "golem", "石", "岩", "矿"))
            return CombatImpactMaterial.Stone;

        if (ContainsAny(target, "tree", "wood", "log", "timber", "chest", "door", "木", "原木"))
            return CombatImpactMaterial.Wood;

        if (ContainsAny(target, "metal", "ingot", "iron", "copper", "bronze", "steel", "tin", "金属", "铁", "铜"))
            return CombatImpactMaterial.Metal;

        if (HasTag(target, Tag.Plant))
            return CombatImpactMaterial.Foliage;
        if (HasTag(target, Tag.Tree))
            return CombatImpactMaterial.Wood;
        if (HasTag(target, Tag.Player))
            return CombatImpactMaterial.Flesh;

        return CombatImpactMaterial.Default;
    }

    private static string GetWeaponAttackCue(CombatWeaponAudioClass weaponClass)
    {
        switch (weaponClass)
        {
            case CombatWeaponAudioClass.Knife: return AudioEventIds.CombatWeaponKnifeAttack;
            case CombatWeaponAudioClass.Axe: return AudioEventIds.CombatWeaponAxeAttack;
            case CombatWeaponAudioClass.Pickaxe: return AudioEventIds.CombatWeaponPickaxeAttack;
            case CombatWeaponAudioClass.Spear: return AudioEventIds.CombatWeaponSpearAttack;
            case CombatWeaponAudioClass.Blunt: return AudioEventIds.CombatWeaponBluntAttack;
            case CombatWeaponAudioClass.Bow: return "combat.weapon.bow.attack";
            case CombatWeaponAudioClass.Crossbow: return "combat.weapon.crossbow.attack";
            case CombatWeaponAudioClass.Sling: return "combat.weapon.sling.attack";
            case CombatWeaponAudioClass.Blowpipe: return "combat.weapon.blowpipe.attack";
            case CombatWeaponAudioClass.None: return null;
            default: return AudioEventIds.CombatWeaponGenericAttack;
        }
    }

    private static string GetBuiltInPairCue(
        CombatWeaponAudioClass weaponClass,
        CombatImpactMaterial material)
    {
        if (weaponClass == CombatWeaponAudioClass.Knife && material == CombatImpactMaterial.Foliage)
            return AudioEventIds.CombatImpactKnifeFoliage;
        if (weaponClass == CombatWeaponAudioClass.Knife && material == CombatImpactMaterial.Stone)
            return AudioEventIds.CombatImpactKnifeStone;
        if (weaponClass == CombatWeaponAudioClass.Axe && material == CombatImpactMaterial.Wood)
            return AudioEventIds.CombatImpactAxeWood;
        if (weaponClass == CombatWeaponAudioClass.Pickaxe && material == CombatImpactMaterial.Stone)
            return AudioEventIds.CombatImpactPickaxeStone;

        return null;
    }

    private static string GetMaterialImpactCue(CombatImpactMaterial material)
    {
        switch (material)
        {
            case CombatImpactMaterial.Foliage: return AudioEventIds.CombatImpactFoliage;
            case CombatImpactMaterial.Wood: return AudioEventIds.CombatImpactWood;
            case CombatImpactMaterial.Stone: return AudioEventIds.CombatImpactStone;
            case CombatImpactMaterial.Metal: return AudioEventIds.CombatImpactMetal;
            case CombatImpactMaterial.Flesh: return AudioEventIds.CombatImpactFlesh;
            case CombatImpactMaterial.Ice: return "combat.impact.ice";
            case CombatImpactMaterial.None: return null;
            default: return AudioEventIds.CombatImpactDefault;
        }
    }

    private static bool ContainsAny(Item item, params string[] tokens)
        => ContainsAny(item?.itemData, tokens);

    private static bool ContainsAny(ItemData item, params string[] tokens)
    {
        if (item == null || tokens == null)
            return false;

        if (ContainsAny(item.IDName, tokens) ||
            ContainsAny(item.GameName, tokens))
        {
            return true;
        }

        List<string> tags = item.Tags;
        if (tags == null)
            return false;

        for (int i = 0; i < tags.Count; i++)
        {
            if (ContainsAny(tags[i], tokens))
                return true;
        }

        return false;
    }

    private static bool ContainsAny(string value, string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        for (int i = 0; i < tokens.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(tokens[i]) &&
                value.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTag(ItemData item, string expectedTag)
    {
        List<string> tags = item?.Tags;
        if (tags == null || string.IsNullOrWhiteSpace(expectedTag))
            return false;

        for (int i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i], expectedTag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
