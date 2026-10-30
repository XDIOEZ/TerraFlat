using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>存档文件的命名、原子读写、备份与后台写入版本。</summary>
public partial class SaveDataMgr
{
    #region 磁盘路径与写入版本
    private static readonly object SaveRevisionLock = new object();
    private static readonly Dictionary<string, long> LatestSaveRevisions =
        new(StringComparer.OrdinalIgnoreCase);
    private static long nextSaveRevision;
    private string pendingBackgroundSavePath;
    private long pendingBackgroundSaveRevision;
    #endregion

    #region 磁盘读写与存档改名
    /// <summary>
    /// 将游戏存档数据保存到磁盘
    /// </summary>
    /// <param name="saveData">存档数据</param>
    /// <param name="savePath">保存路径</param>
    /// <param name="saveName">保存名称</param>
    public bool SaveToDisk(GameSaveData saveData, string savePath, string saveName)
    {
        if (saveData == null || string.IsNullOrWhiteSpace(saveName)) return false;
        try
        {
            saveData.saveName = saveName;
            saveData.ArchiveId ??= Guid.NewGuid().ToString("N");
            string path = Path.Combine(savePath, saveData.ArchiveId, "save.sqlite");
            ReserveSaveRevision(path);
            return SaveSqliteArchive(saveData, path);
        }
        catch (Exception exception)
        {
            LastArchiveError = exception.Message;
            Debug.LogException(exception);
            return false;
        }
    }

    /// <summary>
    /// 为新世界分配一个安全且未被占用的存档名，并立即创建首个存档文件。
    /// 新建世界不会覆盖任何已有世界；重名时自动追加编号。
    /// </summary>
    public bool TryCreateNewSave(GameSaveData saveData, string requestedName, out string createdSaveName)
    {
        createdSaveName = GetAvailableSaveName(requestedName);
        saveData.ArchiveId = Guid.NewGuid().ToString("N");
        return SaveToDisk(saveData, UserSavePath, createdSaveName);
    }

    #region 存档与角色改名

    /// <summary>只修改角色显示名，字典键、运行时玩家索引和控制角色 ID 保持稳定。</summary>
    public bool TryRenamePlayerDisplayName(string profileId, string requestedName)
        => TryRenamePlayerDisplayName(profileId, requestedName, out _);

    // 改名拒绝保留真实校验或写盘阶段，界面不再猜测名称和磁盘原因。
    public bool TryRenamePlayerDisplayName(string profileId, string requestedName, out string reason)
    {
        reason = null;
        string displayName = requestedName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        { reason = "名称不能为空"; return false; }
        if (displayName.Length > 48)
        { reason = "名称不能超过48个字符。"; return false; }
        if (SaveData?.PlayerData_Dict == null)
        { reason = "当前存档尚未载入，无法修改角色名称。"; return false; }
        if (string.IsNullOrWhiteSpace(profileId) ||
            !SaveData.PlayerData_Dict.TryGetValue(profileId, out Data_Player playerData) || playerData == null)
        { reason = "所选角色已不存在，请重新选择。"; return false; }
        if (string.Equals(playerData.Name_User, displayName, StringComparison.Ordinal))
            return true;
        foreach (KeyValuePair<string, Data_Player> profile in SaveData.PlayerData_Dict)
        {
            if (!string.Equals(profile.Key, profileId, StringComparison.Ordinal) &&
                string.Equals(profile.Value?.Name_User, displayName, StringComparison.OrdinalIgnoreCase))
            { reason = "当前存档中已有同名角色。"; return false; }
        }

        string previousName = playerData.Name_User;
        playerData.Name_User = displayName;
        if (SaveToDisk(SaveData, UserSavePath, SaveData.saveName))
            return true;

        playerData.Name_User = previousName;
        reason = "角色名称写盘失败，已保留原名称。";
        return false;
    }

    /// <summary>先写入新文件及内部名称，再移除旧文件；失败时保留旧档。</summary>
    public bool TryRenameSave(string oldName, string requestedName, out string renamedSaveName)
        => TryRenameSave(oldName, requestedName, out renamedSaveName, out _);

    public bool TryRenameSave(string oldName, string requestedName, out string renamedSaveName, out string reason)
    {
        renamedSaveName = oldName;
        reason = null;
        string newName = requestedName?.Trim();
        if (SaveData == null)
        { reason = "当前存档尚未载入，无法改名。"; return false; }
        if (string.IsNullOrWhiteSpace(oldName) ||
            !string.Equals(SaveData.saveName, oldName, StringComparison.Ordinal))
        { reason = "当前选择的存档已变化，请重新选择。"; return false; }
        if (string.IsNullOrWhiteSpace(newName))
        { reason = "名称不能为空"; return false; }
        if (newName.Length > 48)
        { reason = "名称不能超过48个字符。"; return false; }
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return true;

        string oldPath = GetSaveFilePath(UserSavePath, oldName);
        if (!File.Exists(oldPath))
        { reason = "原存档文件已不存在，请刷新存档列表。"; return false; }
        foreach (var entry in ReadSqliteSaveCatalog(UserSavePath))
            if (!string.Equals(entry.Path, oldPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Name, newName, StringComparison.OrdinalIgnoreCase))
            { reason = "这个存档名称已被存档或恢复文件占用。"; return false; }
        if (!SaveToDisk(SaveData, UserSavePath, newName))
        { SaveData.saveName = oldName; reason = "新存档文件写入失败，已保留原名称。"; return false; }
        renamedSaveName = newName;
        return true;
    }

    #endregion

    private string GetAvailableSaveName(string requestedName)
    {
        string normalizedName = NormalizeSaveName(requestedName);
        string candidate = normalizedName;
        int suffix = 2;

        while (File.Exists(GetSaveFilePath(UserSavePath, candidate)))
        {
            candidate = $"{normalizedName} ({suffix})";
            suffix++;
        }

        return candidate;
    }

    private static string NormalizeSaveName(string requestedName)
    {
        string value = requestedName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) value = NewWorldCreationRequest.CreateRandomWorldName();

        const int maxLength = 48;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    /// <summary>
    /// 从磁盘加载存档
    /// </summary>
    /// <param name="loadSavePath">存档路径</param>
    public bool LoadSaveByDisk(string loadSavePath)
    {
        SaveData = null;
        CloseSqliteArchive();
        ResetChunkDifferenceState();
        CurrentContrrolPlayerName = string.Empty;
        try
        {
            string path = NormalizeSavePath(loadSavePath);
            if (path.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                throw new SaveVersionIncompatibleException("开发期 .bytes 存档不支持导入，请创建 SQLite 新世界；原文件已保留。");
            if (!File.Exists(path)) return false;
            return LoadSqliteArchive(path);
        }
        catch (Exception exception)
        {
            LastArchiveError = exception.Message;
            CloseSqliteArchive();
            SaveData = null;
            Debug.LogException(exception);
            return false;
        }
    }

    /// <summary>
    /// 删除指定存档
    /// </summary>
    /// <param name="savePath">存档路径</param>
    /// <param name="saveName">存档名称</param>
    public void DeleteSave(string savePath, string saveName)
    {
        string fullPath = GetSaveFilePath(savePath, saveName);
        DeleteSaveFile(fullPath);
    }

    /// <summary>
    /// 删除指定存档文件（已废弃，使用DeleteSave）
    /// </summary>
    /// <param name="fullPath">完整的存档文件路径</param>
    [System.Obsolete("使用 DeleteSave(path, name) 代替", false)]
    public void DeletSave(string fullPath)
    {
        DeleteSaveFile(fullPath);
    }

    /// <summary>
    /// 删除存档文件的内部方法
    /// </summary>
    private void DeleteSaveFile(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return;
        if (!string.Equals(Path.GetFileName(fullPath), "save.sqlite", StringComparison.Ordinal))
            throw new InvalidOperationException("旧开发存档不会由 SQLite 存档菜单删除。");
        string directory = Path.GetFullPath(Path.GetDirectoryName(fullPath));
        string parent = Path.GetDirectoryName(directory);
        if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) ||
            !string.Equals(parent, Path.GetFullPath(UserSavePath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("存档目录身份不匹配。");
        if (string.Equals(archivePath, fullPath, StringComparison.OrdinalIgnoreCase)) CloseSqliteArchive();
        Directory.Delete(directory, true);
    }

    /// <summary>
    /// 获取默认存档路径
    /// </summary>
    /// <returns>默认存档路径</returns>
    public static string GetDefaultSavePath()
    {
        return Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData") + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// 获取存档文件完整路径
    /// </summary>
    /// <param name="saveName">存档名称</param>
    /// <returns>完整路径</returns>
    public string GetFullSavePath(string saveName)
    {
        return GetSaveFilePath(UserSavePath, saveName);
    }

    /// <summary>
    /// 构建存档文件路径
    /// </summary>
    private string GetSaveFilePath(string basePath, string saveName)
    {
        if (SaveData?.saveName == saveName && !string.IsNullOrWhiteSpace(SaveData.ArchiveId))
            return Path.Combine(basePath, SaveData.ArchiveId, "save.sqlite");
        foreach (var entry in ReadSqliteSaveCatalog(basePath))
            if (string.Equals(entry.Name, saveName, StringComparison.OrdinalIgnoreCase)) return entry.Path;
        return Path.Combine(basePath, "missing", "save.sqlite");
    }

    // 时间与逻辑提交共用数据库元数据。
    public static DateTime GetLastExitTimeUtc(string fullSavePath)
    {
        if (!File.Exists(fullSavePath)) return DateTime.MinValue;
        bool borrow = instance?.archiveStore != null && string.Equals(instance.archivePath, Path.GetFullPath(fullSavePath), StringComparison.OrdinalIgnoreCase);
        using var owned = borrow ? null : new FlatWorld.Persistence.SqliteSaveArchiveStore(fullSavePath);
        FlatWorld.Persistence.ISaveArchiveStore store = borrow ? instance.archiveStore : owned;
        long ticks = store.ReadMetadataAsync().GetAwaiter().GetResult().UpdatedUtcTicks;
        return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
    }

    // 名称与档案 ID 定位统一交给存档服务。
    private string NormalizeSavePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("存档路径为空。", nameof(path));
        if (Directory.Exists(path)) return Path.Combine(path, "save.sqlite");
        if (Path.HasExtension(path)) return path;
        return GetSaveFilePath(UserSavePath, Path.GetFileName(path));
    }

    /// <summary>
    /// 确保目录存在
    /// </summary>
    private void EnsureDirectoryExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("存档目录为空", nameof(path));

        Directory.CreateDirectory(path);
    }

    /// <summary>退出世界时使旧后台保存失效，不等待磁盘任务，也不影响接下来的正式退出保存。</summary>
    public void InvalidatePendingBackgroundSave()
    {
        string fullPath = pendingBackgroundSavePath;
        long revision = pendingBackgroundSaveRevision;
        pendingBackgroundSavePath = null;
        pendingBackgroundSaveRevision = 0;
        if (!string.IsNullOrEmpty(fullPath) && IsSaveRevisionCurrent(fullPath, revision))
            ReserveSaveRevision(fullPath);
    }

    /// <summary>
    /// 为同一存档文件登记最新快照版本；版本锁与文件锁分离，避免主线程等待后台磁盘写入。
    /// </summary>
    private static long ReserveSaveRevision(string fullPath)
    {
        string saveKey = Path.GetFullPath(fullPath);
        lock (SaveRevisionLock)
        {
            long revision = ++nextSaveRevision;
            LatestSaveRevisions[saveKey] = revision;
            if (instance?.archiveStore != null && string.Equals(instance.archivePath, saveKey, StringComparison.OrdinalIgnoreCase))
                instance.ReserveArchiveSnapshotRevision(saveKey);
            return revision;
        }
    }

    /// <summary>判断后台自动保存的快照是否仍是该文件的最新版本。</summary>
    private static bool IsSaveRevisionCurrent(string fullPath, long saveRevision)
    {
        string saveKey = Path.GetFullPath(fullPath);
        lock (SaveRevisionLock)
        {
            return LatestSaveRevisions.TryGetValue(saveKey, out long latestRevision) &&
                   latestRevision == saveRevision;
        }
    }

    #endregion
}
