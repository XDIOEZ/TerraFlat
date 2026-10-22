using System;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>JSON 原表、运行态修正和最终快照之间的统一入口。</summary>
public static class DiscreteProbabilityConfiguration
{
    #region 加载与修正管线
    private sealed class CatalogDefinition
    {
        public int SchemaVersion;
        public DiscreteProbabilityTableDefinition[] Tables;
    }

    private static CatalogDefinition source;
    private static DiscreteProbabilityCatalog current;
    private static DiscreteProbabilityModifier modifiers;
    private static object difficultyRules;

    public static event DiscreteProbabilityModifier Modifying
    {
        add { modifiers += value; current = null; }
        remove { modifiers -= value; current = null; }
    }

    public static DiscreteProbabilityCatalog Capture()
    {
        var activeDifficulty = GameDifficultyService.Current;
        if (!ReferenceEquals(difficultyRules, activeDifficulty))
        {
            difficultyRules = activeDifficulty;
            current = null;
        }
        if (current != null) return current;
        if (source == null)
        {
            TextAsset asset = Resources.Load<TextAsset>("Config/Probability/discrete-probability-tables");
            if (asset == null) throw new MissingReferenceException("缺少 Config/Probability/discrete-probability-tables 概率配置。");
            source = JsonConvert.DeserializeObject<CatalogDefinition>(asset.text, new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            });
            if (source == null || source.SchemaVersion != 1 || source.Tables == null || source.Tables.Length == 0)
                throw new InvalidOperationException("离散概率目录格式无效。");
        }
        // 原表只读，所有难度及扩展修正都发生在构建最终快照之前。
        return current = new DiscreteProbabilityCatalog(source.Tables, modifiers);
    }

    public static void Reload() { source = null; current = null; }
    #endregion
}
