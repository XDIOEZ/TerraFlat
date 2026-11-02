using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>保存每次缩图的不可覆盖原图快照，并在校验通过后恢复图片和导入设置。</summary>
public static partial class FlatWorldPixelArtTextureOptimizer
{
    #region 持久原图快照

    [Serializable]
    private sealed class BackupRecord
    {
        public int version = 1;
        public string createdUtc = DateTime.UtcNow.ToString("O");
        public string directory;
        public string sourcePath;
        public string guid;
        public string sourceHash;
        public string sourceMetaHash;
        public string optimizedHash;
        public string optimizedMetaHash;
        public string state = "prepared";
    }

    private static BackupRecord CreateBackup(Entry entry, byte[] source, byte[] meta, byte[] optimized)
    {
        string directory = BackupRoot + "/" + entry.guid + "/" +
                           DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "_" + Guid.NewGuid().ToString("N");
        var record = new BackupRecord { directory = directory, sourcePath = entry.path, guid = entry.guid,
            sourceHash = entry.sourceHash, sourceMetaHash = entry.metaHash, optimizedHash = Hash(optimized) };
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(directory + "/original.png", source);
        File.WriteAllBytes(directory + "/original.png.meta", meta);
        VerifyBackup(record);
        SaveBackupRecord(record);
        return record;
    }

    private static void VerifyBackup(BackupRecord record)
    {
        string root = Path.GetFullPath(BackupRoot) + Path.DirectorySeparatorChar;
        string directory = Path.GetFullPath(record.directory) + Path.DirectorySeparatorChar;
        if (record.version != 1 || !directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(record.guid) || record.guid.Length != 32 ||
            record.guid.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("备份路径或版本无效，拒绝写入资源。");
        if (Hash(File.ReadAllBytes(record.directory + "/original.png")) != record.sourceHash ||
            Hash(File.ReadAllBytes(record.directory + "/original.png.meta")) != record.sourceMetaHash)
            throw new InvalidDataException("原图备份校验失败，拒绝覆盖工作图。");
    }

    private static void SaveBackupRecord(BackupRecord record) =>
        AtomicWrite(record.directory + "/record.json", Encoding.UTF8.GetBytes(JsonUtility.ToJson(record, true)));

    private static bool IsOptimizedWorkingCopy(string guid, string sourceHash)
    {
        string root = BackupRoot + "/" + guid;
        if (!Directory.Exists(root)) return false;
        BackupRecord latest = Directory.EnumerateFiles(root, "record.json", SearchOption.AllDirectories)
            .Select(path => JsonUtility.FromJson<BackupRecord>(File.ReadAllText(path)))
            .Where(record => record != null && record.guid == guid)
            .OrderByDescending(record => record.createdUtc, StringComparer.Ordinal).FirstOrDefault();
        return latest?.state == "applied" && latest.optimizedHash == sourceHash;
    }

    private static string Hash(byte[] bytes)
    {
        using SHA256 algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".fwtexture-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    #endregion

    #region 恢复与冲突保护

    public static Report Restore(bool selectedOnly)
    {
        RequireEditMode();
        var report = new Report { operation = "restore" };
        if (!Directory.Exists(BackupRoot)) return report;
        HashSet<string> selected = selectedOnly ? Selection.GetFiltered<Texture2D>(SelectionMode.DeepAssets)
            .Select(texture => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture)))
            .ToHashSet(StringComparer.Ordinal) : null;
        var records = new List<BackupRecord>();
        foreach (string path in Directory.EnumerateFiles(BackupRoot, "record.json", SearchOption.AllDirectories))
        {
            try
            {
                BackupRecord record = JsonUtility.FromJson<BackupRecord>(File.ReadAllText(path));
                if (record == null || Path.GetFullPath(record.directory) != Path.GetDirectoryName(Path.GetFullPath(path)))
                    throw new InvalidDataException("备份记录目录不匹配。");
                if (selected == null || selected.Contains(record.guid)) records.Add(record);
            }
            catch (Exception error) { report.errors.Add(path + "：" + error.Message); }
        }
        BackupRecord[] latest = records.GroupBy(record => record.guid).Select(group =>
            group.OrderByDescending(record => record.createdUtc, StringComparer.Ordinal).First())
            .Where(record => record.state == "applied" || record.state == "prepared").ToArray();
        report.inspectedCount = latest.Length;
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            for (int index = 0; index < latest.Length; index++)
            {
                BackupRecord record = latest[index];
                if (EditorUtility.DisplayCancelableProgressBar("恢复高清原图", record.sourcePath, (float)index / latest.Length))
                { report.cancelled = true; break; }
                try { RestoreRecord(record); report.changedCount++; }
                catch (Exception error) { report.errors.Add(record.sourcePath + "：" + error.Message); }
            }
        }
        finally
        {
            AssetDatabase.AllowAutoRefresh();
            EditorUtility.ClearProgressBar();
        }
        WriteReport(report);
        Debug.Log($"[贴图优化] 恢复 {report.changedCount} 张，跳过/失败 {report.errors.Count} 张；所有原图快照继续保留。报告：{ReportPath}");
        return report;
    }

    private static void RestoreRecord(BackupRecord record)
    {
        VerifyBackup(record);
        string path = AssetDatabase.GUIDToAssetPath(record.guid);
        if (string.IsNullOrEmpty(path) || !path.StartsWith(GeneratedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("工作图不存在或已移到优化范围外，不会覆盖其他资源。");
        byte[] current = File.ReadAllBytes(path);
        byte[] currentMeta = File.ReadAllBytes(path + ".meta");
        string hash = Hash(current), metaHash = Hash(currentMeta);
        if (hash == record.sourceHash && metaHash == record.sourceMetaHash)
        { record.state = "restored"; SaveBackupRecord(record); return; }
        if ((hash != record.optimizedHash && hash != record.sourceHash) ||
            (metaHash != record.optimizedMetaHash && metaHash != record.sourceMetaHash))
            throw new InvalidOperationException("压缩后图片或导入设置又被修改，已跳过以保护新改动；原图仍可从备份目录取回。");
        try
        {
            AtomicWrite(path, File.ReadAllBytes(record.directory + "/original.png"));
            AtomicWrite(path + ".meta", File.ReadAllBytes(record.directory + "/original.png.meta"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.AssetPathToGUID(path) != record.guid ||
                !AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Any())
                throw new InvalidDataException("恢复后的 Sprite 引用无效。");
            record.state = "restored";
            SaveBackupRecord(record);
        }
        catch
        {
            AtomicWrite(path, current);
            AtomicWrite(path + ".meta", currentMeta);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            throw;
        }
    }

    #endregion
}
