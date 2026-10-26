using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>编辑态写 Prefab；运行时仅定向修改当前加载的物品，不触发 Unload/Load 或切换场景。</summary>
internal static class BalanceTuningLivePreview
{
    /// <summary>应用 JSON 或 Prefab 数值到当前已加载 Item；不把运行态快照写回原始配置。</summary>
    internal static int ApplyNow(BalanceTuningRecord row)
    {
        if (!EditorApplication.isPlaying || row == null)
            return 0;
        // 多人游戏的正式数值由服务端裁决，本地 Editor 不向联机会话注入单边修改。
        if (GameNetwork.IsOnline)
            return 0;
        ItemMgr manager = ItemMgr.GetInstance();
        if (manager?.RuntimeItemsGroup == null)
            return 0;
        string id = row.SourceKind == BalanceTuningSourceKind.JsonDefinition
            ? row.DefinitionId : Path.GetFileNameWithoutExtension(row.SourcePath);
        if (string.IsNullOrEmpty(id) || !manager.RuntimeItemsGroup.TryGetValue(id, out IReadOnlyList<Item> items))
            return row.SourceKind == BalanceTuningSourceKind.JsonDefinition ? ApplyEquippedBonuses(manager, row) : 0;
        int applied = 0;
        // 遍历稳定的目标 ID 分组，不扫描场景中所有对象。
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            if (item == null || item.itemData == null || item.DestructionHandled)
                continue;
            // 已有 JSON 定义的物品不得被 Prefab 模板覆盖。
            if (row.SourceKind == BalanceTuningSourceKind.Prefab &&
                GameRes.ExistingInstance?.TryGetItemDefinition(id, out _) == true)
                continue;
            bool done = row.SourceKind == BalanceTuningSourceKind.JsonDefinition
                ? ApplyJsonItemField(item, row)
                : ApplyPrefabField(item.gameObject, row, true);
            if (done) applied++;
        }
        if (row.SourceKind == BalanceTuningSourceKind.JsonDefinition)
            applied += ApplyEquippedBonuses(manager, row);
        return applied;
    }

    /// <summary>修改旧 Prefab 字段；同一入口可写源资产或实时物品组件。</summary>
    internal static bool ApplyPrefabField(GameObject root, BalanceTuningRecord row, bool live = false)
    {
        if (root == null || row == null) return false;
        string[] names = row.FieldPath.Split('.');
        if (names.Length < 2) return false;
        bool changed = false;
        switch (names[0])
        {
            case "Damage":
                foreach (Mod_Damage damage in root.GetComponentsInChildren<Mod_Damage>(true))
                {
                    CombatDamage old = damage.ResolveDamageValues();
                    CombatDamage copy = new CombatDamage(old.Cutting, old.Piercing, old.Chopping, old.Blunt);
                    if (!SetFloatField(copy, names[1], row.Value)) continue;
                    damage.SetDamageValues(copy);
                    if (!live) EditorUtility.SetDirty(damage);
                    changed = true;
                }
                break;
            case "Defense":
                foreach (Mod_DamageReceiver receiver in root.GetComponentsInChildren<Mod_DamageReceiver>(true))
                {
                    if (receiver.Data == null) continue;
                    CombatDefense old = receiver.Defense ?? new CombatDefense();
                    CombatDefense copy = new CombatDefense(old.Cutting, old.Piercing, old.Chopping, old.Blunt);
                    if (!SetFloatField(copy, names[1], row.Value)) continue;
                    receiver.SetDefense(copy);
                    if (!live) EditorUtility.SetDirty(receiver);
                    changed = true;
                }
                break;
            case "Health":
                foreach (Mod_DamageReceiver receiver in root.GetComponentsInChildren<Mod_DamageReceiver>(true))
                {
                    if (receiver.Data == null) continue;
                    if (names[1] == "MaxHp")
                    {
                        receiver.Data.MaxHp = Mathf.Max(0f, row.Value);
                        if (receiver.Data.Hp > receiver.Data.MaxHp)
                            receiver.Data.Hp = receiver.Data.MaxHp;
                    }
                    else if (names[1] == "Hp")
                    {
                        if (live) receiver.Hp = Mathf.Clamp(row.Value, 0f, receiver.Data.MaxHp);
                        else receiver.Data.Hp = Mathf.Clamp(row.Value, 0f, receiver.Data.MaxHp);
                    }
                    else continue;
                    if (!live) EditorUtility.SetDirty(receiver);
                    changed = true;
                }
                break;
            case "Food":
                foreach (Mod_Food food in root.GetComponentsInChildren<Mod_Food>(true))
                {
                    if (ApplyFoodField(food, row, names[1..]))
                    {
                        if (live) food.DataUpdate?.Invoke();
                        else EditorUtility.SetDirty(food);
                        changed = true;
                    }
                }
                break;
        }
        return changed;
    }

    /// <summary>只修改食物数据明确的配置字段，不重置进食进度与玩家运行态。</summary>
    private static bool ApplyFoodField(Mod_Food food, BalanceTuningRecord row, string[] parts)
    {
        if (food == null || parts.Length == 0) return false;
        Food data = food.Data;
        if (data == null) return false;
        if (parts[0] == "nutrition" && parts.Length > 1)
            return data.nutrition != null && SetFloatField(data.nutrition, parts[1], row.Value);
        if (parts[0] == "nutritionConsumeSpeed")
        {
            if (data.nutritionConsumeSpeed == null)
                data.nutritionConsumeSpeed = new GameValue_float(Mathf.Max(0f, row.Value));
            else
                data.nutritionConsumeSpeed.BaseValue = Mathf.Max(0f, row.Value);
            return true;
        }
        if (parts[0] == "EnableSpoilage" || parts[0] == "SpoilageIntervalSeconds" ||
            parts[0] == "SpoilageTargetItemID")
        {
            FoodSpoilageObserverData spoilage = FoodSpoilageObserverData.Load(food.FoodModData);
            switch (parts[0])
            {
                case "EnableSpoilage": spoilage.EnableSpoilage = row.ToggleValue; break;
                case "SpoilageIntervalSeconds": spoilage.SpoilageIntervalSeconds = Mathf.Max(0f, row.Value); break;
                case "SpoilageTargetItemID": spoilage.SpoilageTargetItemID = row.TextValue; break;
            }
            spoilage.Save(food.FoodModData);
            return true;
        }
        if (parts[0] == "Max_EatingProgress")
        {
            data.Max_EatingProgress = Mathf.Max(1f, row.Value);
            return true;
        }
        return SetFloatField(data, parts[0], row.Value);
    }

    /// <summary>识别 JSON 模块的显式数值，并使用原模块原位刷新接口。</summary>
    private static bool ApplyJsonItemField(Item item, BalanceTuningRecord row)
    {
        // 普通物品质量/体积/耐久也使用实例的实际数据入口，已加载的物品立即更新。
        if (row.FieldPath == "weight" && item.itemData.Stack != null)
        {
            item.itemData.Stack.Weight = Mathf.Max(0f, row.Value);
            return true;
        }
        if (row.FieldPath == "volume" && item.itemData.Stack != null)
        {
            item.itemData.Stack.Volume = Mathf.Max(0f, row.Value);
            return true;
        }
        if (row.FieldPath == "maxDurability")
        {
            item.itemData.MaxDurability = Mathf.Max(0f, row.Value);
            item.itemData.Durability = Mathf.Min(item.itemData.Durability, item.itemData.MaxDurability);
            return true;
        }
        if (row.FieldPath == "durability")
        {
            item.itemData.Durability = Mathf.Clamp(row.Value, 0f, item.itemData.MaxDurability);
            return true;
        }
        if (!row.FieldPath.StartsWith("modules.", StringComparison.Ordinal)) return false;
        string[] segments = row.FieldPath.Split('.');
        if (segments.Length < 4) return false;
        string moduleKey = segments[1];
        string section = segments[2];
        if (!item.Mods.TryGetValue(moduleKey, out Module module) || module == null)
            return false;

        // DamageValues.Physical 是真正的物理攻击力；不要改四个旧兼容槽。
        if (module is Mod_Damage damage && section == "parameters" &&
            row.FieldPath.Contains(".DamageValues."))
        {
            CombatDamage values = damage.ResolveDamageValues();
            if (!SetFloatFieldOrProperty(values, segments[segments.Length - 1], row.Value)) return false;
            damage.SetDamageValues(values);
            return true;
        }

        if (module is Mod_Food food && section == "data" &&
            row.FieldPath.Contains(".FoodData."))
        {
            int start = Array.IndexOf(segments, "FoodData");
            if (start < 0 || start + 1 >= segments.Length) return false;
            string[] parts = new string[segments.Length - start - 1];
            Array.Copy(segments, start + 1, parts, 0, parts.Length);
            if (!ApplyFoodField(food, row, parts)) return false;
            food.DataUpdate?.Invoke();
            item.MarkModuleScheduleDirty();
            return true;
        }

        // 数组和持久数据可能有外部所有权，由正式原位资源重载处理。
        if (section != "parameters" || row.FieldPath.Contains("[") ||
            row.ValueKind != BalanceTuningValueKind.Number)
            return false;
        try
        {
            JObject delta = new JObject();
            JObject cursor = delta;
            for (int index = 3; index < segments.Length - 1; index++)
            {
                JObject nested = new JObject();
                cursor[segments[index]] = nested;
                cursor = nested;
            }
            cursor[segments[segments.Length - 1]] = row.Value;
            module.ApplyResourceConfiguration(row.DefinitionId, moduleKey, delta.ToString());
            item.MarkModuleScheduleDirty();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[数值工作台] {row.DefinitionId}/{row.Label} 无法原位更新：{exception.Message}");
            return false;
        }
    }

    /// <summary>更新已装备护甲的实际防御增益，先卸载旧增益再装入新增益，避免叠加残留。</summary>
    private static int ApplyEquippedBonuses(ItemMgr manager, BalanceTuningRecord row)
    {
        if (row.SourceKind != BalanceTuningSourceKind.JsonDefinition ||
            !row.FieldPath.Contains("equipmentInstances[") ||
            !row.FieldPath.Contains("DefenseBonus"))
            return 0;
        Match match = Regex.Match(row.FieldPath, @"equipmentInstances\[(\d+)\]");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int effectIndex)) return 0;
        int changed = 0;
        foreach (Item wearer in manager.WorldRunTimeItems.Values)
        {
            if (wearer == null || wearer.DestructionHandled) continue;
            Mod_Equipment equipment = wearer.GetComponentInChildren<Mod_Equipment>(true);
            if (equipment == null || !equipment.IsRuntimeLoaded) continue;
            var indices = new Dictionary<ItemData, int>();
            foreach (EquipmentInstance instance in equipment.EnumerateEquippedInstances())
            {
                ItemData equippedData = instance.EquippedItem;
                if (equippedData == null || !string.Equals(equippedData.IDName, row.DefinitionId, StringComparison.OrdinalIgnoreCase))
                    continue;
                indices.TryGetValue(equippedData, out int index);
                indices[equippedData] = index + 1;
                if (index != effectIndex || instance is not EquipmentInstance_Defense defense) continue;
                defense.UnEquip(wearer);
                bool updated;
                if (row.FieldPath.EndsWith(".DefenseBonusIncrease", StringComparison.Ordinal))
                    updated = SetFloatField(defense, "DefenseBonusIncrease", row.Value);
                else
                {
                    defense.DefenseBonus ??= new CombatDefense();
                    updated = SetFloatFieldOrProperty(defense.DefenseBonus, LastFieldName(row.FieldPath), row.Value);
                }
                if (!updated)
                {
                    defense.Equip(wearer);
                    continue;
                }
                defense.Equip(wearer);
                changed++;
            }
        }
        return changed;
    }

    private static string LastFieldName(string path)
    {
        int dot = path.LastIndexOf('.');
        return dot >= 0 ? path.Substring(dot + 1) : path;
    }

    private static bool SetFloatField(object target, string name, float value)
    {
        FieldInfo field = target?.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (field == null || field.FieldType != typeof(float)) return false;
        field.SetValue(target, value);
        return true;
    }

    private static bool SetFloatFieldOrProperty(object target, string name, float value)
    {
        if (SetFloatField(target, name, value)) return true;
        PropertyInfo property = target?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || property.PropertyType != typeof(float) || !property.CanWrite) return false;
        property.SetValue(target, value);
        return true;
    }
}
