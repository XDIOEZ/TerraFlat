using System;
using System.Collections.Generic;
using MemoryPack;

/// <summary>
/// 独立于 GameSaveData 的 MOD 元数据，避免改变旧 MemoryPack 对象的字段数量。
/// </summary>
[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class ModSaveMetadata
{
    [MemoryPack.MemoryPackOrder(1)] public string ModSetHash = string.Empty;
    [MemoryPack.MemoryPackOrder(2)] public List<ModSaveRecord> Mods = new();
    [MemoryPack.MemoryPackOrder(0)] public Dictionary<string, string> GlobalStates = new();
}
