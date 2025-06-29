
using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class GameSaveData
{
    [Tooltip("当前加载着的存档名称")]
    public string saveName = "defaultSaveName";//存档名称

    public string Active_PlanetName = "地球";//当前激活星球名称

    public string Active_MapName = "平原";//当前激活场景名称

    public Vector2Int Active_MapPos = new Vector2Int(0, 0);//当前激活地图位置
    //种子
    public string SaveSeed = "0";
    //随机种子
    public int Seed;

    //玩家数据
    [ShowInInspector]
    public Dictionary<string, Data_Player> PlayerData_Dict = new();

    //建筑与场景之间的切换表_位置
    public Dictionary<string, Vector2> Scenen_Building_Pos = new();

    //建筑与场景之间的切换表_名字
    public Dictionary<string, string> Scenen_Building_Name = new();
  
    [ShowInInspector]
    public Dictionary<string, PlanetData> PlanetData_Dict = new();

    [ShowInInspector]
    public Vector2Int MapSize
    {
        get
        {
            if (PlanetData_Dict == null ||
                !PlanetData_Dict.ContainsKey(Active_MapName) ||
                PlanetData_Dict[Active_MapName] == null)
            {
                // Return a default value or throw an exception based on your needs
                return new Vector2Int(100,100);
                // Alternatively, you could throw an exception:
                // throw new System.Exception("Unable to get MapSize - dictionary or map data is null");
            }
            return PlanetData_Dict[Active_MapName].MapSize;
        }
    }

    //当前激活星球数据
    [ShowInInspector]
    public Dictionary<string, MapSave> Active_MapsData_Dict
    {
        get
        {
            if (PlanetData_Dict == null ||
                !PlanetData_Dict.ContainsKey(Active_PlanetName) ||
                PlanetData_Dict[Active_PlanetName] == null ||
                PlanetData_Dict[Active_PlanetName].MapData_Dict == null)
            {
                // Return a default value or throw an exception
                return new Dictionary<string, MapSave>();
                // Alternatively:
                // throw new System.Exception("Unable to get Active_MapsData_Dict - dictionary or planet data is null");
            }
            return PlanetData_Dict[Active_PlanetName].MapData_Dict;
        }
        set
        {
            if (PlanetData_Dict == null ||
                !PlanetData_Dict.ContainsKey(Active_PlanetName) ||
                PlanetData_Dict[Active_PlanetName] == null)
            {
                // Handle the error case - either create new entries or throw
                // For example:
                // throw new System.Exception("Cannot set Active_MapsData_Dict - dictionary or planet data is null");
                return;
            }
            PlanetData_Dict[Active_PlanetName].MapData_Dict = value;
        }
    }

    public PlanetData Active_PlanetData
    {
        get
        {
            if (PlanetData_Dict == null ||
                !PlanetData_Dict.ContainsKey(Active_PlanetName) ||
                PlanetData_Dict[Active_PlanetName] == null)
            {
                // Return a default value or throw an exception
                return new PlanetData();
                // Alternatively:
                // throw new System.Exception("Unable to get Active_PlanetData - dictionary or planet data is null");
            }
            return PlanetData_Dict[Active_PlanetName];
        }
        set
        {
            if (PlanetData_Dict == null ||
                !PlanetData_Dict.ContainsKey(Active_PlanetName) ||
                PlanetData_Dict[Active_PlanetName] == null)
            {
                // Handle the error case - either create new entries or throw
                // For example:
                // throw new System.Exception("Cannot set Active_PlanetData - dictionary or planet data is null");
                return;
            }
            PlanetData_Dict[Active_PlanetName] = value;
        }
    }

    //构造函数
    public GameSaveData()
    {
        Active_MapsData_Dict = new Dictionary<string, MapSave>();
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}
[MemoryPackable]
[System.Serializable]
public partial class PlanetData
{
    //星球名称
    public string PlanetName;
    //星球半径
    public int PlanetRadius = 1000;
    //温度偏移值
    public int TemperatureOffset = 0;
    //降雨偏移
    public float RainOffset = 0;
    //海洋高度
    public float OceanHeight = 0;
    //噪声缩放
    public float NoiseScale = 0.1f;

    //星球地图大小
    public Vector2Int MapSize = new Vector2Int(100, 100);

    [ShowInInspector]
    //星球地图数据
    public Dictionary<string, MapSave> MapData_Dict = new();

}