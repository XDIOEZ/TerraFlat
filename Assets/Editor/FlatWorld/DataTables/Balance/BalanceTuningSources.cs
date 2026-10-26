using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>扫描并写入 JSON 物品定义或旧 Prefab；只暴露实际可编辑的数值，不修改运行态职责。</summary>
internal static class BalanceTuningSources
{
    private const string ItemShellRoot = "Assets/StreamingAssets/GameConfig/Items/shells";
    private const string PrefabRoot = "Assets/2_Prefabs";

    private sealed class ItemJsonSource
    {
        internal string Path;
        internal JObject Definition;
    }

    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Physical"] = "物理数值", ["Cutting"] = "切割", ["Piercing"] = "穿刺",
        ["Chopping"] = "劈砍", ["Blunt"] = "钝击", ["MaxHp"] = "最大生命",
        ["Hp"] = "生命", ["Carbohydrates"] = "碳水", ["Fat"] = "脂肪",
        ["Protein"] = "蛋白质", ["Water"] = "水分", ["Vitamins"] = "维生素",
        ["Max_EatingProgress"] = "食用次数", ["nutritionConsumeSpeed"] = "营养消耗速度",
        ["SpoilageIntervalSeconds"] = "腐败间隔", ["EnableSpoilage"] = "启用腐败",
        ["SpoilageTargetItemID"] = "腐败后物品", ["DamageInterval"] = "伤害间隔",
        ["staminaCostPerAttack"] = "攻击体力消耗", ["attackSpeedMultiplier"] = "攻击速度",
        ["weight"] = "重量", ["volume"] = "体积", ["durability"] = "耐久",
        ["maxDurability"] = "最大耐久", ["DefenseBonus"] = "护甲防御",
        ["DamageValues"] = "攻击力"
    };

    /// <summary>首次迁移旧数值表，保留原表的修改值而不销毁其序列化资源。</summary>
    internal static void ImportLegacyIfNecessary(BalanceTuningDatabase database)
    {
        if (database.LegacyImported) return;
        var rows = new List<BalanceTuningRecord>();
        PrefabStatTableConfig prefab = AssetDatabase.LoadAssetAtPath<PrefabStatTableConfig>(
            "Assets/Editor/FlatWorld/DataTables/Prefab/PrefabStatTableConfig.asset");
        if (prefab?.Rows != null)
        {
            foreach (PrefabStatTableRow old in prefab.Rows.Where(x => x != null))
            {
                if (old.HasHp)
                {
                    AddPrefab(rows, old.PrefabPath, old.PrefabName, "Health.MaxHp", "最大生命", "战斗", old.MaxHp);
                    AddPrefab(rows, old.PrefabPath, old.PrefabName, "Health.Hp", "生命", "战斗", old.Hp);
                }
                if (old.HasDefense)
                {
                    AddPrefabDefense(rows, old.PrefabPath, old.PrefabName,
                        old.CuttingDefense, old.PiercingDefense, old.ChoppingDefense, old.BluntDefense);
                }
                if (old.HasDamage)
                {
                    AddPrefabDamage(rows, old.PrefabPath, old.PrefabName,
                        old.CuttingDamage, old.PiercingDamage, old.ChoppingDamage, old.BluntDamage);
                }
            }
        }
        FoodStatTableConfig food = AssetDatabase.LoadAssetAtPath<FoodStatTableConfig>(
            "Assets/Editor/FlatWorld/DataTables/Food/FoodStatTableConfig.asset");
        if (food?.Rows != null)
        {
            foreach (FoodStatTableRow old in food.Rows.Where(x => x != null))
            {
                AddPrefabNutrition(rows, old.PrefabPath, old.PrefabName, old);
            }
        }
        // 旧表格可能保存了尚未应用到 Prefab 的值；首次扫描不得把这些编辑成果冲掉。
        foreach (BalanceTuningRecord record in rows)
            record.IsPendingPrefabSave = true;
        database.Merge(rows);
        database.MarkLegacyImported();
    }

    /// <summary>仅点击“重新扫描”才遍历资源；已调数值与历史不会因为扫描丢失。</summary>
    internal static int ScanInto(BalanceTuningDatabase database)
    {
        var scanned = new List<BalanceTuningRecord>();
        try
        {
            EditorUtility.DisplayProgressBar("数值工作台", "扫描 JSON 物品定义", 0.2f);
            ScanJson(scanned);
            EditorUtility.DisplayProgressBar("数值工作台", "扫描旧 Prefab 数值", 0.6f);
            ScanPrefabs(scanned);
            database.Merge(scanned, replaceValues: true);
            database.MarkCatalogScanned();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        return scanned.Count;
    }

    #region JSON 定义

    /// <summary>解析子物品继承后的有效数值；编辑时只覆盖当前子定义。</summary>
    private static Dictionary<string, ItemJsonSource> LoadJsonDefinitions()
    {
        var definitions = new Dictionary<string, ItemJsonSource>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(ItemShellRoot)) return definitions;
        foreach (string path in Directory.GetFiles(ItemShellRoot, "*.json", SearchOption.AllDirectories))
        {
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception exception)
            {
                Debug.LogWarning($"[数值工作台] 无法解析 {path}: {exception.Message}");
                continue;
            }
            if (root["items"] is not JArray items) continue;
            foreach (JObject item in items.OfType<JObject>())
            {
                string id = item.Value<string>("id");
                if (!string.IsNullOrEmpty(id))
                    definitions[id] = new ItemJsonSource { Path = path.Replace('\\', '/'), Definition = item };
            }
        }
        return definitions;
    }

    private static JObject Resolve(string id, Dictionary<string, ItemJsonSource> sources,
        Dictionary<string, JObject> cache, HashSet<string> visiting)
    {
        if (cache.TryGetValue(id, out JObject previous)) return previous;
        if (!sources.TryGetValue(id, out ItemJsonSource source)) return new JObject();
        if (!visiting.Add(id)) throw new InvalidOperationException($"循环的 JSON parent 继承：{id}");
        string parentId = source.Definition.Value<string>("parent");
        JObject result = !string.IsNullOrEmpty(parentId) && sources.ContainsKey(parentId)
            ? (JObject)Resolve(parentId, sources, cache, visiting).DeepClone() : new JObject();
        result.Merge(source.Definition, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
        visiting.Remove(id);
        cache.Add(id, result);
        return result;
    }

    private static void ScanJson(List<BalanceTuningRecord> scanned)
    {
        Dictionary<string, ItemJsonSource> sources = LoadJsonDefinitions();
        var cache = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, ItemJsonSource> pair in sources)
        {
            if (pair.Value.Definition.Value<bool?>("abstract") == true) continue;
            JObject definition;
            try { definition = Resolve(pair.Key, sources, cache, new HashSet<string>(StringComparer.OrdinalIgnoreCase)); }
            catch (Exception exception)
            {
                Debug.LogWarning($"[数值工作台] 跳过 {pair.Key}: {exception.Message}");
                continue;
            }
            string groupName = definition.Value<string>("gameName") ?? pair.Key;
            foreach (string stat in new[] { "weight", "volume", "durability", "maxDurability" })
                if (definition[stat] is JValue property && IsNumber(property))
                    AddJson(scanned, pair.Value.Path, pair.Key, groupName, property, "通用");
            if (definition["modules"] is not JObject modules) continue;
            foreach (JProperty moduleProperty in modules.Properties())
            {
                if (moduleProperty.Value is not JObject module) continue;
                foreach (string sectionName in new[] { "parameters", "data" })
                {
                    if (module[sectionName] is not JObject section) continue;
                    CollectJsonFields(scanned, pair.Value.Path, pair.Key, groupName,
                        section, InferCategory(definition, moduleProperty.Name), depth: 0);
                }
            }
        }
    }

    /// <summary>排除编辑器表现/Transform/快照身份字段，让工作台只呈现可调玩法参数。</summary>
    private static void CollectJsonFields(List<BalanceTuningRecord> result, string path, string id,
        string name, JToken token, string category, int depth)
    {
        if (depth > 12) return;
        if (token is JObject obj)
        {
            foreach (JProperty prop in obj.Properties())
            {
                if (prop.Name.StartsWith("$", StringComparison.Ordinal) ||
                    prop.Name is "BitData" or "ModuleId" or "Type" or "ImpactKind" or "CoveredParts" or
                        "localPosition" or "localScale" or "localEulerAngles" or "rendererPath" or
                        "spriteAddress" or "color" or "collider" or "spriteStates" or
                        "ItemSpecialData" or "position" or "rotation" or "scale")
                    continue;
                CollectJsonFields(result, path, id, name, prop.Value, category, depth + 1);
            }
        }
        else if (token is JArray array)
        {
            for (int index = 0; index < array.Count; index++)
                CollectJsonFields(result, path, id, name, array[index], category, depth + 1);
        }
        else if (token is JValue value && (IsNumber(value) ||
            (value.Type == JTokenType.Boolean && token.Path.EndsWith("EnableSpoilage", StringComparison.OrdinalIgnoreCase)) ||
            (value.Type == JTokenType.String && token.Path.EndsWith("SpoilageTargetItemID", StringComparison.OrdinalIgnoreCase))))
        {
            AddJson(result, path, id, name, value, category);
        }
    }

    private static bool IsNumber(JValue value) =>
        value.Type == JTokenType.Integer || value.Type == JTokenType.Float;

    private static string InferCategory(JObject definition, string module)
    {
        if (module.IndexOf("food", StringComparison.OrdinalIgnoreCase) >= 0) return "食物";
        if (module.IndexOf("equipment", StringComparison.OrdinalIgnoreCase) >= 0 ||
            module.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0) return "护甲";
        if (module.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0 ||
            module.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0) return "武器";
        return "通用";
    }

    private static void AddJson(List<BalanceTuningRecord> result, string sourcePath, string id,
        string name, JValue value, string category)
    {
        string field = value.Path;
        string label = DescribeField(field, category);
        BalanceTuningValueKind kind = value.Type switch
        {
            JTokenType.Boolean => BalanceTuningValueKind.Toggle,
            JTokenType.String => BalanceTuningValueKind.Text,
            _ => BalanceTuningValueKind.Number
        };
        float number = kind == BalanceTuningValueKind.Number ? value.Value<float>() : 0f;
        result.Add(new BalanceTuningRecord
        {
            Key = $"json|{sourcePath}|{id}|{field}",
            GroupKey = $"json|{id}",
            Name = name,
            DefinitionId = id,
            SourcePath = sourcePath,
            FieldPath = field,
            Label = label,
            Category = category,
            SourceKind = BalanceTuningSourceKind.JsonDefinition,
            ValueKind = kind,
            Value = number,
            ToggleValue = kind == BalanceTuningValueKind.Toggle && value.Value<bool>(),
            TextValue = kind == BalanceTuningValueKind.Text ? value.Value<string>() : string.Empty,
            Step = BalanceTuningRecord.DefaultStep(number, field),
            Min = number < 0f || field.IndexOf("Increase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  field.IndexOf("Override", StringComparison.OrdinalIgnoreCase) >= 0 ? -1000000f : 0f,
            Max = 1000000f
        });
    }

    /// <summary>用可读的中文字段名区分同一物品的多组模块属性。</summary>
    private static string DescribeField(string path, string category)
    {
        string[] tokens = Regex.Split(path, @"[.\[\]]+");
        string last = tokens.Length > 0 ? tokens[tokens.Length - 1] : path;
        if (string.IsNullOrEmpty(last) && tokens.Length > 1) last = tokens[tokens.Length - 2];
        string label = FieldLabels.TryGetValue(last, out string translated) ? translated : last;
        if (path.Contains("DefenseBonus")) return "护甲 · " + label;
        if (path.Contains("DamageValues")) return "伤害 · " + label;
        if (path.Contains("nutrition")) return "营养 · " + label;
        if (path.Contains("equipmentInstances[")) return "装备效果 · " + label;
        return label;
    }

    /// <summary>写回当前定义，继承字段只覆盖本物品，不污染父模板及其他物品。</summary>
    internal static bool SaveJsonField(BalanceTuningRecord row, out string error)
    {
        error = null;
        try
        {
            if (!File.Exists(row.SourcePath)) throw new FileNotFoundException(row.SourcePath);
            JObject root = JObject.Parse(File.ReadAllText(row.SourcePath));
            JObject item = (root["items"] as JArray)?.OfType<JObject>()
                .FirstOrDefault(x => x.Value<string>("id") == row.DefinitionId);
            if (item == null) throw new InvalidOperationException($"找不到物品定义：{row.DefinitionId}");
            Dictionary<string, ItemJsonSource> all = LoadJsonDefinitions();
            JObject effective = Resolve(row.DefinitionId, all, new Dictionary<string, JObject>(
                StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (effective.SelectToken(row.FieldPath, errorWhenNoMatch: false) == null)
                throw new InvalidOperationException($"字段已在源配置中消失，请重新扫描：{row.FieldPath}");
            JToken newValue = row.ValueKind switch
            {
                BalanceTuningValueKind.Toggle => new JValue(row.ToggleValue),
                BalanceTuningValueKind.Text => new JValue(row.TextValue ?? string.Empty),
                _ => new JValue(row.Value)
            };
            SetJsonPath(item, effective, row.FieldPath, newValue);
            // JSON.NET 正确序列化对象、数组和转义，防止按文本替换写坏资源配置。
            File.WriteAllText(row.SourcePath, root.ToString(Formatting.Indented) + Environment.NewLine,
                new UTF8Encoding(false));
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static void SetJsonPath(JObject target, JObject effective, string path, JToken value)
    {
        MatchCollection segments = Regex.Matches(path, @"[^.\[\]]+|\[\d+\]");
        JToken current = target, inherited = effective;
        for (int i = 0; i < segments.Count; i++)
        {
            string token = segments[i].Value;
            bool last = i == segments.Count - 1;
            if (token.StartsWith("[", StringComparison.Ordinal))
            {
                int index = int.Parse(token.Trim('[', ']'));
                if (current is not JArray array || index < 0 || index >= array.Count)
                    throw new InvalidOperationException($"数组索引已变化：{path}");
                if (last) array[index] = value;
                else { current = array[index]; inherited = (inherited as JArray)?[index]; }
                continue;
            }
            if (current is not JObject obj) throw new InvalidOperationException($"字段路径已变化：{path}");
            if (last) { obj[token] = value; break; }
            JToken inheritedNext = (inherited as JObject)?[token];
            if (obj[token] == null)
            {
                bool nextIsArray = segments[i + 1].Value.StartsWith("[", StringComparison.Ordinal);
                obj[token] = nextIsArray
                    ? inheritedNext?.DeepClone() ?? new JArray()
                    : new JObject();
            }
            current = obj[token];
            inherited = inheritedNext;
        }
    }

    #endregion

    #region Prefab 数据

    private static void AddPrefab(List<BalanceTuningRecord> result, string path, string name,
        string field, string label, string category, float value)
    {
        if (string.IsNullOrEmpty(path)) return;
        result.Add(new BalanceTuningRecord
        {
            Key = $"prefab|{path}|{field}", GroupKey = $"prefab|{path}",
            Name = name, SourcePath = path, FieldPath = field, Label = label,
            Category = category, SourceKind = BalanceTuningSourceKind.Prefab,
            Value = value, Step = BalanceTuningRecord.DefaultStep(value, field),
            Min = field == "Food.Max_EatingProgress" ? 1f : 0f, Max = 1000000f
        });
    }

    private static void AddPrefabDamage(List<BalanceTuningRecord> list, string path, string name,
        float cutting, float piercing, float chopping, float blunt)
    {
        AddPrefab(list, path, name, "Damage.Cutting", "伤害 · 切割", "武器", cutting);
        AddPrefab(list, path, name, "Damage.Piercing", "伤害 · 穿刺", "武器", piercing);
        AddPrefab(list, path, name, "Damage.Chopping", "伤害 · 劈砍", "武器", chopping);
        AddPrefab(list, path, name, "Damage.Blunt", "伤害 · 钝击", "武器", blunt);
    }

    private static void AddPrefabDefense(List<BalanceTuningRecord> list, string path, string name,
        float cutting, float piercing, float chopping, float blunt)
    {
        AddPrefab(list, path, name, "Defense.Cutting", "防御 · 切割", "护甲", cutting);
        AddPrefab(list, path, name, "Defense.Piercing", "防御 · 穿刺", "护甲", piercing);
        AddPrefab(list, path, name, "Defense.Chopping", "防御 · 劈砍", "护甲", chopping);
        AddPrefab(list, path, name, "Defense.Blunt", "防御 · 钝击", "护甲", blunt);
    }

    private static void AddPrefabNutrition(List<BalanceTuningRecord> list, string path, string name,
        FoodStatTableRow old)
    {
        if (old.HasNutrition)
        {
            foreach ((string property, float value) in new[]
            {
                ("Carbohydrates", old.Carbohydrates), ("Fat", old.Fat), ("Protein", old.Protein),
                ("Water", old.Water), ("Vitamins", old.Vitamins), ("Max_Carbohydrates", old.Max_Carbohydrates),
                ("Max_Fat", old.Max_Fat), ("Max_Protein", old.Max_Protein),
                ("Max_Water", old.Max_Water), ("Max_Vitamins", old.Max_Vitamins)
            })
                AddPrefab(list, path, name, "Food.nutrition." + property, "营养 · " + property, "食物", value);
        }
        AddPrefab(list, path, name, "Food.Max_EatingProgress", "食用次数", "食物", old.Max_EatingProgress);
        AddPrefab(list, path, name, "Food.nutritionConsumeSpeed", "营养消耗速度", "食物", old.nutritionConsumeSpeed);
        AddPrefab(list, path, name, "Food.WaterConsumeSpeedRate", "水消耗倍率", "食物", old.WaterConsumeSpeedRate);
        AddPrefab(list, path, name, "Food.nutritionConsumeRate", "营养消耗倍率", "食物", old.nutritionConsumeRate);
        AddPrefab(list, path, name, "Food.SpoilageIntervalSeconds", "腐败间隔", "食物", old.SpoilageIntervalSeconds);
        AddPrefabTyped(list, path, name, "Food.EnableSpoilage", "启用腐败",
            BalanceTuningValueKind.Toggle, old.EnableSpoilage, null);
        AddPrefabTyped(list, path, name, "Food.SpoilageTargetItemID", "腐败后物品",
            BalanceTuningValueKind.Text, false, old.SpoilageTargetItemID);
    }

    private static void AddPrefabTyped(List<BalanceTuningRecord> list, string path, string name,
        string field, string label, BalanceTuningValueKind kind, bool toggle, string text)
    {
        AddPrefab(list, path, name, field, label, "食物", 0f);
        if (string.IsNullOrEmpty(path)) return;
        BalanceTuningRecord record = list[list.Count - 1];
        record.ValueKind = kind;
        record.ToggleValue = toggle;
        record.TextValue = text;
    }

    private static void ScanPrefabs(List<BalanceTuningRecord> result)
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
        for (int prefabIndex = 0; prefabIndex < prefabGuids.Length; prefabIndex++)
        {
            string guid = prefabGuids[prefabIndex];
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (prefabIndex % 100 == 0)
                EditorUtility.DisplayProgressBar("数值工作台",
                    $"扫描 Prefab {prefabIndex + 1}/{prefabGuids.Length}",
                    0.6f + (prefabIndex / (float)Mathf.Max(1, prefabGuids.Length)) * 0.4f);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            string name = prefab.name;
            Mod_DamageReceiver receiver = prefab.GetComponentInChildren<Mod_DamageReceiver>(true);
            if (receiver?.Data != null)
            {
                AddPrefab(result, path, name, "Health.MaxHp", "最大生命", "战斗", receiver.Data.MaxHp);
                AddPrefab(result, path, name, "Health.Hp", "生命", "战斗", receiver.Data.Hp);
                CombatDefense defense = receiver.Defense;
                if (defense != null)
                    AddPrefabDefense(result, path, name, defense.Cutting, defense.Piercing, defense.Chopping, defense.Blunt);
            }
            Mod_Damage damage = prefab.GetComponentInChildren<Mod_Damage>(true);
            if (damage != null)
            {
                CombatDamage values = damage.ResolveDamageValues();
                AddPrefabDamage(result, path, name, values.Cutting, values.Piercing, values.Chopping, values.Blunt);
            }
            Mod_Food food = prefab.GetComponentInChildren<Mod_Food>(true);
            if (food != null && food.FoodModData != null)
            {
                Food data = food.FoodModData.FoodData;
                if (data?.nutrition == null) continue;
                Nutrition nutrition = data.nutrition;
                FoodSpoilageObserverData spoilage = FoodSpoilageObserverData.Load(food.FoodModData);
                AddPrefabNutrition(result, path, name, new FoodStatTableRow
                {
                    HasNutrition = true,
                    Carbohydrates = nutrition.Carbohydrates, Fat = nutrition.Fat, Protein = nutrition.Protein,
                    Water = nutrition.Water, Vitamins = nutrition.Vitamins,
                    Max_Carbohydrates = nutrition.Max_Carbohydrates, Max_Fat = nutrition.Max_Fat,
                    Max_Protein = nutrition.Max_Protein, Max_Water = nutrition.Max_Water,
                    Max_Vitamins = nutrition.Max_Vitamins,
                    Max_EatingProgress = data.Max_EatingProgress,
                    nutritionConsumeSpeed = data.nutritionConsumeSpeed?.Value ?? 0f,
                    WaterConsumeSpeedRate = data.WaterConsumeSpeedRate,
                    nutritionConsumeRate = data.nutritionConsumeRate,
                    EnableSpoilage = spoilage.EnableSpoilage,
                    SpoilageIntervalSeconds = spoilage.SpoilageIntervalSeconds,
                    SpoilageTargetItemID = spoilage.SpoilageTargetItemID
                });
            }
        }
    }

    /// <summary>仅保存选中 Prefab 的当前数值，避免批量重建全部内容资源。</summary>
    internal static bool SavePrefabFields(IReadOnlyList<BalanceTuningRecord> rows, out string error)
    {
        error = null;
        if (rows == null || rows.Count == 0) return true;
        GameObject root = null;
        try
        {
            string path = rows[0].SourcePath;
            root = PrefabUtility.LoadPrefabContents(path);
            foreach (BalanceTuningRecord row in rows)
                BalanceTuningLivePreview.ApplyPrefabField(root, row);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            foreach (BalanceTuningRecord row in rows) row.IsPendingPrefabSave = false;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }
    }

    #endregion
}
