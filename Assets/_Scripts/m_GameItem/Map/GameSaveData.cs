using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class GameSaveData
{
    // ===== �����浵��Ϣ =====
    [Tooltip("��ǰ�����ŵĴ浵����")]
    public string saveName = "defaultSaveName";

    [Tooltip("��ǰ������������")]
    public string Active_PlanetName = "����";

    [Tooltip("�浵���ӣ��ַ����汾��")]
    public string SaveSeed = "0";

    [Tooltip("�浵������ӣ������汾��")]
    public int Seed;

    [Tooltip("�ۼ���Ϸʱ��")]
    public float Time = 0;

    [Tooltip("ʱ�����٣�ÿ�����Ŷ�����Ϸʱ�䣩")]
    public float TimeSpeed = 1f;


    // ===== ������ =====
    [ShowInInspector]
    public Dictionary<string, Data_Player> PlayerData_Dict = new();


    // ===== �����볡���л��� =====
    public Dictionary<string, Vector2> Scenen_Building_Pos = new();
    public Dictionary<string, string> Scenen_Building_Name = new();


    // ===== �������� =====
    [ShowInInspector]
    public Dictionary<string, PlanetData> PlanetData_Dict = new();


    // ===== ��ݷ������� =====

    /// <summary> ��ǰ��������ĵ�ͼ���С </summary>
    [ShowInInspector]
    public Vector2 ChunkSize => Active_PlanetData?.MapSize ?? Vector2.one;

    /// <summary> ��ǰ������������� </summary>
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

    /// <summary> ��ǰ������������е�ͼ���� </summary>
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

    /// <summary> ��ǰ�����ͼ������ </summary>
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


    // ===== ���캯�� =====
    public GameSaveData()
    {
        Active_MapsData_Dict = new Dictionary<string, MapSave>();
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}
