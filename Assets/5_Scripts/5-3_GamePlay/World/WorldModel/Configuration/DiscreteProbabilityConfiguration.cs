using System;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>JSON 原表、运行态修正和最终快照之间的统一入口。</summary>
public static partial class DiscreteProbabilityConfiguration
{
    #region 加载与修正管线
    private const string ResourcePath = "Config/Probability/discrete-probability-tables";

    [MemoryPack.MemoryPackable]
    private sealed partial class CatalogDefinition
    {
        public int SchemaVersion;
        public DiscreteProbabilityTableDefinition[] Tables;
    }

    private static CatalogDefinition source;
    private static DiscreteProbabilityCatalog current;
    private static DiscreteProbabilityModifier modifiers;
    private static long rulesVersion = -1;

    public static event DiscreteProbabilityModifier Modifying
    {
        add { modifiers += value; current = null; }
        remove { modifiers -= value; current = null; }
    }

    public static DiscreteProbabilityCatalog Capture()
    {
        if (rulesVersion != GameRuleConfiguration.Version)
        {
            rulesVersion = GameRuleConfiguration.Version;
            current = null;
        }
        if (current != null) return current;
        if (source == null)
            source = LoadSourceConfiguration();
        // 原表只读，所有外部修正都发生在构建最终快照之前。
        return current = new DiscreteProbabilityCatalog(source.Tables, modifiers);
    }

    [BuiltInContentBake]
    public static void BakeBuiltInConfiguration()
    {
        CatalogDefinition definition = LoadSourceConfiguration();
        _ = new DiscreteProbabilityCatalog(definition.Tables);
    }

    private static CatalogDefinition LoadSourceConfiguration()
    {
        CatalogDefinition loaded = BuiltInContentBinaryCatalog.ReadResource(ResourcePath, ParseResourceConfiguration);
        if (loaded == null)
            throw new MissingReferenceException("缺少 Config/Probability/discrete-probability-tables 概率配置。");
        if (loaded.SchemaVersion != 1 || loaded.Tables == null || loaded.Tables.Length == 0)
            throw new InvalidOperationException("离散概率目录格式无效。");
        return loaded;
    }

    private static CatalogDefinition ParseResourceConfiguration(string json)
        => JsonConfigurationCache.Deserialize<CatalogDefinition>(json, new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        }) ?? throw new InvalidOperationException("离散概率目录格式无效。");

    public static void Reload() { source = null; current = null; }

#if UNITY_EDITOR
    /// <summary>编辑器烘焙用独立基础概率目录，结束后恢复原来源、修正器与快照。</summary>
    public static IDisposable UseBuiltInConfigurationForBake()
    {
        CatalogDefinition builtIn = LoadSourceConfiguration();
        var previous = new ProbabilityConfigurationBakeScope(source, current, modifiers, rulesVersion);
        source = builtIn; current = null; modifiers = null; rulesVersion = -1;
        return previous;
    }

    private sealed class ProbabilityConfigurationBakeScope : IDisposable
    {
        private readonly CatalogDefinition previousSource;
        private readonly DiscreteProbabilityCatalog previousCurrent;
        private readonly DiscreteProbabilityModifier previousModifiers;
        private readonly long previousVersion;
        public ProbabilityConfigurationBakeScope(CatalogDefinition source, DiscreteProbabilityCatalog current,
            DiscreteProbabilityModifier modifiers, long version)
        { previousSource = source; previousCurrent = current; previousModifiers = modifiers; previousVersion = version; }
        public void Dispose()
        { source = previousSource; current = previousCurrent; modifiers = previousModifiers; rulesVersion = previousVersion; }
    }
#endif

    // 关闭域重载时也撤掉上轮会话快照，避免相同版本号复用旧修正。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeCache() { current = null; rulesVersion = -1; }
    #endregion
}
