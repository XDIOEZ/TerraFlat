using Sirenix.OdinInspector;
using System.Collections.Generic;
using MemoryPack;
using Newtonsoft.Json;

public partial class GameSaveData
{
    #region 世界生态存档
    [ShowInInspector]
    [MemoryPack.MemoryPackOrder(20)] public Dictionary<string, MonsterSpawnerSaveData> MonsterSpawnerWorlds = new();

    [MemoryPackIgnore, JsonIgnore]
    public MonsterSpawnerSaveData MonsterSpawnerData
    {
        get => GetMonsterSpawnerData(GameplayWorldRegistry.ExecutionWorldKey);
        set => MonsterSpawnerWorlds[GameplayWorldRegistry.ExecutionWorldKey] = value ?? new MonsterSpawnerSaveData();
    }

    // 生态进度与居民按真实世界保存，主机切洞穴不会覆盖其它星球。
    public MonsterSpawnerSaveData GetMonsterSpawnerData(string world)
    {
        MonsterSpawnerWorlds ??= new();
        if (!MonsterSpawnerWorlds.TryGetValue(world ?? string.Empty, out MonsterSpawnerSaveData state))
            MonsterSpawnerWorlds[world ?? string.Empty] = state = new MonsterSpawnerSaveData();
        return state;
    }
    #endregion
}
