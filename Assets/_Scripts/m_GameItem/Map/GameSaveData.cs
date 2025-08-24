
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
    public Vector2Int ChunkSize
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

    [ShowInInspector]
    public PlanetData Active_PlanetData = new PlanetData();

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
[MemoryPackable]
[System.Serializable]
public partial class PlanetData
{
    //星球名称
    public string PlanetName;
    //星球半径
    public int Radius = 1000;
    //温度偏移值
    public int TemperatureOffset = 0;
    //降雨偏移
    public float RainOffset = 0;
    //海洋高度
    public float OceanHeight = -1;
    //噪声缩放
    public float NoiseScale = 0.01f;

    //星球地图大小
    public Vector2Int MapSize = new Vector2Int(100, 100);

    [ShowInInspector]
    //星球地图数据
    public Dictionary<string, MapSave> MapData_Dict = new();

    public PlanetTimeData TimeData = new PlanetTimeData();

}

[MemoryPackable]
[System.Serializable]
public partial class PlanetTimeData
{
    [Header("时间相关")]
    public float DayTime = 0;
    public float SeasonTime = 0;
    public float YearTime = 0;
    [Header("计数相关")]
    public float Day = 0;//1440 进一
    public float Season = 0;//8640 进一
    public float Year = 0; //34560 进一

    [Header("时间常数")]
    [Tooltip("一天时间")]
    public  float OneDayTime = 1440;//一天1440秒 60*24 单位秒 1分钟60秒 24分钟
    [Tooltip("一季时间")]
    public  float OneSeasonTime = 8640;//一季8640秒  6天为一季 24*6*60 单位秒 1天60分钟 6天24小时
    [Tooltip("一年时间")]
    public  float OneYearTime = 34560;//一年31536秒 

    [Tooltip("自转速度")]
    public float RotationSpeed = 1f;
    [Tooltip("公转速度")]
    public float OrbitSpeed = 1f;

    [Header("季节相关参数")]
    [Tooltip("春季-日出日落阈值")]//白天黑夜1:1
    public Vector2 SunriseSunsetThreshold_Spring = new Vector2(0.3f, 0.2f);
    [Tooltip("夏季-日出日落阈值")]//白天黑夜1.5:1
    public Vector2 SunriseSunsetThreshold_Summer = new Vector2(0.2f, 0.1f);
    [Tooltip("秋季-日出日落阈值")]
    public Vector2 SunriseSunsetThreshold_Autumn = new Vector2(0.3f, 0.2f);
    [Tooltip("冬季-日出日落阈值")]
    public Vector2 SunriseSunsetThreshold_Winter = new Vector2(0.5f, 0.4f);
}