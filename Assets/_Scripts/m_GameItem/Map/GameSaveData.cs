
using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class GameSaveData
{
    [Tooltip("��ǰ�����ŵĴ浵����")]
    public string saveName = "defaultSaveName";//�浵����

    public string Active_PlanetName = "����";//��ǰ������������

    public string Active_MapName = "ƽԭ";//��ǰ���������

    public Vector2Int Active_MapPos = new Vector2Int(0, 0);//��ǰ�����ͼλ��
    //����
    public string SaveSeed = "0";
    //�������
    public int Seed;

    //�������
    [ShowInInspector]
    public Dictionary<string, Data_Player> PlayerData_Dict = new();

    //�����볡��֮����л���_λ��
    public Dictionary<string, Vector2> Scenen_Building_Pos = new();

    //�����볡��֮����л���_����
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

    //��ǰ������������
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

    //���캯��
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
    //��������
    public string PlanetName;
    //����뾶
    public int PlanetRadius = 1000;
    //�¶�ƫ��ֵ
    public int TemperatureOffset = 0;
    //����ƫ��
    public float RainOffset = 0;
    //����߶�
    public float OceanHeight = 0;
    //��������
    public float NoiseScale = 0.1f;

    //�����ͼ��С
    public Vector2Int MapSize = new Vector2Int(100, 100);

    [ShowInInspector]
    //�����ͼ����
    public Dictionary<string, MapSave> MapData_Dict = new();

}