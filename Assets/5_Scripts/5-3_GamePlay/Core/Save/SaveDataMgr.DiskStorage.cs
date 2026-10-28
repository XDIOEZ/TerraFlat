using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>存档文件的命名、原子读写、备份与后台写入版本。</summary>
public partial class SaveDataMgr
{
    #region 磁盘路径与写入版本
    private const string TemporarySaveSuffix = ".tmp";
    private const string BackupSaveSuffix = ".bak";
    private const string LastExitTimeSuffix = ".lastplayed";
    private static readonly object SaveFileLock = new object();
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
        if (saveData == null)
        {
            Debug.LogError("❌ saveData为null，无法保存");
            return false;
        }

        if (string.IsNullOrWhiteSpace(saveName))
        {
            Debug.LogError("❌ 存档名称为空，无法保存");
            return false;
        }

        try
        {
            saveData.saveName = saveName;

            // 确保保存路径存在
            EnsureDirectoryExists(savePath);

            string fullPath = GetSaveFilePath(savePath, saveName);
            byte[] dataBytes = BuildCompactSavePayload(saveData);
            long saveRevision = ReserveSaveRevision(fullPath);
            if (!WriteSaveAtomically(fullPath, dataBytes, saveRevision, discardIfSuperseded: false))
                return false;

            Debug.Log($"✅ 存档成功！路径: {fullPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ 保存存档失败: {ex.Message}");
            Debug.LogException(ex);
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
        if (!string.Equals(newName, NormalizeSaveName(newName), StringComparison.Ordinal))
        { reason = "存档名称包含文件名不允许的字符，或以句点结尾。"; return false; }
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return true;

        string oldPath = GetSaveFilePath(UserSavePath, oldName);
        string newPath = GetSaveFilePath(UserSavePath, newName);
        if (!File.Exists(oldPath))
        { reason = "原存档文件已不存在，请刷新存档列表。"; return false; }
        if (File.Exists(newPath) || File.Exists(GetBackupSavePath(newPath)) || File.Exists(GetTemporarySavePath(newPath)))
        { reason = "这个存档名称已被存档或恢复文件占用。"; return false; }

        DateTime lastExitTimeUtc = GetLastExitTimeUtc(oldPath);
        if (!SaveToDisk(SaveData, UserSavePath, newName))
        {
            SaveData.saveName = oldName;
            reason = "新存档文件写入失败，已保留原名称。";
            return false;
        }

        try
        {
            if (lastExitTimeUtc != DateTime.MinValue)
                RecordLastExitTimeUtc(newPath, lastExitTimeUtc);
            // 先让旧路径的后台快照过期，再与原子写盘共用文件锁移除旧档。
            ReserveSaveRevision(oldPath);
            lock (SaveFileLock)
            {
                DeleteSaveFile(oldPath);
                if (File.Exists(oldPath))
                    throw new IOException("旧存档未能删除");
            }
            renamedSaveName = newName;
            return true;
        }
        catch (Exception exception)
        {
            reason = "旧存档或退出记录更新失败，未完成改名。";
            Debug.LogException(exception);
            DeleteSaveFile(newPath);
            SaveData.saveName = oldName;
            return false;
        }
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
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter.ToString(), "_");
        }

        value = value.Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(value) || value == "." || value == "..")
        {
            value = NewWorldCreationRequest.CreateRandomWorldName();
        }

        const int maxLength = 48;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    /// <summary>
    /// 从磁盘加载存档
    /// </summary>
    /// <param name="loadSavePath">存档路径</param>
    public bool LoadSaveByDisk(string loadSavePath)
    {
        // 新选择载入失败时不能继续使用上一份存档和角色。
        SaveData = null;
        CurrentContrrolPlayerName = string.Empty;
        if (string.IsNullOrEmpty(loadSavePath))
        {
            Debug.LogError("❌ 存档路径为空");
            return false;
        }

        try
        {
            string fullPath = NormalizeSavePath(loadSavePath);
            string backupPath = GetBackupSavePath(fullPath);
            string temporaryPath = GetTemporarySavePath(fullPath);
            string[] candidatePaths = { fullPath, backupPath, temporaryPath };
            Exception lastException = null;

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                string candidatePath = candidatePaths[i];
                if (!File.Exists(candidatePath))
                    continue;

                try
                {
                    Debug.Log($"📖 开始加载存档：{candidatePath}");
                    GameSaveData loadedSaveData = DeserializeSavePayload(File.ReadAllBytes(candidatePath));
                    if (loadedSaveData == null)
                        throw new InvalidDataException("存档反序列化结果为空");

                    SaveData = loadedSaveData;
                    if (i == 0)
                        Debug.Log("✅ 存档加载成功");
                    else
                        Debug.LogWarning($"⚠️ 正式存档不可用，已从{(i == 1 ? "备份" : "临时文件")}恢复：{candidatePath}");
                    return true;
                }
                catch (Exception ex)
                {
                    if (ex is SaveVersionIncompatibleException)
                        throw;

                    lastException = ex;
                    Debug.LogWarning($"⚠️ 存档文件无效，尝试恢复文件：{candidatePath}\n{ex.Message}");
                }
            }

            if (lastException != null)
                throw new InvalidDataException("正式存档、备份及临时存档均无法加载", lastException);

            Debug.LogWarning($"⚠️ 存档文件不存在：{fullPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ 加载存档失败: {ex.Message}");
            if (ex is not SaveVersionIncompatibleException)
                Debug.LogException(ex);
        }
        return false;
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
        if (string.IsNullOrEmpty(fullPath))
        {
            Debug.LogError("❌ 存档路径为空");
            return;
        }

        try
        {
            string backupPath = GetBackupSavePath(fullPath);
            string temporaryPath = GetTemporarySavePath(fullPath);
            bool foundSaveFile = File.Exists(fullPath) || File.Exists(backupPath) || File.Exists(temporaryPath);

            DeleteFileIfExists(fullPath);
            DeleteFileIfExists(backupPath);
            DeleteFileIfExists(temporaryPath);
            DeleteFileIfExists(GetLastExitTimePath(fullPath));

            if (foundSaveFile)
            {
                Debug.Log($"✅ 存档已删除：{fullPath}");
            }
            else
            {
                Debug.LogWarning($"⚠️ 未找到要删除的存档文件：{fullPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ 删除存档失败：{fullPath}");
            Debug.LogException(ex);
        }
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
        return Path.Combine(basePath, saveName + ".bytes");
    }

    private static string GetTemporarySavePath(string fullPath)
    {
        return fullPath + TemporarySaveSuffix;
    }

    private static string GetBackupSavePath(string fullPath)
    {
        return fullPath + BackupSaveSuffix;
    }

    private static string GetLastExitTimePath(string fullPath)
    {
        return fullPath + LastExitTimeSuffix;
    }

    /// <summary>
    /// 获取存档最后退出时间。旧存档没有元数据时，使用存档文件修改时间兼容。
    /// </summary>
    public static DateTime GetLastExitTimeUtc(string fullSavePath)
    {
        if (string.IsNullOrWhiteSpace(fullSavePath))
            return DateTime.MinValue;

        try
        {
            string metadataPath = GetLastExitTimePath(fullSavePath);
            if (File.Exists(metadataPath))
            {
                string value = File.ReadAllText(metadataPath);
                if (DateTime.TryParse(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTime timestamp))
                {
                    return timestamp.ToUniversalTime();
                }
            }

            return File.Exists(fullSavePath)
                ? File.GetLastWriteTimeUtc(fullSavePath)
                : DateTime.MinValue;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"读取存档最后退出时间失败，将该存档排到列表末尾：{fullSavePath}\n{ex.Message}");
            return DateTime.MinValue;
        }
    }

    private static void RecordLastExitTimeUtc(string fullSavePath, DateTime exitTimeUtc)
    {
        try
        {
            File.WriteAllText(
                GetLastExitTimePath(fullSavePath),
                exitTimeUtc.ToUniversalTime().ToString(
                    "O",
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"存档已保存，但记录最后退出时间失败：{fullSavePath}\n{ex.Message}");
        }
    }

    /// <summary>
    /// 规范化存档路径（添加.bytes扩展名）
    /// </summary>
    private string NormalizeSavePath(string path)
    {
        return path.EndsWith(".bytes") ? path : path + ".bytes";
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

    /// <summary>
    /// 先将完整存档写入同目录临时文件并强制落盘，再原子替换正式文件。
    /// 正式文件的上一版本会保留为.bak备份。
    /// </summary>
    private static bool WriteSaveAtomically(
        string fullPath,
        byte[] dataBytes,
        long saveRevision,
        bool discardIfSuperseded)
    {
        using var writeSample = SaveWritePoint.Measure();
        writeSample.SetGauge(dataBytes?.LongLength ?? 0);
        string temporaryPath = GetTemporarySavePath(fullPath);
        string backupPath = GetBackupSavePath(fullPath);

        lock (SaveFileLock)
        {
            // 退出/手动保存已生成更新快照时，丢弃尚未开始写入的旧自动保存，
            // 防止后台任务在世界退出后把过期状态覆盖到磁盘。
            if (discardIfSuperseded && !IsSaveRevisionCurrent(fullPath, saveRevision))
                return false;

            DeleteFileIfExists(temporaryPath);

            try
            {
                using (FileStream stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    using var bytesSample = SaveWriteBytesPoint.Measure();
                    stream.Write(dataBytes, 0, dataBytes.Length);
                    bytesSample.AddWork(dataBytes.LongLength);
                    stream.Flush(true);
                }

                if (new FileInfo(temporaryPath).Length != dataBytes.LongLength)
                    throw new IOException("临时存档写入不完整");

                // 退出或更新保存可能发生在写临时文件期间，提交正式文件前再检查一次。
                if (discardIfSuperseded && !IsSaveRevisionCurrent(fullPath, saveRevision))
                    return false;

                if (!File.Exists(fullPath))
                {
                    File.Move(temporaryPath, fullPath);
                    return true;
                }

                DeleteFileIfExists(backupPath);

                try
                {
                    File.Replace(temporaryPath, fullPath, backupPath);
                }
                catch (PlatformNotSupportedException)
                {
                    ReplaceSaveWithBackupFallback(temporaryPath, fullPath, backupPath);
                }
                catch (NotSupportedException)
                {
                    ReplaceSaveWithBackupFallback(temporaryPath, fullPath, backupPath);
                }

                return true;
            }
            finally
            {
                DeleteFileIfExists(temporaryPath);
            }
        }
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

    /// <summary>
    /// 不支持File.Replace的平台使用可恢复的两步替换：旧正式存档先转为备份，再提交新存档。
    /// </summary>
    private static void ReplaceSaveWithBackupFallback(string temporaryPath, string fullPath, string backupPath)
    {
        File.Move(fullPath, backupPath);

        try
        {
            File.Move(temporaryPath, fullPath);
        }
        catch
        {
            if (!File.Exists(fullPath) && File.Exists(backupPath))
                File.Copy(backupPath, fullPath);

            throw;
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
    #endregion
}
