using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;

namespace PuertsUnityMcp.Editor
{
    [Serializable]
    internal sealed class UnityMcpConsoleSnapshot
    {
        #region Console 现场快照

        public int schemaVersion = 1;
        public string snapshotId, capturedAtLocal, capturedAtUtc;
        public string profilerFile = "profiler-analysis.json";
        public int selectedDisplayFrame, entryCount, originalConsoleFlags;
        public string originalSearchFilter;
        public Entry[] entries;
        public string scope = "All entries currently retained by Unity Console, including hidden levels and search-filtered entries; duplicates are expanded.";
        public string timeCorrelation = "Export-time snapshot. Unity 2022.3 LogEntry exposes no per-entry timestamp or Profiler frame id; entries must not be attributed to the selected frame automatically. Original text is preserved, including any timestamps written by the logger.";

        internal static UnityMcpConsoleSnapshot Read(string snapshotId, DateTimeOffset capturedAt, int selectedDisplayFrame)
        {
            var assembly = typeof(EditorApplication).Assembly;
            Type entriesType = assembly.GetType("UnityEditor.LogEntries", true);
            Type entryType = assembly.GetType("UnityEditor.LogEntry", true);
            const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            const BindingFlags fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var flags = entriesType.GetProperty("consoleFlags", statics)
                ?? throw new MissingMemberException("UnityEditor.LogEntries.consoleFlags");
            MethodInfo start = RequireMethod(entriesType, "StartGettingEntries");
            MethodInfo end = RequireMethod(entriesType, "EndGettingEntries");
            MethodInfo getEntry = RequireMethod(entriesType, "GetEntryInternal");
            MethodInfo getFilter = RequireMethod(entriesType, "GetFilteringText");
            MethodInfo setFilter = RequireMethod(entriesType, "SetFilteringText");
            FieldInfo messageField = entryType.GetField("message", fields);
            FieldInfo stackStartField = entryType.GetField("callstackTextStartUTF16", fields);
            FieldInfo modeField = entryType.GetField("mode", fields);
            FieldInfo fileField = entryType.GetField("file", fields);
            FieldInfo lineField = entryType.GetField("line", fields);
            if (messageField == null || stackStartField == null || modeField == null || fileField == null || lineField == null)
                throw new MissingMemberException("Unity Console 日志字段不完整，无法保存现场。");

            var snapshot = new UnityMcpConsoleSnapshot
            {
                snapshotId = snapshotId,
                capturedAtLocal = capturedAt.ToString("o", CultureInfo.InvariantCulture),
                capturedAtUtc = capturedAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                selectedDisplayFrame = selectedDisplayFrame,
                originalConsoleFlags = (int)flags.GetValue(null),
                originalSearchFilter = (string)getFilter.Invoke(null, null)
            };
            bool reading = false;
            try
            {
                // 临时展开全部级别并移除搜索条件，结束时恢复用户原来的 Console 显示设置。
                const int collapse = 1 << 0;
                const int allLevels = (1 << 7) | (1 << 8) | (1 << 9);
                flags.SetValue(null, (snapshot.originalConsoleFlags & ~collapse) | allLevels);
                setFilter.Invoke(null, new object[] { string.Empty });
                int count = (int)start.Invoke(null, null);
                reading = true;
                var rows = new List<Entry>(count);
                object entry = Activator.CreateInstance(entryType);
                var arguments = new object[] { 0, entry };
                for (int index = 0; index < count; index++)
                {
                    arguments[0] = index;
                    if (!(bool)getEntry.Invoke(null, arguments))
                        throw new InvalidOperationException("Console 第 " + index + " 条日志读取失败，未公布本次报告。");
                    string raw = (string)messageField.GetValue(entry) ?? string.Empty;
                    int stackStart = (int)stackStartField.GetValue(entry);
                    bool hasStack = stackStart > 0 && stackStart < raw.Length;
                    int mode = (int)modeField.GetValue(entry);
                    rows.Add(new Entry
                    {
                        index = index,
                        severity = Classify(mode),
                        mode = mode,
                        rawText = raw,
                        message = hasStack ? raw.Substring(0, stackStart) : raw,
                        stackTrace = hasStack ? raw.Substring(stackStart) : string.Empty,
                        file = (string)fileField.GetValue(entry),
                        line = (int)lineField.GetValue(entry)
                    });
                }
                snapshot.entries = rows.ToArray();
                snapshot.entryCount = rows.Count;
                return snapshot;
            }
            finally
            {
                try { if (reading) end.Invoke(null, null); }
                finally
                {
                    try { setFilter.Invoke(null, new object[] { snapshot.originalSearchFilter }); }
                    finally { flags.SetValue(null, snapshot.originalConsoleFlags); }
                }
            }
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(type.FullName, name);
        }

        private static string Classify(int mode)
        {
            if ((mode & (1 << 17)) != 0) return "Exception";
            if ((mode & ((1 << 1) | (1 << 21))) != 0) return "Assert";
            if ((mode & ((1 << 0) | (1 << 4) | (1 << 6) | (1 << 8) | (1 << 11) | (1 << 20))) != 0) return "Error";
            if ((mode & ((1 << 7) | (1 << 9) | (1 << 12))) != 0) return "Warning";
            return "Log";
        }

        [Serializable]
        public sealed class Entry
        {
            public int index, mode, line;
            public string severity, rawText, message, stackTrace, file;
        }

        #endregion
    }
}
