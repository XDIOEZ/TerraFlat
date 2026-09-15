using System;
using System.Collections.Generic;

/// <summary>
/// 存档级世界生成配置策略。
/// 关闭冻结时只清除生成配置快照，保留自然物删除、状态覆盖和恢复年份等区块差量。
/// </summary>
public partial class SaveDataMgr
{
    private readonly struct EcologyConfigurationBackup
    {
        public EcologyConfigurationBackup(EcologyWorldSaveData ecology)
        {
            Ecology = ecology;
            DataVersion = ecology.DataVersion;
            ProfileId = ecology.ProfileId;
            ConfigurationFingerprint = ecology.ConfigurationFingerprint;
            GlobalMultiplier = ecology.GlobalMultiplier;
            Rules = ecology.Rules;
            Generation = ecology.Generation;
        }

        public EcologyWorldSaveData Ecology { get; }
        public int DataVersion { get; }
        public string ProfileId { get; }
        public ulong ConfigurationFingerprint { get; }
        public double GlobalMultiplier { get; }
        public List<EcologyRuleSaveData> Rules { get; }
        public WorldGenerationProfileSaveData Generation { get; }
    }

    /// <summary>修改当前已加载存档的生成配置冻结模式，并立即原子写回磁盘。</summary>
    public bool TrySetWorldGenerationConfigurationFrozen(bool frozen)
    {
        if (SaveData == null || string.IsNullOrWhiteSpace(SaveData.saveName))
        {
            UnityEngine.Debug.LogWarning("[SaveDataMgr] 当前没有可修改生成配置模式的存档。");
            return false;
        }

        WorldGenerationConfigurationMode targetMode = frozen
            ? WorldGenerationConfigurationMode.Frozen
            : WorldGenerationConfigurationMode.FollowCurrent;
        if (SaveData.WorldGenerationConfigMode == targetMode)
            return true;

        WorldGenerationConfigurationMode previousMode = SaveData.WorldGenerationConfigMode;
        var backups = new List<EcologyConfigurationBackup>();

        try
        {
            SaveData.WorldGenerationConfigMode = targetMode;
            if (!frozen)
                ClearFrozenWorldGenerationConfiguration(backups);

            if (SaveToDisk(SaveData, UserSavePath, SaveData.saveName))
                return true;

            RestoreFrozenWorldGenerationConfiguration(previousMode, backups);
            return false;
        }
        catch (Exception exception)
        {
            RestoreFrozenWorldGenerationConfiguration(previousMode, backups);
            UnityEngine.Debug.LogError($"[SaveDataMgr] 修改世界生成配置模式失败：{exception.Message}");
            UnityEngine.Debug.LogException(exception);
            return false;
        }
    }

    /// <summary>清除所有维度的冻结快照，但不触碰生态区块差量。</summary>
    private void ClearFrozenWorldGenerationConfiguration(
        List<EcologyConfigurationBackup> backups)
    {
        if (SaveData?.PlanetData_Dict == null)
            return;

        foreach (PlanetData planet in SaveData.PlanetData_Dict.Values)
        {
            EcologyWorldSaveData ecology = planet?.Ecology;
            if (ecology == null)
                continue;

            backups.Add(new EcologyConfigurationBackup(ecology));
            ecology.ClearFrozenConfiguration();
        }
    }

    /// <summary>写盘失败时恢复本次修改前的冻结配置引用与模式。</summary>
    private void RestoreFrozenWorldGenerationConfiguration(
        WorldGenerationConfigurationMode previousMode,
        List<EcologyConfigurationBackup> backups)
    {
        if (SaveData == null)
            return;

        SaveData.WorldGenerationConfigMode = previousMode;
        for (int i = 0; i < backups.Count; i++)
        {
            EcologyConfigurationBackup backup = backups[i];
            EcologyWorldSaveData ecology = backup.Ecology;
            if (ecology == null)
                continue;

            ecology.DataVersion = backup.DataVersion;
            ecology.ProfileId = backup.ProfileId;
            ecology.ConfigurationFingerprint = backup.ConfigurationFingerprint;
            ecology.GlobalMultiplier = backup.GlobalMultiplier;
            ecology.Rules = backup.Rules;
            ecology.Generation = backup.Generation;
        }
    }
}
