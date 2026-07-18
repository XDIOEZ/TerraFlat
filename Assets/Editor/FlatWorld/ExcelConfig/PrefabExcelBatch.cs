#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class PrefabExcelBatch
{
    public static void ExportAll()
    {
        PrefabExcelSyncService.ExportAll();
        Debug.Log("[PrefabExcelBatch] ExportAll completed.");
    }

    public static void ValidateAll()
    {
        foreach (PrefabExcelDefinition definition in PrefabExcelSyncService.Definitions)
        {
            PrefabExcelImportReport report = PrefabExcelSyncService.Preview(definition);
            PrefabExcelSyncWindow.LogReport(report);
            if (!report.IsValid)
            {
                throw new InvalidOperationException(report.BuildSummary());
            }
        }

        Debug.Log("[PrefabExcelBatch] ValidateAll completed.");
    }
}
#endif
