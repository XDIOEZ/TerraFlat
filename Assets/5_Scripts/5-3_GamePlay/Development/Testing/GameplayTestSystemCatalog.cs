using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>系统清单记录代码归属、规则入口及真实场景缺口，供 AI 精确选择回归范围。</summary>
public static class GameplayTestSystemCatalog
{
    #region 系统索引
    public const string RulesSuitePath = "GameplayTests/all-systems-rules.json";
    public const string CatalogPath = "GameplayTests/systems.catalog.json";

    public static JObject Read()
    {
        string path = GameplayTestSelection.GetAbsoluteSuitePath(CatalogPath);
        JObject catalog = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
        if (catalog.Value<int>("schemaVersion") != 1 || catalog["systems"] is not JArray systems || systems.Count == 0)
            throw new InvalidDataException("系统索引版本或 systems 列表无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JObject system in systems)
        {
            string id = system.Value<string>("id");
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || system["ruleCaseIds"] is not JArray ruleCases || ruleCases.Count == 0)
                throw new InvalidDataException("系统索引 ID 重复或缺少规则案例：" + id);
            GameplayTestSelection.NormalizeSuitePath(system.Value<string>("rulesSuite"));
        }
        return catalog;
    }

    public static IReadOnlyList<string> SelectRuleCases(IEnumerable<string> selectedSystems)
    {
        var selected = new HashSet<string>(selectedSystems ?? throw new ArgumentNullException(nameof(selectedSystems)), StringComparer.Ordinal);
        if (selected.Count == 0) throw new ArgumentException("至少选择一个系统。");
        var cases = new List<string>();
        foreach (JObject system in (JArray)Read()["systems"])
            if (selected.Remove(system.Value<string>("id")))
                foreach (JToken caseId in (JArray)system["ruleCaseIds"])
                {
                    if (caseId.Type != JTokenType.String) throw new InvalidDataException("ruleCaseIds 只能包含文本 ID。");
                    cases.Add(caseId.Value<string>());
                }
        if (selected.Count != 0) throw new ArgumentException("未登记的测试系统：" + string.Join(", ", selected));
        return cases;
    }
    #endregion
}
