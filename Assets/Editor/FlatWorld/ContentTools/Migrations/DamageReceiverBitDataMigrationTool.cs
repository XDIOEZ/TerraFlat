using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class DamageReceiverBitDataMigrationTool
{
    [MenuItem("FlatWorld/数据迁移/迁移 DamageReceiver BitData")]
    public static void MigrateAllDamageReceiverBitData()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int scanned = 0;
        int changed = 0;
        int failed = 0;

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            DamageReceiver[] receivers = prefab.GetComponentsInChildren<DamageReceiver>(true);
            if (receivers == null || receivers.Length == 0)
                continue;

            foreach (DamageReceiver receiver in receivers)
            {
                scanned++;

                if (receiver.modData == null || string.IsNullOrEmpty(receiver.modData.BitData))
                    continue;

                try
                {
                    if (!TryMigrate(receiver.modData.BitData, out string migratedJson))
                        continue;

                    receiver.modData.BitData = migratedJson;
                    EditorUtility.SetDirty(receiver);
                    changed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Debug.LogError($"[DamageReceiverBitDataMigrationTool] 迁移失败: {path} / {receiver.name}\n{ex}");
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[DamageReceiverBitDataMigrationTool] 迁移完成。扫描={scanned}, 修改={changed}, 失败={failed}");
    }

    private static bool TryMigrate(string json, out string migratedJson)
    {
        migratedJson = json;

        JObject root = JObject.Parse(json);
        bool changed = false;

        if (root.TryGetValue("MaxHp", out JToken maxHpToken))
        {
            if (maxHpToken.Type == JTokenType.Object)
            {
                float value = ConvertLegacyGameValueToFloat((JObject)maxHpToken, 100f);
                root["MaxHp"] = value;
                changed = true;
            }
        }

        if (root.TryGetValue("BaseHp", out JToken baseHpToken))
        {
            if (!root.ContainsKey("MaxHp"))
            {
                root["MaxHp"] = baseHpToken.Type == JTokenType.Object
                    ? ConvertLegacyGameValueToFloat((JObject)baseHpToken, 100f)
                    : baseHpToken.Value<float>();
            }

            root.Remove("BaseHp");
            changed = true;
        }

        if (root.TryGetValue("Defense", out JToken defenseToken))
        {
            float defenseValue;
            if (defenseToken.Type == JTokenType.Object)
            {
                defenseValue = ConvertLegacyGameValueToFloat((JObject)defenseToken, 0f);
            }
            else
            {
                defenseValue = defenseToken.Value<float>();
            }

            root["Defense"] = defenseValue;
            changed = true;
        }

        if (root.TryGetValue("BaseDefense", out JToken baseDefenseToken))
        {
            if (!root.ContainsKey("Defense"))
            {
                root["Defense"] = baseDefenseToken.Type == JTokenType.Object
                    ? ConvertLegacyGameValueToFloat((JObject)baseDefenseToken, 0f)
                    : baseDefenseToken.Value<float>();
            }

            root.Remove("BaseDefense");
            changed = true;
        }

        if (root.TryGetValue("DefenseBonus", out JToken defenseBonusToken))
        {
            float defenseBonus = defenseBonusToken.Type == JTokenType.Object
                ? ConvertLegacyGameValueToFloat((JObject)defenseBonusToken, 0f)
                : defenseBonusToken.Value<float>();

            float currentDefense = root.TryGetValue("Defense", out JToken currentDefenseToken)
                ? currentDefenseToken.Value<float>()
                : 0f;

            root["Defense"] = currentDefense + defenseBonus;
            root.Remove("DefenseBonus");
            changed = true;
        }

        if (root.TryGetValue("DefenseMultiplier", out JToken defenseMultiplierToken))
        {
            float multiplier = defenseMultiplierToken.Type == JTokenType.Object
                ? ConvertLegacyGameValueToFloat((JObject)defenseMultiplierToken, 1f)
                : defenseMultiplierToken.Value<float>();

            float currentDefense = root.TryGetValue("Defense", out JToken currentDefenseToken)
                ? currentDefenseToken.Value<float>()
                : 0f;

            root["Defense"] = currentDefense * multiplier;
            root.Remove("DefenseMultiplier");
            changed = true;
        }

        if (root.TryGetValue("Defense", out JToken newDefenseToken))
        {
            float defenseValue = newDefenseToken.Value<float>();
            float safeDefense = Mathf.Max(0f, defenseValue);
            root["DefenseValues"] = new JObject
            {
                ["Cutting"] = safeDefense,
                ["Piercing"] = safeDefense,
                ["Chopping"] = safeDefense,
                ["Blunt"] = safeDefense
            };
            root["DamageSystemVersion"] = 1;
            changed = true;
        }

        if (root.ContainsKey("Weakness"))
        {
            root.Remove("Weakness");
            changed = true;
        }

        if (root.ContainsKey("MaxDefense"))
        {
            root.Remove("MaxDefense");
            changed = true;
        }

        if (!changed)
            return false;

        migratedJson = root.ToString(Formatting.None);
        return true;
    }

    private static float ConvertLegacyGameValueToFloat(JObject legacyObj, float fallback)
    {
        float baseValue = legacyObj.Value<float?>("BaseValue") ?? fallback;
        float baseAdditive = legacyObj.Value<float?>("BaseAdditive") ?? 0f;
        float additiveModifier = legacyObj.Value<float?>("AdditiveModifier") ?? 0f;
        float multiplicativeModifier = legacyObj.Value<float?>("MultiplicativeModifier") ?? 1f;
        float finalAdditive = legacyObj.Value<float?>("FinalAdditive") ?? 0f;

        return (((baseValue + baseAdditive) + ((baseValue + baseAdditive) * additiveModifier)) * multiplicativeModifier) + finalAdditive;
    }
}
