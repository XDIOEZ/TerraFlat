using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    // ===== �������� =====
    [ShowInInspector]
    public Dictionary<string, PlanetData> PlanetData_Dict = new();
    [ShowInInspector]
    public DayTimeSaveData DayTimeData = new();

    [MemoryPackIgnore]
    public PlanetData CurrentPlanetData => PlanetData_Dict[SceneManager.GetActiveScene().name];

    // ===== ���캯�� =====
    public GameSaveData()
    {
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}
