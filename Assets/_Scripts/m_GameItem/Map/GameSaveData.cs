
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

    public string ActiveSceneName = "平原";//当前激活场景名称

    public Vector2Int ActiveMapPos = new Vector2Int(0, 0);//当前激活地图位置

    public Vector2Int MapSize = new Vector2Int(100, 100);//地图大小

    public string leaveTime = "0";
    //地图种子
    public string MapSeed = "0";

    [ShowInInspector]
    //存档数据结构
    public Dictionary<string, MapSave> MapSaves_Dict = new();
    //玩家数据
    [ShowInInspector]
    public Dictionary<string, Data_Player> PlayerData_Dict = new();
    //建筑与场景之间的切换表_位置
    [ShowInInspector]
    public Dictionary<string, Vector2> Scenen_Building_Pos = new();
    //建筑与场景之间的切换表_名字
    [ShowInInspector]
    public Dictionary<string, string> Scenen_Building_Name = new();
    [Tooltip("当前激活场景的名称")]


    //构造函数
    public GameSaveData()
    {
        MapSaves_Dict = new Dictionary<string, MapSave>();
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}