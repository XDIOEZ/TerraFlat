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

    [Tooltip("当前激活星球名称")]
    public string Active_PlanetName = "地球";

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


    // ===== 建筑与场景切换表 =====
    public Dictionary<string, Vector2> Scenen_Building_Pos = new();
    public Dictionary<string, string> Scenen_Building_Name = new();


    // ===== 星球数据 =====
    [ShowInInspector]
    public Dictionary<string, PlanetData> PlanetData_Dict = new();


    // ===== 快捷访问属性 =====

    /// <summary> 当前激活星球的地图块大小 </summary>
    [ShowInInspector]
    public Vector2 ChunkSize => Active_PlanetData?.MapSize ?? Vector2.one;

    /// <summary> 当前激活星球的数据 </summary>
    [ShowInInspector]
    public PlanetData Active_PlanetData
    {
        get
        {
            if (PlanetData_Dict != null &&
                PlanetData_Dict.TryGetValue(Active_PlanetName, out var planetData) &&
                planetData != null)
            {
                return planetData;
            }

            return null;
        }
        set
        {
            if (PlanetData_Dict != null)
            {
                PlanetData_Dict[Active_PlanetName] = value;
            }
        }
    }

    /// <summary> 当前激活星球的所有地图数据 </summary>
    [ShowInInspector]
    public Dictionary<string, MapSave> Active_MapsData_Dict
    {
        get => Active_PlanetData?.MapData_Dict ?? new Dictionary<string, MapSave>();
        set
        {
            if (Active_PlanetData != null)
                Active_PlanetData.MapData_Dict = value;
        }
    }

    /// <summary> 当前激活地图的数据 </summary>
    public MapSave Active_MapData
    {
        get
        {
            if (Active_MapsData_Dict != null &&
                Active_MapsData_Dict.TryGetValue(Active_MapName, out var mapSave))
            {
                return mapSave;
            }

            return null;
        }
    }


    // ===== 构造函数 =====
    public GameSaveData()
    {
        Active_MapsData_Dict = new Dictionary<string, MapSave>();
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}
