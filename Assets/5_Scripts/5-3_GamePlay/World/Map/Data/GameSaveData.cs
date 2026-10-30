using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[System.Serializable]
public partial class GameSaveData
{
    // ===== 基础存档信息 =====
    [Tooltip("当前加载着的存档名称")]
    [MemoryPack.MemoryPackOrder(28)] public string saveName = "defaultSaveName";

    [Tooltip("存档种子（字符串版本）")]
    [MemoryPack.MemoryPackOrder(23)] public string SaveSeed = "0";

    [Tooltip("存档随机种子（整数版本）")]
    [MemoryPack.MemoryPackOrder(24)] public int Seed;

    [Tooltip("累计游戏时间")]
    [MemoryPack.MemoryPackOrder(26)] public float Time = 0;

    [Tooltip("时间流速（每秒流逝多少游戏时间）")]
    [MemoryPack.MemoryPackOrder(27)] public float TimeSpeed = 1f;


    // ===== 玩家相关 =====
    [ShowInInspector]
    [MemoryPackIgnore] public Dictionary<string, Data_Player> PlayerData_Dict = new();
    [MemoryPackInclude, MemoryPackOrder(22)]
    private Dictionary<string, ItemInstanceSnapshot> SavedPlayers
    {
        get
        {
            var players = new Dictionary<string, ItemInstanceSnapshot>(PlayerData_Dict.Count);
            foreach (var pair in PlayerData_Dict) players.Add(pair.Key, ItemSnapshotSerialization.Capture(pair.Value));
            return players;
        }
        set
        {
            PlayerData_Dict = new();
            if (value != null)
                foreach (var pair in value) PlayerData_Dict.Add(pair.Key, (Data_Player)pair.Value.CreateColdData());
        }
    }

    // ===== 星球数据 =====
    [ShowInInspector]
    [MemoryPack.MemoryPackOrder(21)] public Dictionary<string, PlanetData> PlanetData_Dict = new();
    [ShowInInspector]
    [MemoryPack.MemoryPackOrder(16)] public DayTimeSaveData DayTimeData = new();

    [MemoryPackIgnore]
    public PlanetData CurrentPlanetData => PlanetData_Dict != null && PlanetData_Dict.TryGetValue(
        GameplayWorldRegistry.ExecutionWorldKey ?? string.Empty, out PlanetData worldData) ? worldData : null;

    // ===== 构造函数 =====
    public GameSaveData()
    {
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}
