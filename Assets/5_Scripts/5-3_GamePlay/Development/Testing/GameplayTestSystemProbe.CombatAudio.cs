using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 真实命中选音与树木定义采样

    private static void RegisterCombatAudioOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("audio/combat_impact", EntityCombatImpactAudio);
        operations.Add("audio/tree_impact_configuration", EntityTreeImpactConfiguration);
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
            Text(args, "targetCue", ""), Text(args, "weaponCue", ""), Flag(args, "blocked", false));
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
                bool blocked = module.Parameters?.Value<bool?>("playHurtAudioOnBlockedHit") ?? false;
                CombatImpactMaterial material = CombatAudioRouter.ResolveImpactMaterial(new Data_GeneralItem
                    { IDName = definition.Id, GameName = definition.GameName, Tags = definition.Tags },
                    (CombatImpactMaterial)(module.Parameters?.Value<int?>("impactAudioMaterial") ?? 0));
                trees.Add(new JObject
                {
                    ["id"] = definition.Id,
                    ["cue"] = cue ?? string.Empty,
                    ["blocked"] = blocked,
                    ["material"] = material.ToString(),
                    ["normalCue"] = CombatAudioRouter.ResolveImpactCue(CombatWeaponAudioClass.Generic,
                        material, 1f, cue, playOnBlockedHit: blocked) ?? string.Empty,
                    ["axeCue"] = CombatAudioRouter.ResolveImpactCue(CombatWeaponAudioClass.Axe,
                        material, 1f, cue, playOnBlockedHit: blocked) ?? string.Empty,
                    ["blockedCue"] = CombatAudioRouter.ResolveImpactCue(CombatWeaponAudioClass.Axe,
                        material, 0f, cue, playOnBlockedHit: blocked) ?? string.Empty
                });
            }
        }
        return new JObject { ["trees"] = trees };
    }

    #endregion
}
