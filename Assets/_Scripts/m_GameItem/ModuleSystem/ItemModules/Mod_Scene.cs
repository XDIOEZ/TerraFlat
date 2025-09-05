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
    public Vector2 PlayerPos;
    public bool Encapsulation;
}

public class Mod_Scene : Module
{
    public Ex_ModData_MemoryPackable _data;
    public override ModuleData _Data { get { return _data; } set { _data = (Ex_ModData_MemoryPackable)value; } }
  
    public TextAsset _sceneAsset;
    public SceneData Data;
    public Vector2 PlayerPosOffset;

    public MapSave MapSave
    {
        get
        {
            if (SaveDataMgr.Instance.SaveData.MapInScene.TryGetValue(Data.SceneName, out var mapSave))
            {
                return mapSave;
            }
            return null;
        }
        set
        {
            SaveDataMgr.Instance.SaveData.MapInScene[Data.SceneName] = value;
        }
    }


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
        var Interacter = item.itemMods.GetMod_ByID(ModText.Interact) as Mod_Interaction;

        Interacter.OnAction_Start += Interact;

        if (mod_Building!= null)
        {
            mod_Building.StartUnInstall += UnInstall;
        }

        //����Ƿ��Ѿ���ʼ���� ���û�оͳ�ʼ��
        if (Data.IsInit == false && _sceneAsset!= null)
        {
            
            MapSave MapSave = MemoryPackSerializer.Deserialize<MapSave>(_sceneAsset.bytes);

            Data.SceneName += _sceneAsset.name;
            Data.SceneName += "_";
            Data.SceneName += Random.Range(1, 1000000).ToString();


            Data.IsInit = true;
            this.MapSave = MapSave;
            Debug.Log(Data.SceneName + "��ʼ�����");
          
        }
    }
    public void UnInstall()
    {
        if (Data.Encapsulation == true)
        {
            return;
        }
        if (MapSave == null)
        {
            Debug.LogWarning("MapSave ����Ϊ�գ��޷�ж��������Ʒ��");
            return;
        }

        // �������գ���ֹ�������޸��ֵ�
        foreach (var itemListKV in MapSave.items.ToList())
        {
            // ��������Ҫ�����Ʒ���
            if (itemListKV.Key == "MapEdge" || itemListKV.Key == "MapCore"|| itemListKV.Key == "ǽ��"|| itemListKV.Key == "Door")
                continue;

            var itemList = itemListKV.Value;
            foreach (var mapItem in itemList)
            {
                // ������Ʒʵ�����ڲ�Ӧ�ûָ�λ�á���ת�ȣ�
             Item item =   ItemMgr.Instance.InstantiateItem(mapItem,null);
                item.transform.position = transform.position;
            }

            // �ӱ����������Ƴ������
            MapSave.items.Remove(itemListKV.Key);
           
        }
        SaveDataMgr.Instance.SaveData.MapInScene.Remove(Data.SceneName);
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
    public void Interact(Item interacter)
    {
        Player player = interacter as Player;
        if (player == null)
        {
            Debug.LogError("Interact ����ʧ�ܣ���������� Player");
            return;
        }

        Data_Player playerData = player.Data;
        Vector2 playerPos = player.transform.position;

        // ===== ���뷿�� =====
        if (MapSave != null)
        {

            string lastSceneName = playerData.CurrentSceneName;

            // ���ó�ʼ���뷿���λ��
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;
            //////////����Ĳ����������³����н��е�//////////////

            // �л�����
            GameManager.Instance.ChangeScene_ByPlayerData(lastSceneName, Data.SceneName,() =>
            {
                playerData.CurrentSceneName = this.MapSave.MapName;
                // lastScene �Ѿ�������
                playerData.IsInRoom = true;

                // ���¼������
                Player newPlayer = ItemMgr.Instance.LoadPlayer(playerData.Name_User);
                newPlayer.Load();
                newPlayer.LoadDataPosition();
                ItemMgr.Instance.Player_DIC[playerData.Name_User] = newPlayer;

                // ���� Chunk
                ChunkMgr.Instance.CleanEmptyDicValues();
                Chunk chunk = ChunkMgr.Instance.CreateChunK_ByMapSave(MapSave);
                ChunkMgr.Instance.Chunk_Dic[MapSave.MapName] = chunk;
                ChunkMgr.Instance.Chunk_Dic_Active[MapSave.MapName] = chunk;
                // ���������ţ����÷��ص�
                if (chunk.RuntimeItemsGroup.TryGetValue("Door", out var doors))
                {
                    foreach (var door in doors)
                    {
                        if (door.itemMods.GetMod_ByID(ModText.Scene) is Mod_Scene sceneMod)
                        {
                            sceneMod.Data.SceneName = lastSceneName;
                            sceneMod.Data.PlayerPos = playerPos;

                            newPlayer.transform.position = (Vector2)door.transform.position + PlayerPosOffset;
                            this.Data.PlayerPos = door.transform.position;
                        }
                    }
                }
            });
        }
        // ===== �뿪���� =====
        else
        {
       
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;

            GameManager.Instance.ChangeScene_ByPlayerData(playerData.CurrentSceneName, this.Data.SceneName,() =>
            {
                playerData.CurrentSceneName = this.Data.SceneName;//������������ĳ�������
                playerData.IsInRoom = false;

                // ���¼������
                Player newPlayer = ItemMgr.Instance.LoadPlayer(playerData.Name_User);
                newPlayer.Load();
                newPlayer.LoadDataPosition();
                ItemMgr.Instance.Player_DIC[playerData.Name_User] = newPlayer;
            });
        }
    }


    public override void Save()
    {
        _data.WriteData(Data);
    }
}