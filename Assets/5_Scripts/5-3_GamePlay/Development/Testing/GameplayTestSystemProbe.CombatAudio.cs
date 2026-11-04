using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using FlatWorld.Audio;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 正式硬度定义与命中选音采样
    private static void RegisterCombatAudioOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("audio/combat_impact", EntityCombatImpactAudio);
        operations.Add("audio/tree_impact_configuration", EntityTreeImpactConfiguration);
        operations.Add("audio/combat_configuration", EntityCombatAudioConfiguration);
    }

    private static Dictionary<string, ItemDefinitionDto> LoadCombatAudioDefinitionInputs()
    {
        var definitions = new Dictionary<string, ItemDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions()) definitions.Add(definition.Id, definition);
        foreach (ItemDefinitionDto definition in ActorDefinitionCatalogLoader.LoadBuiltInDefinitions()) definitions.Add(definition.Id, definition);
        return definitions;
    }

    private static JObject FindCombatAudioHealthParameters(ItemDefinitionDto definition)
    {
        if (definition == null) return null;
        foreach (ItemModuleDefinitionDto module in definition.Modules.Values)
            if (module.Enabled != false && module.Prefab == "Module_DamageReciver") return module.Parameters;
        return null;
    }

    private static CombatImpactMaterial ResolveCombatAudioDefinitionMaterial(ItemDefinitionDto definition, string itemId,
        CombatImpactMaterial configured = CombatImpactMaterial.Auto)
    {
        if (configured == CombatImpactMaterial.Auto)
            configured = (CombatImpactMaterial)(FindCombatAudioHealthParameters(definition)?.Value<int?>("impactAudioMaterial") ??
                (int)(definition?.Health?.ImpactAudioMaterial ?? CombatImpactMaterial.Auto));
        return CombatAudioRouter.ResolveImpactMaterial(new Data_GeneralItem
            { IDName = itemId, GameName = definition?.GameName, Tags = definition?.Tags }, configured);
    }

    private static JObject EntityCombatImpactAudio(JObject args)
    {
        if (!Enum.TryParse(Text(args, "material", "Auto"), true, out CombatImpactMaterial configuredMaterial) ||
            !Enum.TryParse(Text(args, "sourceHardness", "Auto"), true, out CombatImpactHardness sourceHardness) ||
            !Enum.TryParse(Text(args, "targetHardness", "Auto"), true, out CombatImpactHardness targetHardness))
            throw new ArgumentException("命中声音采样需要有效材质和硬度档位。");
        var definitions = LoadCombatAudioDefinitionInputs();
        MineralPropertyCatalog minerals = MineralPropertyCatalog.LoadBuiltIn();
        string sourceId = Text(args, "sourceItemId", ""), targetId = Text(args, "itemId", "");
        definitions.TryGetValue(sourceId, out ItemDefinitionDto source);
        definitions.TryGetValue(targetId, out ItemDefinitionDto target);
        CombatImpactMaterial material = ResolveCombatAudioDefinitionMaterial(target, targetId, configuredMaterial);
        if (sourceHardness == CombatImpactHardness.Auto)
            sourceHardness = CombatAudioRouter.ResolveConfiguredImpactHardness(sourceId,
                ResolveCombatAudioDefinitionMaterial(source, sourceId), source?.ImpactAudioHardness ?? CombatImpactHardness.Auto, minerals);
        if (targetHardness == CombatImpactHardness.Auto)
            targetHardness = CombatAudioRouter.ResolveConfiguredImpactHardness(targetId, material,
                target?.ImpactAudioHardness ?? CombatImpactHardness.Auto, minerals);
        float damage = Text(args, "invalidDamage", "") switch
        {
            "NaN" => float.NaN, "Infinity" => float.PositiveInfinity, _ => (float)Number(args, "damage", 1)
        };
        // Lua只断言正式resolver和正式继承定义的结果，不复制分档和矩阵算法。
        string cue = CombatAudioRouter.ResolveImpactCue(sourceHardness, targetHardness, material, damage,
            Text(args, "targetCue", ""), Text(args, "weaponCue", ""),
            Text(args, "targetBlockedCue", ""), Text(args, "weaponBlockedCue", ""));
        return new JObject { ["material"] = material.ToString(), ["sourceHardness"] = sourceHardness.ToString(),
            ["targetHardness"] = targetHardness.ToString(),
            ["sourceConfiguredHardness"] = (source?.ImpactAudioHardness ?? CombatImpactHardness.Auto).ToString(),
            ["cue"] = cue ?? string.Empty, ["silent"] = cue == null };
    }

    private static JObject EntityTreeImpactConfiguration(JObject args)
    {
        var trees = new JArray();
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
        {
            if (definition.Abstract || definition.Tags == null || !definition.Tags.Exists(tag => tag == Tag.Tree)) continue;
            JObject health = FindCombatAudioHealthParameters(definition);
            if (health == null) continue;
            string cue = health.Value<string>("hurtAudioCueId"), blocked = health.Value<string>("blockedAudioCueId");
            CombatImpactMaterial material = ResolveCombatAudioDefinitionMaterial(definition, definition.Id);
            CombatImpactHardness hardness = CombatAudioRouter.ResolveConfiguredImpactHardness(definition.Id, material, definition.ImpactAudioHardness);
            trees.Add(new JObject { ["id"] = definition.Id, ["cue"] = cue ?? string.Empty, ["material"] = material.ToString(),
                ["normalCue"] = CombatAudioRouter.ResolveImpactCue(CombatImpactHardness.Medium, hardness, material, 1f, cue),
                ["axeCue"] = CombatAudioRouter.ResolveImpactCue(CombatImpactHardness.High, hardness, material, 1f, cue),
                ["blockedCue"] = CombatAudioRouter.ResolveImpactCue(CombatImpactHardness.High, hardness, material, 0f, cue, targetBlockedCueId: blocked) });
        }
        return new JObject { ["trees"] = trees };
    }

    private static JObject EntityCombatAudioConfiguration(JObject args)
    {
        var definitions = LoadCombatAudioDefinitionInputs();
        MineralPropertyCatalog minerals = MineralPropertyCatalog.LoadBuiltIn();
        AudioCatalog catalog = Resources.Load<AudioCatalog>("Audio/AudioCatalog");
        var targets = new JArray();
        var cues = new JArray();
        int damageDefinitions = 0, healthDefinitions = 0;
        foreach (ItemDefinitionDto definition in definitions.Values)
        {
            if (definition.Abstract) continue;
            JObject parameters = FindCombatAudioHealthParameters(definition);
            foreach (ItemModuleDefinitionDto module in definition.Modules.Values)
                if (module.Enabled != false && module.Prefab == "Mod_Damage") damageDefinitions++;
            if (parameters == null && definition.Health?.HasHp != true) continue;
            healthDefinitions++;
            CombatImpactMaterial material = ResolveCombatAudioDefinitionMaterial(definition, definition.Id);
            CombatImpactHardness hardness = CombatAudioRouter.ResolveConfiguredImpactHardness(definition.Id, material, definition.ImpactAudioHardness, minerals);
            string damaged = CombatAudioRouter.ResolveImpactCue(CombatImpactHardness.Medium, hardness, material, 1f,
                parameters?.Value<string>("hurtAudioCueId") ?? definition.Health?.HurtAudioCueId);
            string blocked = CombatAudioRouter.ResolveImpactCue(CombatImpactHardness.Medium, hardness, material, 0f,
                targetBlockedCueId: parameters?.Value<string>("blockedAudioCueId") ?? definition.Health?.BlockedAudioCueId);
            targets.Add(new JObject { ["id"] = definition.Id, ["material"] = material.ToString(), ["hardness"] = hardness.ToString(),
                ["damagedExists"] = material == CombatImpactMaterial.None || catalog.TryGet(damaged, out _),
                ["blockedExists"] = material == CombatImpactMaterial.None || catalog.TryGet(blocked, out _) });
        }
        var cueIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (CombatImpactHardness source in new[] { CombatImpactHardness.Low, CombatImpactHardness.Medium, CombatImpactHardness.High })
        foreach (CombatImpactHardness target in new[] { CombatImpactHardness.Low, CombatImpactHardness.Medium, CombatImpactHardness.High })
        foreach (float damage in new[] { 0f, 1f })
            cueIds.Add(CombatAudioRouter.ResolveImpactCue(source, target, CombatImpactMaterial.Default, damage));
        int genericCueCount = cueIds.Count;
        foreach (string weapon in new[] { "generic", "knife", "axe", "pickaxe", "spear", "blunt", "bow", "crossbow", "sling", "blowpipe" })
            cueIds.Add($"combat.weapon.{weapon}.attack");
        foreach (string id in cueIds)
        {
            bool exists = catalog.TryGet(id, out AudioCue cue), validClips = exists;
            var clipIds = new HashSet<int>();
            if (exists)
                foreach (AudioClip clip in cue.Clips)
                    validClips &= clip != null && clip.channels == 1 && clip.frequency == 48000 && clip.samples > 0 && clipIds.Add(clip.GetInstanceID());
            cues.Add(new JObject { ["id"] = id, ["exists"] = exists, ["clipCount"] = cue != null ? cue.Clips.Count : 0,
                ["validClips"] = validClips, ["spatial"] = cue != null && cue.SpatialBlend == 1f,
                ["sfx"] = cue != null && cue.Bus == AudioBus.Sfx, ["loop"] = cue != null && cue.Loop });
        }
        return new JObject { ["genericCueCount"] = genericCueCount, ["damageDefinitions"] = damageDefinitions,
            ["healthDefinitions"] = healthDefinitions, ["targets"] = targets, ["cues"] = cues };
    }
    #endregion
}
