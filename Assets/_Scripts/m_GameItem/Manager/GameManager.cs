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
    [Header("寻路系统")]
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
            Debug.LogWarning("区块字典为 空 退出时未保存任何 请检查加载时是否向区块字典中填充对象");
        }
        foreach (var go in ChunkMgr.Instance.Chunk_Dic.Values)
        {
            go.SaveChunk();
            //覆盖当前激活的地图
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
        //创建随机数        // 初始化随机种子并创建系统随机实例
        SaveDataMgr.Instance.SaveData.SaveSeed = UnityEngine.Random.Range(0, int.MaxValue).ToString();
        SaveDataMgr.Instance.SaveData.Seed = SaveDataMgr.Instance.SaveData.SaveSeed.GetHashCode();
        UnityEngine.Random.InitState(SaveDataMgr.Instance.SaveData.Seed);

        //创建初始星球的数据
        SaveDataMgr.Instance.SaveData.PlanetData_Dict["地球"] = Ready_planetData;
        ContinueGame();
    }

    public void ContinueGame()
    {
        StartGame_By_LoadPlayer(SaveDataMgr.Instance.CurrentContrrolPlayerName);
    }

    public void StartGame_By_LoadPlayer(string PlayerName)
    {
        // 1. 根据存档立即确定星球名
        SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(PlayerName, out Data_Player playerData);
        string planetName = playerData != null ? playerData.CurrentSceneName : "地球";

        // 2. 立刻创建并激活空场景
        Scene newScene = SceneManager.CreateScene(planetName);
        SceneManager.SetActiveScene(newScene);

        // 3. 准备卸载旧场景（如有）
        Scene startScene = SceneManager.GetSceneByName("GameStartScene");
        if (startScene.IsValid() && startScene.isLoaded)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(startScene);
            unloadOp.completed += _ =>
            {
                Debug.Log($"旧场景已卸载：{startScene.name}");
                LoadPlayerAndCreateWorld(PlayerName, playerData);
            };
        }
        else
        {
            // 没有旧场景，直接完成后续步骤
            LoadPlayerAndCreateWorld(PlayerName, playerData);
        }
    }

    public void ChangeScene_ByPlayerData(string LastSceneName, string NextSceneName,Action onSceneUnloaded = null)
    {
        ChunkMgr.Instance.CleanEmptyDicValues();

        // 保存玩家和区块
        ItemMgr.Instance.SavePlayer();

        foreach (var go in ChunkMgr.Instance.Chunk_Dic.Values)
        {
            go.SaveChunk();

            //覆盖当前激活的地图
            if (ItemMgr.Instance.User_Player.Data.IsInRoom == false)
            {
                SaveDataMgr.Instance.Active_PlanetData.MapData_Dict[go.MapSave.MapName] = go.MapSave;
            }

            if (ItemMgr.Instance.User_Player.Data.IsInRoom == true)
            {
                SaveDataMgr.Instance.SaveData.MapInScene[go.MapSave.MapName] = go.MapSave;
            }
        }
        ///////////////////////////上面都是对旧场景的处理////////////////////
        // 创建新场景
        Scene newScene = SceneManager.CreateScene(NextSceneName);

        // 卸载旧场景
        Scene startScene = SceneManager.GetActiveScene();

        if (startScene.IsValid() && startScene.isLoaded)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(startScene);
            unloadOp.completed += _ =>
            {
                SceneManager.SetActiveScene(newScene);
                Debug.Log($"旧场景已卸载：{startScene.name}");

               // 触发回调
               onSceneUnloaded?.Invoke();
            };
        }
        else
        {
            // 如果没有旧场景，直接执行回调
            onSceneUnloaded?.Invoke();
        }
    }

    /// <summary>
    /// 旧场景卸载完之后，再真正加载玩家、创建天体
    /// </summary>
    private void LoadPlayerAndCreateWorld(string playerName, Data_Player playerData)
    {
        ItemMgr.Instance.CleanupNullItems();
    
        Player player = ItemMgr.Instance.LoadPlayer(playerName);
        if (playerData == null)                // 新玩家：随机放到新场景
            ItemMgr.Instance.RandomDropInMap(player.gameObject,null,new Vector2Int(-1,-1));

        ItemMgr.Instance.Player_DIC[playerName] = player;

        player.Load();
        player.LoadDataPosition();

        
        if (player.Data.IsInRoom == false)
        {
            // 创建天体
            SunAndMoonObj = Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
        }
        else
        {
            // 进入房间
          Chunk chunk = ChunkMgr.Instance.CreateChunK_ByMapSave(SaveDataMgr.Instance.SaveData.MapInScene[playerData.CurrentSceneName]);
            ChunkMgr.Instance.Chunk_Dic[chunk.MapSave.MapName] = chunk;
        }

    }



}
