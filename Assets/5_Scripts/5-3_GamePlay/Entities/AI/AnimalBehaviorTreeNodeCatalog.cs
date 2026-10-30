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
        // 共享库换代只替换本会话缓存，不能清空热更新上下文仍持有的正式模板。
        templates = new(StringComparer.Ordinal);
    }

    /// <summary>返回只读目录的独立快照，外部修改不能绕过共享库发布与模板失效。</summary>
    public static AnimalBehaviorSubtreeDefinition GetSharedSubtree(string name)
        => name != null && sharedSubtrees.TryGetValue(name, out AnimalBehaviorSubtreeDefinition subtree)
            ? JObject.FromObject(subtree).ToObject<AnimalBehaviorSubtreeDefinition>() : null;

    public static AnimalBehaviorTreeTemplate Compile(AnimalBehaviorTreeDefinition definition)
    {
        EnsureRegistered();
        if (definition == null) throw new InvalidOperationException("生物缺少行为树定义。");
        // 共享库在发布时统一失效，重复校验只序列化当前物种，不再遍历整个共享库。
        string key = JsonConvert.SerializeObject(definition);
        if (templates.TryGetValue(key, out AnimalBehaviorTreeTemplate cached)) return cached;
        var subtrees = new Dictionary<string, AnimalBehaviorSubtreeDefinition>(sharedSubtrees, StringComparer.Ordinal);
        if (definition.Subtrees != null)
            foreach (var pair in definition.Subtrees) subtrees[pair.Key] = pair.Value;
        var resolved = new AnimalBehaviorTreeDefinition
        {
            SchemaVersion = definition.SchemaVersion, Root = definition.Root,
            Capabilities = definition.Capabilities, Subtrees = subtrees
        };
        AnimalBehaviorTreeTemplate template = AnimalBehaviorTreeCompiler.Compile(resolved);
        templates.Add(key, template);
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
