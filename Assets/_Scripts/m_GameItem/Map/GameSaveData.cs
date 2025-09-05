using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class GameSaveData
{
    // ===== 基础存档信息 =====
    [Tooltip("当前加载着的存档名称")]
    public string saveName = "defaultSaveName";

    [Tooltip("存档种子（字符串版本）")]
    public string SaveSeed = "0";

    [Tooltip("存档随机种子（整数版本）")]
    public int Seed;

    [Tooltip("累计游戏时间")]
    public float Time = 0;

    [Tooltip("时间流速（每秒流逝多少游戏时间）")]
    public float TimeSpeed = 1f;


    // ===== 玩家相关 =====
    [ShowInInspector]
    public Dictionary<string, Data_Player> PlayerData_Dict = new();

    [ShowInInspector]
    // ===== 建筑与场景切换表 =====
    public Dictionary<string, MapSave> MapInScene = new();


    // ===== 星球数据 =====
    [ShowInInspector]
    public Dictionary<string, PlanetData> PlanetData_Dict = new();

    // ===== 构造函数 =====
    public GameSaveData()
    {
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}
