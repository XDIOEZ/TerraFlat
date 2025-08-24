
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

    public string Active_MapName = "(0,0)";//��ǰ���������

    public Vector2Int Active_MapPos = new Vector2Int(0, 0);//��ǰ�����ͼλ��
    //����
    public string SaveSeed = "0";
    //�������
    public int Seed;

    public float Time = 0;

    public float TimeSpeed = 1f; // ÿ�����ŵ���Ϸʱ�䣨�����ⲿ���ڣ�

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
    public Vector2Int ChunkSize
    {
        get
        {
            return Active_PlanetData.MapSize;
        }
    }

    //��ǰ������������
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
    public int Radius = 1000;
    //�¶�ƫ��ֵ
    public int TemperatureOffset = 0;
    //����ƫ��
    public float RainOffset = 0;
    //����߶�
    public float OceanHeight = -1;
    //��������
    public float NoiseScale = 0.01f;

    //�����ͼ��С
    public Vector2Int MapSize = new Vector2Int(100, 100);

    [ShowInInspector]
    //�����ͼ����
    public Dictionary<string, MapSave> MapData_Dict = new();

    public PlanetTimeData TimeData = new PlanetTimeData();

}

[MemoryPackable]
[System.Serializable]
public partial class PlanetTimeData
{
    [Header("ʱ�����")]
    public float DayTime = 0;
    public float SeasonTime = 0;
    public float YearTime = 0;
    [Header("�������")]
    public float Day = 0;//1440 ��һ
    public float Season = 0;//8640 ��һ
    public float Year = 0; //34560 ��һ

    [Header("ʱ�䳣��")]
    [Tooltip("һ��ʱ��")]
    public  float OneDayTime = 1440;//һ��1440�� 60*24 ��λ�� 1����60�� 24����
    [Tooltip("һ��ʱ��")]
    public  float OneSeasonTime = 8640;//һ��8640��  6��Ϊһ�� 24*6*60 ��λ�� 1��60���� 6��24Сʱ
    [Tooltip("һ��ʱ��")]
    public  float OneYearTime = 34560;//һ��31536�� 

    [Tooltip("��ת�ٶ�")]
    public float RotationSpeed = 1f;
    [Tooltip("��ת�ٶ�")]
    public float OrbitSpeed = 1f;

    [Header("������ز���")]
    [Tooltip("����-�ճ�������ֵ")]//�����ҹ1:1
    public Vector2 SunriseSunsetThreshold_Spring = new Vector2(0.3f, 0.2f);
    [Tooltip("�ļ�-�ճ�������ֵ")]//�����ҹ1.5:1
    public Vector2 SunriseSunsetThreshold_Summer = new Vector2(0.2f, 0.1f);
    [Tooltip("�＾-�ճ�������ֵ")]
    public Vector2 SunriseSunsetThreshold_Autumn = new Vector2(0.3f, 0.2f);
    [Tooltip("����-�ճ�������ֵ")]
    public Vector2 SunriseSunsetThreshold_Winter = new Vector2(0.5f, 0.4f);
}