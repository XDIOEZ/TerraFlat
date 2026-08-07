using System;
using System.Collections.Generic;
using MemoryPack;

/// <summary>
/// 独立于 GameSaveData 的 MOD 元数据，避免改变旧 MemoryPack 对象的字段数量。
/// </summary>
[MemoryPackable]
[Serializable]
public partial class ModSaveMetadata
{
    public string ModSetHash = string.Empty;
    public List<ModSaveRecord> Mods = new();
    public Dictionary<string, string> GlobalStates = new();
}
