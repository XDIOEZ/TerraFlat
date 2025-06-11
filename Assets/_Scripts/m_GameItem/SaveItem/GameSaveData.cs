
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
                                               //[ReadOnly]
    public string ActiveSceneName = "ƽԭ";//��ǰ���������

    public string leaveTime = "0";

    [ShowInInspector]
    //�浵���ݽṹ
    public Dictionary<string, MapSave> MapSaves_Dict = new();
    //�������
    [ShowInInspector]
    public Dictionary<string, Data_Player> PlayerData_Dict = new();
    //�����볡��֮����л���_λ��
    [ShowInInspector]
    public Dictionary<string, Vector2> Scenen_Building_Pos = new();
    //�����볡��֮����л���_����
    [ShowInInspector]
    public Dictionary<string, string> Scenen_Building_Name = new();
    [Tooltip("��ǰ�����������")]


    //���캯��
    public GameSaveData()
    {
        MapSaves_Dict = new Dictionary<string, MapSave>();
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}