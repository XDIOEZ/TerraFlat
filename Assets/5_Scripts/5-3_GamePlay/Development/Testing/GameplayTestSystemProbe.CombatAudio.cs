using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using FlatWorld.Audio;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 真实命中选音与树木定义采样

    private static void RegisterCombatAudioOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("audio/combat_impact", EntityCombatImpactAudio);
        operations.Add("audio/tree_impact_configuration", EntityTreeImpactConfiguration);
        operations.Add("audio/combat_configuration", EntityCombatAudioConfiguration);
    }

    private static JObject EntityCombatImpactAudio(JObject args)
    {
        if (!Enum.TryParse(Text(args, "weapon", "Generic"), true, out CombatWeaponAudioClass weapon) ||
            !Enum.TryParse(Text(args, "material", "Auto"), true, out CombatImpactMaterial configuredMaterial))
            throw new ArgumentException("命中声音采样需要有效武器类别与受击材质。");

        var target = new Data_GeneralItem
        {
            IDName = Text(args, "itemId", ""),
            GameName = Text(args, "gameName", ""),
            Tags = new List<string>()
        };
        if (args["tags"] is JArray tags)
            foreach (JToken tag in tags)
                target.Tags.Add(tag.Value<string>());

        float damage = Text(args, "invalidDamage", "") switch
        {
            "NaN" => float.NaN,
            "Infinity" => float.PositiveInfinity,
            _ => (float)Number(args, "damage", 1)
        };
        CombatImpactMaterial material = CombatAudioRouter.ResolveImpactMaterial(target, configuredMaterial);
        // 直接采样两种后端共用的正式选音，Lua 持有命中、覆盖优先级和拒播断言。
        string cue = CombatAudioRouter.ResolveImpactCue(weapon, material, damage,
            Text(args, "targetCue", ""), Text(args, "weaponCue", ""),
            Text(args, "targetBlockedCue", ""), Text(args, "weaponBlockedCue", ""));
        return new JObject
        {
            ["material"] = material.ToString(),
            ["cue"] = cue ?? string.Empty,
            ["silent"] = string.IsNullOrWhiteSpace(cue)
        };
    }

    private static JObject EntityTreeImpactConfiguration(JObject args)
    {
        var trees = new JArray();
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
        {
            if (definition.Abstract || definition.Tags == null ||
                !definition.Tags.Exists(tag => string.Equals(tag, Tag.Tree, StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (ItemModuleDefinitionDto module in definition.Modules.Values)
            {
                if (module.Enabled == false || module.Prefab != "Module_DamageReciver")
                    continue;
                string cue = module.Parameters?.Value<string>("hurtAudioCueId");
                string blocked = module.Parameters?.Value<string>("blockedAudioCueId");
                CombatImpactMaterial material = CombatAudioRouter.ResolveImpactMaterial(new Data_GeneralItem
                    { IDName = definition.Id, GameName = definition.GameName, Tags = definition.Tags },
                    (CombatImpactMaterial)(module.Parameters?.Value<int?>("impactAudioMaterial") ?? 0));
                trees.Add(new JObject
                {
                    ["id"] = definition.Id,
                    ["cue"] = cue ?? string.Empty,
                    ["material"] = material.ToString(),
                    ["normalCue"] = CombatAudioRouter.ResolveImpactCue(CombatWeaponAudioClass.Generic,
                        material, 1f, cue) ?? string.Empty,
                    ["axeCue"] = CombatAudioRouter.ResolveImpactCue(CombatWeaponAudioClass.Axe,
                        material, 1f, cue) ?? string.Empty,
                    ["blockedCue"] = CombatAudioRouter.ResolveImpactCue(CombatWeaponAudioClass.Axe,
                        material, 0f, cue, targetBlockedCueId: blocked) ?? string.Empty
                });
            }
        }
        return new JObject { ["trees"] = trees };
    }

    private static JObject EntityCombatAudioConfiguration(JObject args)
    {
        var definitions = new List<ItemDefinitionDto>(ItemDefinitionCatalogLoader.LoadBuiltInDefinitions());
        definitions.AddRange(ActorDefinitionCatalogLoader.LoadBuiltInDefinitions());
        AudioCatalog catalog = Resources.Load<AudioCatalog>("Audio/AudioCatalog");
        var targets = new JArray();
        var cues = new JArray();
        int damageDefinitions = 0, healthDefinitions = 0;
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (definition.Abstract) continue;
            bool explicitHealth = false;
            foreach (ItemModuleDefinitionDto module in definition.Modules.Values)
                explicitHealth |= module.Enabled != false && module.Prefab == "Module_DamageReciver";
            if (!explicitHealth && definition.Health?.HasHp == true)
            {
                healthDefinitions++;
                ItemHealthDefinitionDto health = definition.Health;
                CombatImpactMaterial material = CombatAudioRouter.ResolveImpactMaterial(new Data_GeneralItem
                    { IDName = definition.Id, GameName = definition.GameName, Tags = definition.Tags }, health.ImpactAudioMaterial);
                string damaged = CombatAudioRouter.ResolveImpactCue(CombatWeaponAudioClass.Generic, material, 1f, health.HurtAudioCueId);
                string blocked = CombatAudioRouter.ResolveImpactCue(CombatWeaponAudioClass.Generic, material, 0f,
                    targetBlockedCueId: health.BlockedAudioCueId);
                targets.Add(new JObject { ["id"] = definition.Id, ["material"] = material.ToString(),
                    ["damagedExists"] = material == CombatImpactMaterial.None || catalog.TryGet(damaged, out _),
                    ["blockedExists"] = material == CombatImpactMaterial.None || catalog.TryGet(blocked, out _) });
            }
            foreach (ItemModuleDefinitionDto module in definition.Modules.Values)
            {
                if (module.Enabled == false) continue;
                if (module.Prefab == "Mod_Damage") damageDefinitions++;
                if (module.Prefab != "Module_DamageReciver") continue;
                healthDefinitions++;
                JObject parameters = module.Parameters;
                CombatImpactMaterial material = CombatAudioRouter.ResolveImpactMaterial(new Data_GeneralItem
                    { IDName = definition.Id, GameName = definition.GameName, Tags = definition.Tags },
                    (CombatImpactMaterial)(parameters?.Value<int?>("impactAudioMaterial") ?? 0));
                string damaged = CombatAudioRouter.ResolveImpactCue(CombatWeaponAudioClass.Generic, material, 1f,
                    parameters?.Value<string>("hurtAudioCueId"));
                string blocked = CombatAudioRouter.ResolveImpactCue(CombatWeaponAudioClass.Generic, material, 0f,
                    targetBlockedCueId: parameters?.Value<string>("blockedAudioCueId"));
                targets.Add(new JObject { ["id"] = definition.Id, ["material"] = material.ToString(),
                    ["damagedExists"] = material == CombatImpactMaterial.None || catalog.TryGet(damaged, out _),
                    ["blockedExists"] = material == CombatImpactMaterial.None || catalog.TryGet(blocked, out _) });
            }
        }
        var cueIds = new List<string>();
        foreach (string material in new[] { "default", "foliage", "wood", "stone", "metal", "flesh", "ice" })
        foreach (string result in new[] { "impact", "blocked" })
            cueIds.Add($"combat.{result}.{material}");
        foreach (string weapon in new[] { "generic", "knife", "axe", "pickaxe", "spear", "blunt", "bow", "crossbow", "sling", "blowpipe" })
            cueIds.Add($"combat.weapon.{weapon}.attack");
        foreach (string pair in new[] { "knife.foliage", "knife.stone", "axe.wood", "pickaxe.stone" })
            cueIds.Add("combat.impact." + pair);
        foreach (string id in cueIds)
        {
            bool exists = catalog.TryGet(id, out AudioCue cue);
            bool validClips = exists;
            var clipIds = new HashSet<int>();
            if (exists)
                foreach (AudioClip clip in cue.Clips)
                    validClips &= clip != null && clip.channels == 1 && clip.frequency == 48000 && clip.samples > 0 && clipIds.Add(clip.GetInstanceID());
            cues.Add(new JObject { ["id"] = id, ["exists"] = exists, ["clipCount"] = cue != null ? cue.Clips.Count : 0,
                ["validClips"] = validClips, ["spatial"] = cue != null && cue.SpatialBlend == 1f, ["sfx"] = cue != null && cue.Bus == AudioBus.Sfx,
                ["loop"] = cue != null && cue.Loop });
        }
        return new JObject { ["damageDefinitions"] = damageDefinitions, ["healthDefinitions"] = healthDefinitions,
            ["targets"] = targets, ["cues"] = cues };
    }

    #endregion
}
