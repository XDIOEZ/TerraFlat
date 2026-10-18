using System;
using System.IO;
using MemoryPack;
using UnityEngine;

public partial class SaveDataMgr
{
    #region 测试归档的非发布恢复

    internal static SaveDataMgr TestExistingInstance => instance;

    internal GameSaveData ReadTestArchiveWithoutPublishing(byte[] payload)
    {
        // 仅停用测试外壳可使用，正式管理器的差量和 MOD 状态不得被恢复测试替换。
        if (ReferenceEquals(this, instance) || gameObject.activeInHierarchy || !string.IsNullOrEmpty(UserSavePath))
            throw new InvalidOperationException("恢复测试必须使用尚未 Awake 的停用独立 SaveDataMgr。");
        ResetChunkDifferenceState();
        ModdedSaveEnvelope envelope = ReadSaveEnvelope(payload);
        if (envelope.ModMetadata != null && envelope.ModMetadata.Length > 0)
        {
            ModSaveMetadata metadata = MemoryPackSerializer.Deserialize<ModSaveMetadata>(envelope.ModMetadata);
            ModRuntimeManager mods = ModRuntimeManager.Instance;
            string error = null;
            if (mods == null || !mods.IsReady || !mods.ValidateSaveMetadata(metadata, out error))
                throw new InvalidDataException(error ?? "测试存档的 MOD 环境不兼容。");
        }
        return DeserializeCoreSavePayload(envelope.CoreSavePayload);
    }

    #endregion
}
