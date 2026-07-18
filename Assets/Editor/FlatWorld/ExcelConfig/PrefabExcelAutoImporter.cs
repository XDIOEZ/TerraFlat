#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class PrefabExcelAutoImporter : AssetPostprocessor
{
    private const double ImportDelaySeconds = 0.75d;
    private static readonly HashSet<string> PendingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static double _nextImportTime;
    private static bool _isProcessing;

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (!PrefabExcelPreferences.AutoImportEnabled || _isProcessing)
        {
            return;
        }

        bool queued = false;
        foreach (string path in importedAssets)
        {
            if (path.StartsWith("~$", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            PrefabExcelDefinition definition = PrefabExcelSyncService.GetDefinitionByPath(path);
            if (definition == null)
            {
                continue;
            }

            PendingPaths.Add(definition.AssetPath);
            queued = true;
        }

        if (!queued)
        {
            return;
        }

        _nextImportTime = EditorApplication.timeSinceStartup + ImportDelaySeconds;
        EditorApplication.update -= ProcessPending;
        EditorApplication.update += ProcessPending;
    }

    private static void ProcessPending()
    {
        if (EditorApplication.timeSinceStartup < _nextImportTime || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        EditorApplication.update -= ProcessPending;
        if (_isProcessing)
        {
            return;
        }

        _isProcessing = true;
        try
        {
            string[] paths = new string[PendingPaths.Count];
            PendingPaths.CopyTo(paths);
            PendingPaths.Clear();

            foreach (string path in paths)
            {
                PrefabExcelDefinition definition = PrefabExcelSyncService.GetDefinitionByPath(path);
                if (definition == null)
                {
                    continue;
                }

                PrefabExcelImportReport report = PrefabExcelSyncService.Import(definition, true);
                PrefabExcelSyncWindow.LogReport(report);
                if (!report.IsValid)
                {
                    Debug.LogError($"[PrefabExcel] 自动同步 {definition.DisplayName} 已取消。请修正Excel错误后重新保存。", null);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            _isProcessing = false;
        }
    }
}
#endif
