using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>游戏数据初始化时编译并复用行为树模板，出生时不再解析 JSON。</summary>
public static class AnimalBehaviorTreeNodeCatalog
{
    #region 节点注册与模板缓存
    private const int BakedTreeVersion = 1;
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
        if (!BuiltInContentBinaryCatalog.IsRecording || registered)
            return CompileWithInitializedNodeFactories(definition);

        Action restore = AnimalBehaviorTreeRegistry.BeginIsolatedFactoryRegistrations();
        try { return CompileWithInitializedNodeFactories(definition); }
        finally { registered = false; restore(); }
    }

    private static AnimalBehaviorTreeTemplate CompileWithInitializedNodeFactories(AnimalBehaviorTreeDefinition definition)
    {
        EnsureRegistered();
        if (definition == null) throw new InvalidOperationException("生物缺少行为树定义。");
        // 共享库在发布时统一失效，重复校验只序列化当前物种，不再遍历整个共享库。
        string key = JsonConvert.SerializeObject(definition);
        if (!BuiltInContentBinaryCatalog.IsRecording && templates.TryGetValue(key, out AnimalBehaviorTreeTemplate cached)) return cached;
        AnimalBehaviorTreeDefinition resolved = ResolveSharedSubtrees(definition);
        if (BuiltInContentBinaryCatalog.IsRecording)
            return BakeResolvedTree(definition, resolved, key);
        AnimalBehaviorTreeTemplate template = BuiltInContentBinaryCatalog.IsActive &&
            BuiltInContentBinaryCatalog.TryRead(GetBakedTreeKey(definition, resolved, key), BakedTreeVersion, out BakedAnimalBehaviorTree baked)
                ? AnimalBehaviorTreeCompiler.BindBakedTree(baked) : AnimalBehaviorTreeCompiler.Compile(resolved);
        templates[key] = template;
        return template;
    }

#if UNITY_EDITOR
    /// <summary>从独立本体 DTO 收集所有实际 Actor 使用的行为树。</summary>
    [BuiltInContentBake]
    public static void BakeBuiltInAnimalBehaviorTreeDefinitions()
    {
        foreach (ItemDefinitionDto actor in ActorDefinitionCatalogLoader.LoadBuiltInDefinitionsForBake())
        {
            if (actor.Abstract || actor.Modules == null) continue;
            foreach (ItemModuleDefinitionDto module in actor.Modules.Values)
                if (module?.Parameters?["behaviorTree"] is JObject tree)
                    Bake(tree.ToObject<AnimalBehaviorTreeDefinition>());
        }
    }
#endif

    /// <summary>在 Editor 同步烘焙当前定义和共享库所引用的纯行为树。</summary>
    public static void Bake(AnimalBehaviorTreeDefinition definition)
    {
        if (!BuiltInContentBinaryCatalog.IsRecording)
            throw new InvalidOperationException("行为树烘焙需要先开始二进制目录录制。");
        Compile(definition);
    }

    private static AnimalBehaviorTreeTemplate BakeResolvedTree(AnimalBehaviorTreeDefinition definition,
        AnimalBehaviorTreeDefinition resolved, string serializedDefinition)
    {
        BakedAnimalBehaviorTree baked = AnimalBehaviorTreeCompiler.CreateBakedTree(resolved);
        AnimalBehaviorTreeTemplate template = AnimalBehaviorTreeCompiler.BindBakedTree(baked);
        BuiltInContentBinaryCatalog.Record(GetBakedTreeKey(definition, resolved, serializedDefinition), BakedTreeVersion, baked);
        return template;
    }

    private static AnimalBehaviorTreeDefinition ResolveSharedSubtrees(AnimalBehaviorTreeDefinition definition)
    {
        var subtrees = new Dictionary<string, AnimalBehaviorSubtreeDefinition>(sharedSubtrees, StringComparer.Ordinal);
        if (definition.Subtrees != null)
            foreach (var pair in definition.Subtrees) subtrees[pair.Key] = pair.Value;
        return new AnimalBehaviorTreeDefinition
        {
            SchemaVersion = definition.SchemaVersion, Root = definition.Root,
            Capabilities = definition.Capabilities, Subtrees = subtrees
        };
    }

    private static string GetBakedTreeKey(AnimalBehaviorTreeDefinition definition,
        AnimalBehaviorTreeDefinition resolved, string serializedDefinition)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var shared = new SortedDictionary<string, AnimalBehaviorSubtreeDefinition>(StringComparer.Ordinal);
        void Visit(AnimalBehaviorTreeNodeDefinition node)
        {
            if (node == null) return;
            if (node.Type == "subtree" && node.Name != null && visited.Add(node.Name) &&
                resolved.Subtrees.TryGetValue(node.Name, out AnimalBehaviorSubtreeDefinition subtree))
            {
                if (definition.Subtrees == null || !definition.Subtrees.ContainsKey(node.Name)) shared.Add(node.Name, subtree);
                Visit(subtree.Root);
            }
            if (node.Children != null) foreach (var child in node.Children) Visit(child);
        }
        Visit(definition.Root);
        string source = serializedDefinition + "\n" + JsonConvert.SerializeObject(shared);
        using var hash = SHA256.Create();
        return "animal-behavior-tree/" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(source))).Replace("-", "");
    }

    private static void EnsureRegistered()
    {
        if (registered) return;
        Action restore = AnimalBehaviorTreeRegistry.BeginIsolatedFactoryRegistrations();
        try { RegisterBuiltInNodeFactories(); registered = true; }
        catch { restore(); throw; }
    }

    private static void RegisterBuiltInNodeFactories()
    {
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
        ServitorBehaviorNodes.Register();
    }
    #endregion
}
