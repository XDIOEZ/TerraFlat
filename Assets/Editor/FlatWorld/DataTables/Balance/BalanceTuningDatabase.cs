using System;
using System.Collections.Generic;
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
}

/// <summary>兼容旧 Unity 工作台 .asset 的只读序列化格式；调参历史已迁到 ItemWiki。</summary>
public sealed class BalanceTuningDatabase : ScriptableObject
{
    [SerializeField] private List<BalanceTuningRecord> records = new();
    [SerializeField] private bool legacyImported;
    [SerializeField] private bool catalogScanned;
}
