#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;

public enum PrefabExcelConfigKind
{
    Equipment,
    Defense,
    Food
}

public sealed class PrefabExcelDefinition
{
    public PrefabExcelConfigKind Kind { get; }
    public string DisplayName { get; }
    public string AssetPath { get; }
    public string SheetName { get; }
    public IReadOnlyList<string> Headers { get; }

    public PrefabExcelDefinition(
        PrefabExcelConfigKind kind,
        string displayName,
        string assetPath,
        string sheetName,
        params string[] headers)
    {
        Kind = kind;
        DisplayName = displayName;
        AssetPath = assetPath;
        SheetName = sheetName;
        Headers = headers;
    }
}

public sealed class PrefabExcelChange
{
    public string PrefabPath;
    public string ItemId;
    public string Field;
    public string OldValue;
    public string NewValue;

    public override string ToString()
    {
        string target = string.IsNullOrWhiteSpace(ItemId) ? PrefabPath : ItemId;
        return $"{target}: {Field}  {OldValue} -> {NewValue}";
    }
}

public sealed class PrefabExcelImportReport
{
    public PrefabExcelDefinition Definition;
    public string FilePath;
    public int ScannedRows;
    public int EnabledRows;
    public int MatchedPrefabs;
    public int ChangedPrefabs;
    public bool Applied;

    public readonly List<PrefabExcelChange> Changes = new List<PrefabExcelChange>();
    public readonly List<string> Warnings = new List<string>();
    public readonly List<string> Errors = new List<string>();

    public bool IsValid => Errors.Count == 0;

    public string BuildSummary(int maxChanges = 30)
    {
        var builder = new StringBuilder();
        string name = Definition != null ? Definition.DisplayName : "Excel配置";
        builder.AppendLine($"[{name}] 扫描行={ScannedRows}, 启用行={EnabledRows}, 匹配Prefab={MatchedPrefabs}, 修改字段={Changes.Count}, 修改Prefab={ChangedPrefabs}");

        if (Errors.Count > 0)
        {
            builder.AppendLine($"错误 ({Errors.Count})：");
            for (int i = 0; i < Errors.Count; i++)
            {
                builder.AppendLine($"  - {Errors[i]}");
            }
        }

        if (Warnings.Count > 0)
        {
            builder.AppendLine($"警告 ({Warnings.Count})：");
            for (int i = 0; i < Warnings.Count; i++)
            {
                builder.AppendLine($"  - {Warnings[i]}");
            }
        }

        int visibleChanges = Math.Min(maxChanges, Changes.Count);
        if (visibleChanges > 0)
        {
            builder.AppendLine(Applied ? "已应用修改：" : "待应用修改：");
            for (int i = 0; i < visibleChanges; i++)
            {
                builder.AppendLine($"  - {Changes[i]}");
            }

            if (Changes.Count > visibleChanges)
            {
                builder.AppendLine($"  ... 其余 {Changes.Count - visibleChanges} 项请在同步窗口中查看");
            }
        }

        if (IsValid && Changes.Count == 0)
        {
            builder.AppendLine("Excel 与 Prefab 当前一致。 ");
        }

        return builder.ToString().TrimEnd();
    }
}

public static class PrefabExcelPreferences
{
    private const string AutoImportKey = "FlatWorld.PrefabExcel.AutoImport";

    public static bool AutoImportEnabled
    {
        get => EditorPrefs.GetBool(AutoImportKey, true);
        set => EditorPrefs.SetBool(AutoImportKey, value);
    }
}
#endif
