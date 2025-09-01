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
            _Data.ID = ModText.Scene;
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
        if (Data.IsInit == false && _sceneAsset!= null)
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
        //SaveDataManager.Instance.SaveData.Active_PlanetData.MapData_Dict.Remove(Data.MapSave.MapName);

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
        // ȷ�� SceneName ͬ��
        this.Data.SceneName = this.Data.MapSave?.MapName;

        Player player = null;

        // ���Ի�ȡ��ǰ���Ƶ����
        if (!GameItemManager.Instance.Player_DIC.TryGetValue(
            SaveDataManager.Instance.CurrentContrrolPlayerName, out player))
        {
            Debug.LogError($"û���ҵ���ң�{SaveDataManager.Instance.CurrentContrrolPlayerName}");
            return;
        }

        Data_Player playerData = player.Data;
        playerData.CurrentPlanetName = this.Data.SceneName;

        if (this.Data.MapSave != null)
        {
            // �����Ų����³�������
            if (this.Data.MapSave.items.TryGetValue("Door", out var doorItems))
            {
                doorItems.ForEach(item =>
                {
                    SceneData sceneData = ExtractData<SceneData>(item, ModText.Scene);
                    if (sceneData != null)
                    {
                        sceneData.SceneName = playerData.CurrentPlanetName;
                        sceneData.PlayerPos = playerData._transform.Position;
                    }

                    // ���� Data �� PlayerPos
                    this.Data.PlayerPos = item._transform.Position;
                });
            }

            // �������λ��
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;

            // �л�����
            GameManager.Instance.ChangeScene_ByPlayerData(playerData);

            // ���� Chunk
            GameChunkManager.Instance.CreateChun_ByMapSave(this.Data.MapSave);
        }
        else
        {
            // MapSave ������ʱ�Ĵ���
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;
            GameManager.Instance.ChangeScene_ByPlayerData(playerData);
        }
    }


    public override void Save()
    {
        _data.WriteData(Data);
    }
}