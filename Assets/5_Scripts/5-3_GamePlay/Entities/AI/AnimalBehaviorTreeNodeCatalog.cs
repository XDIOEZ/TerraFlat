using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>游戏数据初始化时编译并复用行为树模板，出生时不再解析 JSON。</summary>
public static class AnimalBehaviorTreeNodeCatalog
{
    #region 节点注册与模板缓存
    private static bool registered;
    private static Dictionary<string, AnimalBehaviorTreeTemplate> templates = new(StringComparer.Ordinal);
    private static Dictionary<string, AnimalBehaviorSubtreeDefinition> sharedSubtrees = new(StringComparer.Ordinal);

    internal static void ConfigureResourceReload(ResourceReloadContext context)
    {
        context.AddDictionary(() => templates, value => templates = value);
        context.AddDictionary(() => sharedSubtrees, value => sharedSubtrees = value);
    }

    public static void SetSharedSubtrees(JObject source)
    {
        sharedSubtrees = source?.ToObject<Dictionary<string, AnimalBehaviorSubtreeDefinition>>() ?? new(StringComparer.Ordinal);
    }

    public static AnimalBehaviorSubtreeDefinition GetSharedSubtree(string name)
        => name != null && sharedSubtrees.TryGetValue(name, out AnimalBehaviorSubtreeDefinition subtree) ? subtree : null;

    public static AnimalBehaviorTreeTemplate Compile(AnimalBehaviorTreeDefinition definition)
    {
        EnsureRegistered();
        if (definition == null) throw new InvalidOperationException("生物缺少行为树定义。");
        var subtrees = new Dictionary<string, AnimalBehaviorSubtreeDefinition>(sharedSubtrees, StringComparer.Ordinal);
        if (definition.Subtrees != null)
            foreach (var pair in definition.Subtrees) subtrees[pair.Key] = pair.Value;
        var resolved = new AnimalBehaviorTreeDefinition
        {
            SchemaVersion = definition.SchemaVersion, Root = definition.Root,
            Capabilities = definition.Capabilities, Subtrees = subtrees
        };
        string key = JsonConvert.SerializeObject(resolved);
        if (!templates.TryGetValue(key, out AnimalBehaviorTreeTemplate template))
        {
            template = AnimalBehaviorTreeCompiler.Compile(resolved);
            templates.Add(key, template);
        }
        return template;
    }

    private static void EnsureRegistered()
    {
        if (registered) return;
        registered = true;
        AnimalBehaviorTreeRegistry.RegisterCondition("always", (_, _) => () => true);
        AnimalBehaviorTreeRegistry.RegisterCondition("healthBelow", (c, p) =>
        {
            float ratio = p.Value<float?>("ratio") ?? .25f;
            return () => c.Health != null && c.Health.Hp / Mathf.Max(1f, c.Health.MaxHp) < ratio;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("healthAbove", (c, p) =>
        {
            float ratio = p.Value<float?>("ratio") ?? .5f;
            return () => c.Health != null && c.Health.Hp / Mathf.Max(1f, c.Health.MaxHp) > ratio;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("recentDamage", (c, _) => () => c.HasRecentDamageThreat);
        AnimalBehaviorTreeRegistry.RegisterCondition("recentDamageExpired", (c, _) => () => !c.HasRecentDamageThreat);
        AnimalBehaviorTreeRegistry.RegisterCondition("timerElapsed", (c, p) =>
        {
            string key = p.Value<string>("key");
            return () => c.IsTimerElapsed(key);
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("stateElapsedAtLeast", (c, p) =>
        {
            float seconds = p.Value<float?>("seconds") ?? 0f;
            return () => c.StateElapsed >= seconds;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("isNight", (c, p) => () =>
            c.IsNightTime(p.Value<float?>("dayStartRatio") ?? .25f, p.Value<float?>("dayEndRatio") ?? .75f));
        AnimalBehaviorTreeRegistry.RegisterCondition("isDay", (c, p) => () =>
            !c.IsNightTime(p.Value<float?>("dayStartRatio") ?? .25f, p.Value<float?>("dayEndRatio") ?? .75f));
        AnimalLandBehaviorNodes.Register();
        AnimalSpecialMovementBehaviorNodes.Register();
        AnimalTemperatureSafetyBehaviorNodes.Register();
    }
    #endregion
}
