using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : SingletonAutoMono<GameManager>
{
    [SerializeField]
    private GameObject SunAndMoonPrefab;
    [Header("Ѱ·ϵͳ")]
    public GameObject PathFindingSystem;
    public GameObject SunAndMoonObj { get; private set; }

    public PlanetData Ready_planetData = new PlanetData();
    public UltEvent Event_GameStart { get; set; } = new UltEvent();
    public UltEvent Event_ExitGame_Start { get;  set; } = new UltEvent();
    public UltEvent Event_ExitGame_End { get;  set; } = new UltEvent();

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        PathFindingSystem.SetActive(true);
    }
    public void ExitGame()
    {
        Event_ExitGame_Start.Invoke();

        ItemMgr.Instance.CleanupNullItems();

        ChunkMgr.Instance.CleanEmptyDicValues();

        ItemMgr.Instance.SavePlayer();
        
        if(ChunkMgr.Instance.Chunk_Dic.Count <= 0)
        {
            Debug.LogWarning("�����ֵ�Ϊ �� �˳�ʱδ�����κ� �������ʱ�Ƿ��������ֵ���������");
        }
        foreach (var go in ChunkMgr.Instance.Chunk_Dic.Values)
        {
            go.SaveChunk();
            //���ǵ�ǰ����ĵ�ͼ
            if (ItemMgr.Instance.User_Player.Data.IsInRoom == false)
            {
                SaveDataMgr.Instance.Active_PlanetData.MapData_Dict[go.MapSave.MapName] = go.MapSave;

            }

            if (ItemMgr.Instance.User_Player.Data.IsInRoom == true)
            {
                SaveDataMgr.Instance.SaveData.MapInScene[ItemMgr.Instance.User_Player.Data.CurrentSceneName] = go.MapSave;
            }

        }




        SaveDataMgr.Instance.Save_And_WriteToDisk();

        ChunkMgr.Instance.ClearAllChunk();

        SceneManager.LoadScene("GameStartScene");

        Event_ExitGame_End.Invoke();
    }

    public void StartNewGame()
    {
        //���������        // ��ʼ��������Ӳ�����ϵͳ���ʵ��
        SaveDataMgr.Instance.SaveData.SaveSeed = UnityEngine.Random.Range(0, int.MaxValue).ToString();
        SaveDataMgr.Instance.SaveData.Seed = SaveDataMgr.Instance.SaveData.SaveSeed.GetHashCode();
        UnityEngine.Random.InitState(SaveDataMgr.Instance.SaveData.Seed);

        //������ʼ���������
        SaveDataMgr.Instance.SaveData.PlanetData_Dict["����"] = Ready_planetData;
        ContinueGame();
    }

    public void ContinueGame()
    {
        StartGame_By_LoadPlayer(SaveDataMgr.Instance.CurrentContrrolPlayerName);
    }

    public void StartGame_By_LoadPlayer(string PlayerName)
    {
        // 1. ���ݴ浵����ȷ��������
        SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(PlayerName, out Data_Player playerData);
        string planetName = playerData != null ? playerData.CurrentSceneName : "����";

        // 2. ���̴���������ճ���
        Scene newScene = SceneManager.CreateScene(planetName);
        SceneManager.SetActiveScene(newScene);

        // 3. ׼��ж�ؾɳ��������У�
        Scene startScene = SceneManager.GetSceneByName("GameStartScene");
        if (startScene.IsValid() && startScene.isLoaded)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(startScene);
            unloadOp.completed += _ =>
            {
                Debug.Log($"�ɳ�����ж�أ�{startScene.name}");
                LoadPlayerAndCreateWorld(PlayerName, playerData);
            };
        }
        else
        {
            // û�оɳ�����ֱ����ɺ�������
            LoadPlayerAndCreateWorld(PlayerName, playerData);
        }
    }

    public void ChangeScene_ByPlayerData(string LastSceneName, string NextSceneName,Action onSceneUnloaded = null)
    {
        ChunkMgr.Instance.CleanEmptyDicValues();

        // ������Һ�����
        ItemMgr.Instance.SavePlayer();

        foreach (var go in ChunkMgr.Instance.Chunk_Dic.Values)
        {
            go.SaveChunk();

            //���ǵ�ǰ����ĵ�ͼ
            if (ItemMgr.Instance.User_Player.Data.IsInRoom == false)
            {
                SaveDataMgr.Instance.Active_PlanetData.MapData_Dict[go.MapSave.MapName] = go.MapSave;
            }

            if (ItemMgr.Instance.User_Player.Data.IsInRoom == true)
            {
                SaveDataMgr.Instance.SaveData.MapInScene[go.MapSave.MapName] = go.MapSave;
            }
        }
        ///////////////////////////���涼�ǶԾɳ����Ĵ���////////////////////
        // �����³���
        Scene newScene = SceneManager.CreateScene(NextSceneName);

        // ж�ؾɳ���
        Scene startScene = SceneManager.GetActiveScene();

        if (startScene.IsValid() && startScene.isLoaded)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(startScene);
            unloadOp.completed += _ =>
            {
                SceneManager.SetActiveScene(newScene);
                Debug.Log($"�ɳ�����ж�أ�{startScene.name}");

               // �����ص�
               onSceneUnloaded?.Invoke();
            };
        }
        else
        {
            // ���û�оɳ�����ֱ��ִ�лص�
            onSceneUnloaded?.Invoke();
        }
    }

    /// <summary>
    /// �ɳ���ж����֮��������������ҡ���������
    /// </summary>
    private void LoadPlayerAndCreateWorld(string playerName, Data_Player playerData)
    {
        ItemMgr.Instance.CleanupNullItems();
    
        Player player = ItemMgr.Instance.LoadPlayer(playerName);
        if (playerData == null)                // ����ң�����ŵ��³���
            ItemMgr.Instance.RandomDropInMap(player.gameObject,null,new Vector2Int(-1,-1));

        ItemMgr.Instance.Player_DIC[playerName] = player;

        player.Load();
        player.LoadDataPosition();

        
        if (player.Data.IsInRoom == false)
        {
            // ��������
            SunAndMoonObj = Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
        }
        else
        {
            // ���뷿��
          Chunk chunk = ChunkMgr.Instance.CreateChunK_ByMapSave(SaveDataMgr.Instance.SaveData.MapInScene[playerData.CurrentSceneName]);
            ChunkMgr.Instance.Chunk_Dic[chunk.MapSave.MapName] = chunk;
        }

    }



}
