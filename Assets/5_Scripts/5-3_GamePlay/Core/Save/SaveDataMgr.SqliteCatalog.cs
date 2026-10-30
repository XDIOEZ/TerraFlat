using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.Persistence;
using MemoryPack;
using UnityEngine;

public partial class SaveDataMgr
{
    #region 存档目录元数据
    public sealed class ArchiveCatalogEntry
    {
        public string Name, Path;
        public DateTime LastExitTimeUtc;
    }

    public static List<ArchiveCatalogEntry> ReadSqliteSaveCatalog(string root)
    {
        var entries = new List<ArchiveCatalogEntry>();
        if (!Directory.Exists(root)) return entries;
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string path = Path.Combine(directory, "save.sqlite");
            if (!File.Exists(path)) continue;
            try
            {
                bool borrow = instance?.archiveStore != null && string.Equals(instance.archivePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
                using var owned = borrow ? null : new SqliteSaveArchiveStore(path);
                ISaveArchiveStore store = borrow ? instance.archiveStore : owned;
                var metadata = store.ReadMetadataAsync().GetAwaiter().GetResult();
                if (!metadata.HasSnapshot) continue;
                entries.Add(new ArchiveCatalogEntry { Name = metadata.DisplayName, Path = path,
                    LastExitTimeUtc = new DateTime(metadata.UpdatedUtcTicks, DateTimeKind.Utc) });
            }
            catch (Exception exception) { Debug.LogWarning($"读取存档元数据失败：{path}\n{exception.Message}"); }
        }
        entries.Sort((a,b) => { int result = b.LastExitTimeUtc.CompareTo(a.LastExitTimeUtc); return result != 0 ? result : string.CompareOrdinal(a.Name,b.Name); });
        return entries;
    }

    public async Task<string> CopySqliteSaveAsync(string name, string newName)
    {
        newName = NormalizeSaveName(newName);
        if (ReadSqliteSaveCatalog(UserSavePath).Any(entry => string.Equals(entry.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("这个存档名称已被占用。");
        return await CopySqliteArchiveToFolderAsync(GetFullSavePath(name), UserSavePath, newName);
    }

    public static async Task<string> CopySqliteArchiveToFolderAsync(string sourcePath, string targetRoot, string displayName)
    {
        string target = Path.Combine(targetRoot, Guid.NewGuid().ToString("N"), "save.sqlite");
        string id = Path.GetFileName(Path.GetDirectoryName(target));
        bool borrow = instance?.archiveStore != null && string.Equals(instance.archivePath, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase);
        using var owned = borrow ? null : new SqliteSaveArchiveStore(sourcePath);
        ISaveArchiveStore store = borrow ? instance.archiveStore : owned;
        await store.ForkAsync(target, id, displayName);
        return target;
    }
    #endregion
}
