using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>数值工作台的数据源种类；历史按“资源 + 定义 ID + 字段”分别保存。</summary>
internal enum BalanceTuningSourceKind
{
    JsonDefinition,
    Prefab
}

internal enum BalanceTuningValueKind
{
    Number,
    Toggle,
    Text
}

[Serializable]
internal sealed class BalanceTuningHistory
{
    public float Before;
    public float After;
    public float StepBefore;
    public float StepAfter;
    public int DirectionBefore;
    public int StreakBefore;
    public string Feedback;
    public string Timestamp;
}

[Serializable]
internal sealed class BalanceTuningRecord
{
    public string Key;
    public string GroupKey;
    public string Name;
    public string DefinitionId;
    public string SourcePath;
    public string FieldPath;
    public string Label;
    public string Category;
    public BalanceTuningSourceKind SourceKind;
    public BalanceTuningValueKind ValueKind;
    public float Value;
    public bool ToggleValue;
    public string TextValue;
    public float Step;
    public float Min = 0f;
    public float Max = 1000000f;
    public int LastDirection;
    public int DirectionStreak;
    public bool IsPendingPrefabSave;
    public List<BalanceTuningHistory> History = new();

    /// <summary>同方向轻微加速，反方向减半；不会让首次反馈砍掉一半范围。</summary>
    public bool Vote(int direction)
    {
        if (direction != -1 && direction != 1)
            return false;
        float stepBefore = Mathf.Max(0.0001f, Step);
        float nextStep = stepBefore;
        if (LastDirection != 0 && direction != LastDirection)
            nextStep = Mathf.Max(0.0001f, nextStep * 0.5f);
        else if (direction == LastDirection && DirectionStreak >= 2)
            nextStep = Mathf.Min(1000000f, nextStep * 1.25f);
        return Change(Mathf.Clamp(Value + direction * nextStep, Min, Max),
            direction, nextStep, direction > 0 ? "偏低 ↑" : "偏高 ↓", stepBefore);
    }

    /// <summary>手动输入也留下可撤销的版本，不限制今后的微调方向。</summary>
    public bool SetValue(float nextValue)
    {
        return Change(Mathf.Clamp(nextValue, Min, Max), 0, Mathf.Max(0.0001f, Step), "手动输入", Step);
    }

    /// <summary>按当前条目的最近一次操作撤销数值与自适应方向。</summary>
    public bool Undo()
    {
        if (History == null || History.Count == 0)
            return false;
        BalanceTuningHistory previous = History[History.Count - 1];
        History.RemoveAt(History.Count - 1);
        Value = previous.Before;
        Step = previous.StepBefore;
        LastDirection = previous.DirectionBefore;
        DirectionStreak = previous.StreakBefore;
        return true;
    }

    private bool Change(float nextValue, int direction, float nextStep, string feedback, float stepBefore)
    {
        if (float.IsNaN(nextValue) || float.IsInfinity(nextValue) ||
            Mathf.Approximately(nextValue, Value))
            return false;
        History ??= new List<BalanceTuningHistory>();
        History.Add(new BalanceTuningHistory
        {
            Before = Value,
            After = nextValue,
            StepBefore = stepBefore,
            StepAfter = nextStep,
            DirectionBefore = LastDirection,
            StreakBefore = DirectionStreak,
            Feedback = feedback,
            Timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss")
        });
        Value = nextValue;
        Step = nextStep;
        DirectionStreak = direction == 0 ? 0 : direction == LastDirection ? DirectionStreak + 1 : 1;
        LastDirection = direction;
        IsPendingPrefabSave = SourceKind == BalanceTuningSourceKind.Prefab;
        return true;
    }

    /// <summary>默认步长取当前值约 5%，小数与概率也能独立调整。</summary>
    public static float DefaultStep(float value, string fieldPath = "")
    {
        float magnitude = Mathf.Abs(value);
        if (magnitude >= 10f) return Mathf.Max(1f, Mathf.Round(magnitude * 0.05f * 10f) / 10f);
        if (magnitude >= 1f) return Mathf.Max(0.1f, Mathf.Round(magnitude * 0.05f * 100f) / 100f);
        if (magnitude >= 0.01f) return Mathf.Max(0.005f, magnitude * 0.05f);
        // 伤害/数量从 0 起调应至少走 1 点，概率/倍率才采用百分级步长。
        bool fractional = fieldPath.IndexOf("chance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          fieldPath.IndexOf("probability", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          fieldPath.IndexOf("multiplier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          fieldPath.IndexOf("rate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          fieldPath.IndexOf("increase", StringComparison.OrdinalIgnoreCase) >= 0;
        return fractional ? 0.01f : 1f;
    }
}

/// <summary>Unity 不直接序列化字典；磁盘用列表，查找使用只重建一次的字典。</summary>
public sealed class BalanceTuningDatabase : ScriptableObject
{
    internal const string AssetPath = "Assets/Editor/FlatWorld/DataTables/Balance/BalanceTuningDatabase.asset";

    [SerializeField] private List<BalanceTuningRecord> records = new();
    [SerializeField] private bool legacyImported;
    [SerializeField] private bool catalogScanned;

    [NonSerialized] private Dictionary<string, BalanceTuningRecord> lookup;

    internal IReadOnlyList<BalanceTuningRecord> Records => records;
    internal bool LegacyImported => legacyImported;
    internal bool CatalogScanned => catalogScanned;

    /// <summary>加载持久的工程级调参历史。</summary>
    internal static BalanceTuningDatabase LoadOrCreate()
    {
        BalanceTuningDatabase database = AssetDatabase.LoadAssetAtPath<BalanceTuningDatabase>(AssetPath);
        if (database != null) return database;
        database = CreateInstance<BalanceTuningDatabase>();
        AssetDatabase.CreateAsset(database, AssetPath);
        AssetDatabase.SaveAssetIfDirty(database);
        return database;
    }

    /// <summary>扫描后更新条目但保留同一个字段的步长与操作历史。</summary>
    internal void Merge(IEnumerable<BalanceTuningRecord> scanned, bool replaceValues = false)
    {
        EnsureLookup();
        bool changed = false;
        foreach (BalanceTuningRecord row in scanned)
        {
            if (lookup.TryGetValue(row.Key, out BalanceTuningRecord existing))
            {
                existing.History ??= new List<BalanceTuningHistory>();
                existing.Name = row.Name;
                existing.Label = row.Label;
                existing.Category = row.Category;
                if (replaceValues && existing.History.Count == 0 && !existing.IsPendingPrefabSave)
                    existing.Value = row.Value;
                changed = true;
                continue;
            }
            records.Add(row);
            lookup.Add(row.Key, row);
            changed = true;
        }
        if (changed) Save();
    }

    internal void MarkLegacyImported()
    {
        legacyImported = true;
        Save();
    }

    internal void MarkCatalogScanned()
    {
        catalogScanned = true;
        Save();
    }

    /// <summary>序列化之前只更新当前数据库资源，不触碰其他配置。</summary>
    internal void Save()
    {
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssetIfDirty(this);
    }

    private void EnsureLookup()
    {
        if (lookup != null) return;
        records ??= new List<BalanceTuningRecord>();
        lookup = new Dictionary<string, BalanceTuningRecord>(records.Count, StringComparer.Ordinal);
        foreach (BalanceTuningRecord row in records)
            if (row != null && !string.IsNullOrEmpty(row.Key))
                lookup[row.Key] = row;
    }

    internal BalanceTuningRecord Find(string key)
    {
        EnsureLookup();
        return lookup.TryGetValue(key, out BalanceTuningRecord result) ? result : null;
    }
}
