using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>仅在存在 Library 请求文件时执行一次内容校验，供当前修复过程读取精确结果。</summary>
[InitializeOnLoad]
internal static class CodexContentValidationRunner
{
    private const string RequestPath = "Library/CodexRunContentValidation.request";
    private const string ResultPath = "Library/CodexContentValidation.result.json";
    private const int RunnerRevision = 5;
    private static bool _scheduled;

    /// <summary>脚本重载后检查是否存在待处理的校验请求。</summary>
    static CodexContentValidationRunner()
    {
        ScheduleIfRequested();
    }

    /// <summary>在编辑器空闲帧安排校验，避免资源导入回调中直接遍历资产。</summary>
    private static void ScheduleIfRequested()
    {
        _ = RunnerRevision;
        if (_scheduled || !File.Exists(RequestPath))
            return;

        _scheduled = true;
        EditorApplication.delayCall += Run;
    }

    /// <summary>执行校验并把结构化结果写入 Library。</summary>
    private static void Run()
    {
        _scheduled = false;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            ScheduleIfRequested();
            return;
        }

        File.Delete(RequestPath);
        try
        {
            FlatWorldContentValidationReport report = FlatWorldContentValidator.ValidateAll(
                FlatWorldContentValidationMode.Manual,
                true);
            WriteResult(new
            {
                success = !report.HasErrors,
                errors = report.ErrorCount,
                warnings = report.WarningCount,
                issues = report.Issues.Select(issue => issue.ToString()).ToArray()
            });
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteResult(new
            {
                success = false,
                errors = -1,
                warnings = -1,
                exception = exception.ToString()
            });
        }
    }

    /// <summary>以 UTF-8 JSON 保存校验结果，便于外部脚本稳定读取。</summary>
    private static void WriteResult(object value)
    {
        File.WriteAllText(ResultPath, JsonConvert.SerializeObject(value, Formatting.Indented));
    }
}
