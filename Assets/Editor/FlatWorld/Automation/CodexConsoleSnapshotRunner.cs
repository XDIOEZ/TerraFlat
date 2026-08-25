using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;

/// <summary>仅在收到 Library 请求时读取当前 Unity Console，供构建问题收尾定位。</summary>
[InitializeOnLoad]
internal static class CodexConsoleSnapshotRunner
{
    private const string RequestPath = "Library/CodexReadConsole.request";
    private const string ResultPath = "Library/CodexConsoleSnapshot.result.json";

    /// <summary>脚本重载后安排一次 Console 快照。</summary>
    static CodexConsoleSnapshotRunner()
    {
        if (File.Exists(RequestPath))
            EditorApplication.delayCall += Capture;
    }

    /// <summary>通过 UnityEditor 内部只读接口提取当前 Console 条目。</summary>
    private static void Capture()
    {
        File.Delete(RequestPath);
        try
        {
            Assembly editorAssembly = typeof(EditorApplication).Assembly;
            Type entriesType = editorAssembly.GetType("UnityEditor.LogEntries", true);
            Type entryType = editorAssembly.GetType("UnityEditor.LogEntry", true);
            MethodInfo getCount = entriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo start = entriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo end = entriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo getEntry = entriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo messageField = entryType.GetField("message", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo modeField = entryType.GetField("mode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getCount == null || start == null || end == null || getEntry == null || messageField == null || modeField == null)
                throw new MissingMethodException("当前 Unity 版本的 Console 内部接口与快照脚本不匹配。");

            int count = (int)getCount.Invoke(null, null);
            var records = new List<object>(Math.Min(count, 500));
            start.Invoke(null, null);
            try
            {
                int first = Math.Max(0, count - 500);
                for (int index = first; index < count; index++)
                {
                    object entry = Activator.CreateInstance(entryType);
                    object result = getEntry.Invoke(null, new[] { (object)index, entry });
                    if (result is bool found && !found)
                        continue;

                    records.Add(new
                    {
                        index,
                        mode = Convert.ToInt32(modeField.GetValue(entry)),
                        message = messageField.GetValue(entry) as string ?? string.Empty
                    });
                }
            }
            finally
            {
                end.Invoke(null, null);
            }

            WriteResult(new { success = true, count, records });
        }
        catch (Exception exception)
        {
            WriteResult(new { success = false, exception = exception.ToString() });
        }
    }

    /// <summary>以 UTF-8 JSON 保存 Console 快照。</summary>
    private static void WriteResult(object value)
    {
        File.WriteAllText(ResultPath, JsonConvert.SerializeObject(value, Formatting.Indented));
    }
}
