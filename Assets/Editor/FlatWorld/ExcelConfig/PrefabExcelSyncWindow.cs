#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class PrefabExcelSyncWindow : EditorWindow
{
    private Vector2 _scroll;
    private PrefabExcelImportReport _lastReport;

    [MenuItem("FlatWorld/Excel配置同步")]
    public static void Open()
    {
        GetWindow<PrefabExcelSyncWindow>("Excel配置同步");
    }

    [MenuItem("FlatWorld/Excel配置/导出全部Prefab到Excel")]
    private static void ExportAllMenu()
    {
        if (!EditorUtility.DisplayDialog(
                "覆盖全部Excel配置",
                "这会使用当前Prefab数据覆盖三份Excel。以后通常应当以Excel为数据源，只有首次建立或明确重建时才使用导出。",
                "确认覆盖",
                "取消"))
        {
            return;
        }

        RunWithProgress("导出Excel配置", () => PrefabExcelSyncService.ExportAll());
    }

    [MenuItem("FlatWorld/Excel配置/从全部Excel应用到Prefab")]
    private static void ImportAllMenu()
    {
        foreach (PrefabExcelDefinition definition in PrefabExcelSyncService.Definitions)
        {
            PrefabExcelImportReport report = PrefabExcelSyncService.Import(definition, true);
            LogReport(report);
            if (!report.IsValid)
            {
                EditorUtility.DisplayDialog("Excel导入已停止", report.BuildSummary(12), "确定");
                return;
            }
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("FlatWorld Prefab ↔ Excel", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "推荐流程：首次从Prefab导出；之后只编辑黄色Excel列。Excel保存时会先整表校验，再自动写入Prefab。AssetGuid负责定位，ItemId负责防止映射错位。",
            MessageType.Info);

        bool autoImport = EditorGUILayout.ToggleLeft("保存Excel后自动同步到Prefab", PrefabExcelPreferences.AutoImportEnabled);
        if (autoImport != PrefabExcelPreferences.AutoImportEnabled)
        {
            PrefabExcelPreferences.AutoImportEnabled = autoImport;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("打开Excel目录", GUILayout.Height(26)))
        {
            string folder = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Assets/GameConfig/Excel"));
            EditorUtility.RevealInFinder(folder);
        }

        if (GUILayout.Button("预览全部", GUILayout.Height(26)))
        {
            PreviewAll();
        }

        if (GUILayout.Button("应用全部", GUILayout.Height(26)))
        {
            ApplyAll();
        }

        if (GUILayout.Button("从Prefab重建全部Excel", GUILayout.Height(26)))
        {
            if (EditorUtility.DisplayDialog("覆盖全部Excel", "确定使用Prefab当前值覆盖全部Excel吗？", "覆盖", "取消"))
            {
                RunWithProgress("导出Excel配置", () => PrefabExcelSyncService.ExportAll());
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
        foreach (PrefabExcelDefinition definition in PrefabExcelSyncService.Definitions)
        {
            DrawDefinition(definition);
        }

        if (_lastReport != null)
        {
            DrawReport(_lastReport);
        }
    }

    private void DrawDefinition(PrefabExcelDefinition definition)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(definition.DisplayName, EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(definition.AssetPath, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("打开Excel"))
        {
            string absolutePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), definition.AssetPath));
            if (File.Exists(absolutePath))
            {
                EditorUtility.OpenWithDefaultApp(absolutePath);
            }
            else
            {
                EditorUtility.DisplayDialog("文件不存在", definition.AssetPath, "确定");
            }
        }

        if (GUILayout.Button("预览导入"))
        {
            _lastReport = PrefabExcelSyncService.Preview(definition);
            LogReport(_lastReport);
        }

        if (GUILayout.Button("应用到Prefab"))
        {
            _lastReport = PrefabExcelSyncService.Import(definition, true);
            LogReport(_lastReport);
        }

        if (GUILayout.Button("从Prefab重新导出"))
        {
            if (EditorUtility.DisplayDialog("覆盖Excel", $"确定覆盖 {definition.AssetPath} 吗？", "覆盖", "取消"))
            {
                RunWithProgress($"导出{definition.DisplayName}", () => PrefabExcelSyncService.Export(definition));
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawReport(PrefabExcelImportReport report)
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField(report.Applied ? "最近一次应用结果" : "最近一次预览结果", EditorStyles.boldLabel);

        MessageType messageType = report.Errors.Count > 0
            ? MessageType.Error
            : report.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info;
        EditorGUILayout.HelpBox(report.BuildSummary(0), messageType);

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(160), GUILayout.MaxHeight(360));
        foreach (string error in report.Errors)
        {
            EditorGUILayout.LabelField("错误：" + error, EditorStyles.wordWrappedLabel);
        }
        foreach (string warning in report.Warnings)
        {
            EditorGUILayout.LabelField("警告：" + warning, EditorStyles.wordWrappedLabel);
        }
        foreach (PrefabExcelChange change in report.Changes)
        {
            EditorGUILayout.LabelField(change.ToString(), EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    private void PreviewAll()
    {
        var combined = new PrefabExcelImportReport();
        foreach (PrefabExcelDefinition definition in PrefabExcelSyncService.Definitions)
        {
            PrefabExcelImportReport report = PrefabExcelSyncService.Preview(definition);
            combined.ScannedRows += report.ScannedRows;
            combined.EnabledRows += report.EnabledRows;
            combined.MatchedPrefabs += report.MatchedPrefabs;
            combined.ChangedPrefabs += report.ChangedPrefabs;
            combined.Changes.AddRange(report.Changes);
            combined.Warnings.AddRange(report.Warnings);
            combined.Errors.AddRange(report.Errors);
        }
        _lastReport = combined;
        LogReport(_lastReport);
    }

    private void ApplyAll()
    {
        foreach (PrefabExcelDefinition definition in PrefabExcelSyncService.Definitions)
        {
            _lastReport = PrefabExcelSyncService.Import(definition, true);
            LogReport(_lastReport);
            if (!_lastReport.IsValid)
            {
                return;
            }
        }
    }

    internal static void LogReport(PrefabExcelImportReport report)
    {
        if (report == null)
        {
            return;
        }

        string summary = report.BuildSummary();
        if (report.Errors.Count > 0)
        {
            Debug.LogError("[PrefabExcel] " + summary);
        }
        else if (report.Warnings.Count > 0)
        {
            Debug.LogWarning("[PrefabExcel] " + summary);
        }
        else
        {
            Debug.Log("[PrefabExcel] " + summary);
        }
    }

    private static void RunWithProgress(string title, Action action)
    {
        try
        {
            EditorUtility.DisplayProgressBar(title, "处理中...", 0.5f);
            action();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(title + "失败", exception.Message, "确定");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
#endif
