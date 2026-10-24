using System;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>JSON 原表、运行态修正和最终快照之间的统一入口。</summary>
public static partial class DiscreteProbabilityConfiguration
{
    #region 加载与修正管线
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
        {
            TextAsset asset = Resources.Load<TextAsset>("Config/Probability/discrete-probability-tables");
            if (asset == null) throw new MissingReferenceException("缺少 Config/Probability/discrete-probability-tables 概率配置。");
            source = JsonConfigurationCache.Deserialize<CatalogDefinition>(asset.text, new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            });
            if (source == null || source.SchemaVersion != 1 || source.Tables == null || source.Tables.Length == 0)
                throw new InvalidOperationException("离散概率目录格式无效。");
        }
        // 原表只读，所有外部修正都发生在构建最终快照之前。
        return current = new DiscreteProbabilityCatalog(source.Tables, modifiers);
    }

    public static void Reload() { source = null; current = null; }

    // 关闭域重载时也撤掉上轮会话快照，避免相同版本号复用旧修正。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeCache() { current = null; rulesVersion = -1; }
    #endregion
}
