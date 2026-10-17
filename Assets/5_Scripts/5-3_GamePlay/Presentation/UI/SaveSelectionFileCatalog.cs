using System;
using System.Collections.Generic;
using System.IO;

/// <summary>存档列表先采样文件元数据，再在内存中排序，避免比较器重复读盘。</summary>
public static class SaveSelectionFileCatalog
{
    #region 文件元数据快照

    public readonly struct Entry
    {
        public readonly string Name;
        public readonly DateTime LastExitTimeUtc;

        public Entry(string name, DateTime lastExitTimeUtc)
        {
            Name = name;
            LastExitTimeUtc = lastExitTimeUtc;
        }
    }

    public static List<Entry> Load(IReadOnlyList<string> paths, Func<string, DateTime> readExitTime)
    {
        var entries = new List<Entry>(paths.Count);
        for (int index = 0; index < paths.Count; index++)
            entries.Add(new Entry(Path.GetFileNameWithoutExtension(paths[index]), readExitTime(paths[index])));
        entries.Sort((left, right) =>
        {
            int timeComparison = right.LastExitTimeUtc.CompareTo(left.LastExitTimeUtc);
            return timeComparison != 0 ? timeComparison : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        });
        return entries;
    }

    #endregion
}
