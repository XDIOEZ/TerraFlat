using System;
using System.Collections.Generic;
using FlatWorld.Audio;
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
    None
}

/// <summary>
/// 受击对象材质。Auto 会根据物品 ID、名称和标签推断，
/// 新对象可直接在 DamageReceiver 上显式指定。
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
    None
}

[Serializable]
public sealed class CombatImpactAudioOverride
{
    public CombatImpactMaterial Material = CombatImpactMaterial.Default;

    [Tooltip("命中该材质时使用的 AudioCue ID。留空则继续使用内置路由。")]
    public string CueId;
}

/// <summary>
/// 战斗音效只负责路由，不参与伤害计算。
/// 一次攻击由“武器动作层”与“受击材质层”组成，材质层支持武器×材质覆盖。
/// </summary>
public static class CombatAudioRouter
{
    private const float DuplicateAttackWindow = 0.035f;

    private static readonly Dictionary<int, float> LastAttackTimeByItem =
        new Dictionary<int, float>();

    public static void PlayWeaponAttack(Mod_Damage sender)
    {
        if (sender == null)
            return;

        CombatWeaponAudioClass weaponClass = ResolveWeaponClass(sender);
        if (weaponClass == CombatWeaponAudioClass.None)
            return;

        Item attacker = sender.item;
        int sourceId = attacker != null ? attacker.GetInstanceID() : sender.GetInstanceID();
        float now = Time.unscaledTime;
        if (LastAttackTimeByItem.TryGetValue(sourceId, out float previousTime) &&
            now - previousTime < DuplicateAttackWindow)
        {
            return;
        }

        if (LastAttackTimeByItem.Count > 256)
            LastAttackTimeByItem.Clear();
        LastAttackTimeByItem[sourceId] = now;

        string cueId = string.IsNullOrWhiteSpace(sender.AttackAudioCueId)
            ? GetWeaponAttackCue(weaponClass)
            : sender.AttackAudioCueId.Trim();

        if (string.IsNullOrWhiteSpace(cueId))
            return;

        Transform origin = attacker != null ? attacker.transform : sender.transform;
        AudioService.Instance.PlayAttached(cueId, origin);
    }

    public static void PlayImpact(DamageReceiver receiver, DamageReceiverDamageInfo damageInfo)
    {
        if (receiver == null ||
            damageInfo == null ||
            damageInfo.DamageSender == null ||
            damageInfo.DamageValue <= 0f)
        {
            return;
        }

        CombatImpactMaterial material = ResolveImpactMaterial(receiver);
        if (material == CombatImpactMaterial.None)
            return;

        Mod_Damage sender = damageInfo.DamageSender as Mod_Damage;
        CombatWeaponAudioClass weaponClass = sender != null
            ? ResolveWeaponClass(sender)
            : ResolveWeaponClass(damageInfo.Attacker);

        string cueId = null;
        if (sender != null)
            sender.TryGetImpactAudioOverride(material, out cueId);

        if (string.IsNullOrWhiteSpace(cueId))
            cueId = receiver.HurtAudioCueId;

        if (string.IsNullOrWhiteSpace(cueId))
            cueId = GetBuiltInPairCue(weaponClass, material);

        if (string.IsNullOrWhiteSpace(cueId))
            cueId = GetMaterialImpactCue(material);

        if (string.IsNullOrWhiteSpace(cueId))
            return;

        Vector3 hitPosition = damageInfo.HitPosition;
        if (receiver.item != null)
            hitPosition = receiver.item.transform.position;

        AudioService.Instance.PlayAt(cueId.Trim(), hitPosition);
    }

    private static CombatWeaponAudioClass ResolveWeaponClass(Mod_Damage sender)
    {
        if (sender.WeaponAudioClass != CombatWeaponAudioClass.Auto)
            return sender.WeaponAudioClass;

        return ResolveWeaponClass(sender.item);
    }

    private static CombatWeaponAudioClass ResolveWeaponClass(Item attacker)
    {
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

    private static CombatImpactMaterial ResolveImpactMaterial(DamageReceiver receiver)
    {
        if (receiver.ImpactAudioMaterial != CombatImpactMaterial.Auto)
            return receiver.ImpactAudioMaterial;

        Item target = receiver.item;
        if (ContainsAny(target, "bush", "shrub", "grass", "leaf", "foliage", "plant", "vine", "fern", "flower", "灌木", "草", "叶"))
            return CombatImpactMaterial.Foliage;

        // 生物名称可能带有皮肤/生态后缀（例如 WildBoar_Tree），因此优先识别生物。
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
            case CombatImpactMaterial.None: return null;
            default: return AudioEventIds.CombatImpactDefault;
        }
    }

    private static bool ContainsAny(Item item, params string[] tokens)
    {
        if (item == null || item.itemData == null || tokens == null)
            return false;

        if (ContainsAny(item.itemData.IDName, tokens) ||
            ContainsAny(item.itemData.GameName, tokens))
        {
            return true;
        }

        List<string> tags = item.itemData.Tags;
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

    private static bool HasTag(Item item, string expectedTag)
    {
        List<string> tags = item?.itemData?.Tags;
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
