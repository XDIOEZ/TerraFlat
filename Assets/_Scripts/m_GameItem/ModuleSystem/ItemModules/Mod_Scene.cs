using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
[System.Serializable]
[MemoryPackable]
public partial class SceneData
{
    public string SceneName;
    public bool IsInit = false;
    public MapSave MapSave;
    public Vector2 PlayerPos;
    public bool Encapsulation;
}

public class Mod_Scene : Module
{
    public static MapSave CurrentMapSave;
    public Ex_ModData_MemoryPackable _data;
    public override ModuleData _Data { get { return _data; } set { _data = (Ex_ModData_MemoryPackable)value; } }
  
    public TextAsset _sceneAsset;
    public SceneData Data;
    public Vector2 PlayerPosOffset;

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Space;
        }
    }

    public override void Load()
    {
        _data.ReadData(ref Data);


        var mod_Building = item.itemMods.GetMod_ByID(ModText.Building) as Mod_Building;

        if(mod_Building!= null)
        {
            mod_Building.StartUnInstall += UnInstall;
        }

        //����Ƿ��Ѿ���ʼ���� ���û�оͳ�ʼ��
        if (Data.IsInit == false)
        {
            Data.MapSave = MemoryPackSerializer.Deserialize<MapSave>(_sceneAsset.bytes);
            Debug.Log(Data.MapSave.MapName + "��ʼ�����");
            Data.MapSave.MapName += "_";
            Data.MapSave.MapName += Random.Range(1, 1000000).ToString();
            Data.IsInit = true;
        }

        if (CurrentMapSave != null && CurrentMapSave.MapName == Data.MapSave.MapName)
        {
            Data.MapSave = CurrentMapSave;
            CurrentMapSave = null;
        }
    }
    public void UnInstall()
    {
        SaveDataManager.Instance.SaveData.Active_PlanetData.MapData_Dict.Remove(Data.MapSave.MapName);

        if (Data.Encapsulation == true)
        {
            return;
        }
        if (Data?.MapSave == null)
        {
            Debug.LogWarning("MapSave ����Ϊ�գ��޷�ж��������Ʒ��");
            return;
        }

        // �������գ���ֹ�������޸��ֵ�
        foreach (var itemListKV in Data.MapSave.items.ToList())
        {
            // ��������Ҫ�����Ʒ���
            if (itemListKV.Key == "MapEdge" || itemListKV.Key == "MapCore"|| itemListKV.Key == "ǽ��")
                continue;

            var itemList = itemListKV.Value;
            foreach (var mapItem in itemList)
            {
                // ������Ʒʵ�����ڲ�Ӧ�ûָ�λ�á���ת�ȣ�
             Item item =   GameItemManager.Instance.InstantiateItem(mapItem,null);
                item.transform.position = transform.position;
            }

            // �ӱ����������Ƴ������
            Data.MapSave.items.Remove(itemListKV.Key);
           
        }

        Debug.Log("�����ڲ���Ʒ��ȫ��ʵ�������� MapSave �Ƴ�");
    }

    [Button]
    public void AddScene()
    {
        // ����һ���µ���ʱ���������ֿ��������
        Scene newScene = SceneManager.CreateScene("TempTentScene");

        // �л�������³�������ѡ��
        SceneManager.SetActiveScene(newScene);

        Debug.Log("�³����Ѵ�����" + newScene.name);
    }

    [Button]
    public void Test()
    {
        //����һ����ʱ����
        Scene newScene = SceneManager.CreateScene(Data.SceneName);

        //TODO 0.����ɳ�������Ʒ
        //1.ж�ؾɳ�������Ʒ


        //�л�������³���
        SceneManager.SetActiveScene(newScene);

        //TODO ͨ��Data.MapSave ��ԭ��������Ʒ �������������ʱ����

        GameChunkManager.Instance.CreateChun_ByMapSave(Data.MapSave);

        Data.MapSave.items["MapEdge"].ForEach(item =>
        {
            Data_Boundary boundary = item as Data_Boundary;
            boundary.TP_Position = transform.position;
            boundary.TP_SceneName = SaveDataManager.Instance.SaveData.Active_MapName;
            Data.PlayerPos = boundary._transform.Position;
        });

        if (SaveDataManager.Instance.SaveData.PlayerData_Dict.TryGetValue
         (SaveDataManager.Instance.CurrentContrrolPlayerName, out var savedData))
        {
            //Debug.Log($"�ɹ������ѱ������ң�{playerName}");
            Data_Player playerData = savedData;
            playerData._transform.Position = Data.PlayerPos + PlayerPosOffset;
        }


    }

    public override void Save()
    {
        _data.WriteData(Data);
    }
}