
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

    public string Active_MapName = "(0,0)";//当前激活场景名称

    public Vector2Int Active_MapPos = new Vector2Int(0, 0);//当前激活地图位置
    //种子
    public string SaveSeed = "0";
    //随机种子
    public int Seed;

    public float Time = 0;

    public float TimeSpeed = 1f; // 每秒流逝的游戏时间（可以外部调节）

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
    public Vector2 ChunkSize
    {
        get
        {
            return Active_PlanetData.MapSize;
        }
    }

    //当前激活星球数据
    [ShowInInspector]
    public Dictionary<string, MapSave> Active_MapsData_Dict
    {
        get
        {
/*            if (PlanetData_Dict == null ||
                !PlanetData_Dict.ContainsKey(Active_PlanetName) ||
                PlanetData_Dict[Active_PlanetName] == null ||
                PlanetData_Dict[Active_PlanetName].MapData_Dict == null)
            {
                // Return a default value or throw an exception
                return new Dictionary<string, MapSave>();
                // Alternatively:
                // throw new System.Exception("Unable to get Active_MapsData_Dict - dictionary or planet data is null");
            }*/
            return Active_PlanetData.MapData_Dict;
        }
        set
        {
/*            if (PlanetData_Dict == null ||
                !PlanetData_Dict.ContainsKey(Active_PlanetName) ||
                PlanetData_Dict[Active_PlanetName] == null)
            {
                // Handle the error case - either create new entries or throw
                // For example:
                // throw new System.Exception("Cannot set Active_MapsData_Dict - dictionary or planet data is null");
                return;
            }*/
            Active_PlanetData.MapData_Dict = value;
        }
    }

    public MapSave Active_MapData
    {
        get
        {   if(!Active_MapsData_Dict.ContainsKey(Active_MapName))
            {
                return null;
            }
            return Active_MapsData_Dict[Active_MapName];
        }
    }
    [ShowInInspector]
    public PlanetData Active_PlanetData
    {
        get
        {
            return PlanetData_Dict[Active_PlanetName];
        }

        set
        {
            PlanetData_Dict[Active_PlanetName] = value;
        }
    }


    /*  {
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
      }*/


    //构造函数
    public GameSaveData()
    {
        Active_MapsData_Dict = new Dictionary<string, MapSave>();
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}