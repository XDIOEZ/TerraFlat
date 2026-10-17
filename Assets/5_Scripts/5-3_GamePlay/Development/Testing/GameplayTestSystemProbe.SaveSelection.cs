using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 存档列表元数据规则

    // 独立路径和计数读取器调用正式目录快照，不访问玩家磁盘存档。
    private static JObject ServiceSaveFileCatalog(JObject args)
    {
        string mode = Text(args, "mode", "mixed");
        string[] names = mode == "empty" ? Array.Empty<string>() : mode == "ties"
            ? new[] { "zeta", "Gamma", "beta", "Alpha" }
            : new[] { "Old", "Missing", "Newest", "Middle" };
        var paths = new List<string>();
        var reads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string name in names)
            paths.Add(Path.Combine("SaveCatalogProbe", name + ".bytes"));
        int readCount = 0;
        int maxReads = 0;
        List<SaveSelectionFileCatalog.Entry> entries = SaveSelectionFileCatalog.Load(paths, path =>
        {
            reads.TryGetValue(path, out int previous);
            reads[path] = previous + 1;
            maxReads = Math.Max(maxReads, previous + 1);
            readCount++;
            string name = Path.GetFileNameWithoutExtension(path);
            return mode == "ties" ? new DateTime(200, DateTimeKind.Utc)
                : new DateTime(name == "Newest" ? 300 : name == "Middle" ? 200 : name == "Old" ? 100 : 0,
                    DateTimeKind.Utc);
        });
        var orderedNames = new JArray();
        foreach (SaveSelectionFileCatalog.Entry entry in entries)
            orderedNames.Add(entry.Name);
        return new JObject
        {
            ["count"] = entries.Count,
            ["readCount"] = readCount,
            ["maxReadsPerPath"] = maxReads,
            ["names"] = orderedNames,
            ["firstTicks"] = entries.Count > 0 ? entries[0].LastExitTimeUtc.Ticks : 0
        };
    }

    #endregion
}
