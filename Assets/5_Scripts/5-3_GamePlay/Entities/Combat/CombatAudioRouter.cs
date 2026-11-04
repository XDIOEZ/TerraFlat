using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using FlatWorld.Networking;
using FlatWorld.Combat;
using Unity.Collections;
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

/// <summary>声音接触硬度三档；Auto 由矿物参考或物品材质在定义编译时确定。</summary>
public enum CombatImpactHardness { Auto, Low, Medium, High }

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
/// 一次攻击由“武器动作层”与“接触结果层”组成；通用结果按双方硬度选音，专用材质覆盖优先。
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
    private static readonly string[] GenericImpactCues =
    {
        "combat.blocked.low.low", "combat.blocked.low.medium", "combat.blocked.low.high",
        "combat.blocked.medium.low", "combat.blocked.medium.medium", "combat.blocked.medium.high",
        "combat.blocked.high.low", "combat.blocked.high.medium", "combat.blocked.high.high",
        "combat.impact.low.low", "combat.impact.low.medium", "combat.impact.low.high",
        "combat.impact.medium.low", "combat.impact.medium.medium", "combat.impact.medium.high",
        "combat.impact.high.low", "combat.impact.high.medium", "combat.impact.high.high"
    };

    public static void PlayImpact(Mod_DamageReceiver receiver, DamageReceiverDamageInfo damageInfo)
    {
        if (receiver == null || damageInfo == null ||
            damageInfo.Context.Attack.Source.Backend == CombatBackend.Environment ||
            (damageInfo.DamageSender == null && !damageInfo.Context.Attack.Source.IsValid)) return;
        Mod_Damage sender = damageInfo.DamageSender as Mod_Damage;
        Vector3 point = damageInfo.HitPosition;
        PlayImpact(receiver.item?.itemData, receiver.ImpactAudioMaterial, receiver.HurtAudioCueId,
            damageInfo.DamageValue, point, sender, receiver.BlockedAudioCueId,
            CombatAudioLocation.Capture(point, GameplayWorldRegistry.GetItemWorldKey(receiver.item), receiver.item),
            ResolveSourceImpactHardness(sender, damageInfo.Context.SourceItemId));
    }

    internal static void PlayImpact(ItemData target, CombatImpactMaterial configuredMaterial, string targetCueId,
        float actualDamage, Vector3 hitPosition, Mod_Damage sender = null, string blockedCueId = null,
        CombatAudioLocation? location = null, CombatImpactHardness sourceHardness = CombatImpactHardness.Auto)
    {
        CombatImpactMaterial material = ResolveImpactMaterial(target, configuredMaterial);
        string weaponCue = null, weaponBlockedCue = null;
        sender?.TryGetImpactAudioOverride(material, out weaponCue);
        sender?.TryGetImpactAudioOverride(material, out weaponBlockedCue, true);
        string cue = ResolveImpactCue(sourceHardness == CombatImpactHardness.Auto
                ? ResolveItemImpactHardness(sender?.item?.itemData) : sourceHardness,
            ResolveItemImpactHardness(target, material), material, actualDamage,
            targetCueId, weaponCue, blockedCueId, weaponBlockedCue);
        CombatAudioFeedback.Publish(cue, location ?? CombatAudioLocation.Capture(hitPosition,
            GameplayWorldRegistry.ExecutionWorldKey, sender?.item));
    }

    // 专用声音显式覆盖；其余命中只按攻击方、受击方和实际伤害选择十八类通用声。
    internal static string ResolveImpactCue(CombatImpactHardness sourceHardness, CombatImpactHardness targetHardness,
        CombatImpactMaterial targetMaterial, float actualDamage, string targetCueId = null, string weaponCueId = null,
        string targetBlockedCueId = null, string weaponBlockedCueId = null)
    {
        if (!float.IsFinite(actualDamage) || actualDamage < 0f || targetMaterial == CombatImpactMaterial.None) return null;
        string weapon = actualDamage > 0f ? weaponCueId : weaponBlockedCueId;
        string target = actualDamage > 0f ? targetCueId : targetBlockedCueId;
        if (!string.IsNullOrWhiteSpace(weapon)) return weapon.Trim();
        if (!string.IsNullOrWhiteSpace(target)) return target.Trim();
        int source = (int)(sourceHardness == CombatImpactHardness.Auto ? CombatImpactHardness.Medium : sourceHardness) - 1;
        int receiver = (int)(targetHardness == CombatImpactHardness.Auto ? CombatImpactHardness.Medium : targetHardness) - 1;
        return GenericImpactCues[(actualDamage > 0f ? 9 : 0) + source * 3 + receiver];
    }

    public static string ResolveDefinitionImpactCue(RuntimeItemDefinition definition, Mod_Damage sender,
        float actualDamage, in CombatDamageContext context)
    {
        CombatImpactMaterial material = definition.CombatImpactAudioMaterial;
        string weaponCue = null, weaponBlockedCue = null;
        sender?.TryGetImpactAudioOverride(material, out weaponCue);
        sender?.TryGetImpactAudioOverride(material, out weaponBlockedCue, true);
        return ResolveImpactCue(ResolveSourceImpactHardness(sender, context.SourceItemId), definition.CombatImpactAudioHardness,
            material, actualDamage, definition.CombatHurtAudioCueId, weaponCue, definition.CombatBlockedAudioCueId, weaponBlockedCue);
    }
    #endregion

    #region 冷编译硬度与来源身份缓存
    internal static CombatImpactHardness ResolveConfiguredImpactHardness(string itemId, CombatImpactMaterial material,
        CombatImpactHardness configured = CombatImpactHardness.Auto, MineralPropertyCatalog minerals = null)
    {
        if (configured != CombatImpactHardness.Auto) return configured;
        if (minerals != null && minerals.TryGetByItemId(itemId, out MineralPropertyReference mineral))
            return mineral.Hardness <= 2 ? CombatImpactHardness.Low :
                mineral.Hardness <= 5 ? CombatImpactHardness.Medium : CombatImpactHardness.High;
        return material switch
        {
            CombatImpactMaterial.Foliage or CombatImpactMaterial.Wood or CombatImpactMaterial.Flesh => CombatImpactHardness.Low,
            CombatImpactMaterial.Stone or CombatImpactMaterial.Metal => CombatImpactHardness.High,
            _ => CombatImpactHardness.Medium
        };
    }

    private static CombatImpactHardness ResolveItemImpactHardness(ItemData data,
        CombatImpactMaterial material = CombatImpactMaterial.Auto)
    {
        if (data != null && GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition))
            return definition.CombatImpactAudioHardness;
        return ResolveConfiguredImpactHardness(data?.IDName, ResolveImpactMaterial(data, material));
    }

    private static readonly Dictionary<FixedString128Bytes, CombatImpactHardness> SourceHardnessById = new();
    private static GameRes sourceHardnessResources;
    private static ulong sourceHardnessRevision;

    internal static CombatImpactHardness ResolveSourceImpactHardness(Mod_Damage sender, FixedString128Bytes sourceId)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (sourceId.Length == 0 || resources == null) return ResolveItemImpactHardness(sender?.item?.itemData);
        if (sourceHardnessResources != resources || sourceHardnessRevision != resources.ItemDefinitionRevision)
        {
            SourceHardnessById.Clear();
            sourceHardnessResources = resources;
            sourceHardnessRevision = resources.ItemDefinitionRevision;
        }
        if (SourceHardnessById.TryGetValue(sourceId, out CombatImpactHardness cached)) return cached;
        CombatImpactHardness hardness = resources.TryGetItemDefinition(sourceId.ToString(), out RuntimeItemDefinition definition)
            ? definition.CombatImpactAudioHardness : ResolveItemImpactHardness(sender?.item?.itemData);
        SourceHardnessById.Add(sourceId, hardness);
        return hardness;
    }
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
