using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using Newtonsoft.Json.Linq;
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
        ModdedSaveEnvelope envelope = ReadSaveEnvelope(payload);
        if (envelope.ModMetadata != null && envelope.ModMetadata.Length > 0)
        {
            ModSaveMetadata metadata = MemoryPackSerializer.Deserialize<ModSaveMetadata>(envelope.ModMetadata);
            ModRuntimeManager mods = ModRuntimeManager.Instance;
            string error = null;
            if (mods == null || !mods.IsReady || !mods.ValidateSaveMetadata(metadata, out error))
                throw new InvalidDataException(error ?? "测试存档的 MOD 环境不兼容。");
        }
        return DeserializeCoreSavePayload(envelope.CoreSavePayload, new Dictionary<string, ChunkSaveRecord>());
    }

    internal JObject CaptureTestLoadFailureState()
    {
        if (ReferenceEquals(this, instance) || gameObject.activeInHierarchy || !string.IsNullOrEmpty(UserSavePath))
            throw new InvalidOperationException("失败恢复测试必须使用停用独立 SaveDataMgr。");

        // 只用内存载荷和随机不存在的路径，检查正式拒绝路径不污染既有差量。
        var previous = new ChunkSaveRecord { PlanetName = "probe:previous", ChunkName = "0_0" };
        string previousKey = BuildChunkKey(previous.PlanetName, previous.ChunkName);
        chunkDeltas.Add(previousKey, previous);
        var source = new GameSaveData { saveName = "probe:current", Seed = 2468 };
        byte[] valid = BuildCapturedSavePayload(MemoryPackSerializer.Serialize(source),
            new List<byte[]>(), null, null, null, new List<FrozenEcologyChunkSaveRecord>());
        GameSaveData restored = ReadTestArchiveWithoutPublishing(valid);

        ModdedSaveEnvelope oldEnvelope = ReadSaveEnvelope(valid);
        oldEnvelope.CoreSavePayload.Version = 31;
        oldEnvelope.CoreSavePayload.CoreSaveData = new byte[] { 30 };
        byte[] oldPayload = AddSaveHeader(MemoryPackSerializer.Serialize(oldEnvelope), ModdedSaveMagic);
        string oldFailure = CaptureFailure(oldPayload);
        bool oldPreserved = HasPreviousDelta();

        var first = new ChunkSaveRecord { PlanetName = "probe:new", ChunkName = "1_0" };
        byte[] corrupt = BuildCapturedSavePayload(MemoryPackSerializer.Serialize(source),
            new List<byte[]> { MemoryPackSerializer.Serialize(first), null },
            null, null, null, new List<FrozenEcologyChunkSaveRecord>());
        string corruptFailure = CaptureFailure(corrupt);
        bool corruptPreserved = HasPreviousDelta();

        SaveData = source;
        CurrentContrrolPlayerName = "probe:previous-player";
        bool missingLoaded = LoadSaveByDisk(Path.Combine(Application.temporaryCachePath,
            "FlatWorldGameplayTests", "missing-" + Guid.NewGuid().ToString("N") + ".bytes"));
        return new JObject
        {
            ["currentVersion"] = ReadSaveEnvelope(valid).CoreSavePayload.Version,
            ["currentName"] = restored.saveName, ["currentSeed"] = restored.Seed,
            ["oldFailure"] = oldFailure, ["oldPreserved"] = oldPreserved,
            ["corruptFailure"] = corruptFailure, ["corruptPreserved"] = corruptPreserved,
            ["missingLoaded"] = missingLoaded, ["saveCleared"] = SaveData == null,
            ["playerCleared"] = string.IsNullOrEmpty(CurrentContrrolPlayerName),
            ["missingPreserved"] = HasPreviousDelta()
        };

        bool HasPreviousDelta() => chunkDeltas.Count == 1 &&
            chunkDeltas.TryGetValue(previousKey, out ChunkSaveRecord record) && ReferenceEquals(record, previous);
        string CaptureFailure(byte[] payload)
        {
            try { DeserializeSavePayload(payload); return null; }
            catch (Exception exception) { return exception.GetType().Name; }
        }
    }

    #endregion
}
