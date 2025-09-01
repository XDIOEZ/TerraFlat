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

    // ===== ���캯�� =====
    public GameSaveData()
    {
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}
