using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : SingletonAutoMono<GameManager>
{
    [SerializeField]
    private GameObject SunAndMoonPrefab;
    public GameObject SunAndMoonObj { get; private set; }

    public PlanetData Ready_planetData = new PlanetData();
    public UltEvent Event_GameStart { get; set; } = new UltEvent();
    public UltEvent Event_ExitGame_Start { get;  set; } = new UltEvent();
    public UltEvent Event_ExitGame_End { get;  set; } = new UltEvent();


    public void ExitGame()
    {
        Event_ExitGame_Start.Invoke();

        GameItemManager.Instance.CleanupNullItems();

        GameChunkManager.Instance.CleanEmptyDicValues();

        GameItemManager.Instance.SavePlayer();
        
        foreach (var go in GameChunkManager.Instance.Chunk_Dic.Values)
        {
            go.SaveChunk();
        }

        SaveDataManager.Instance.Save_And_WriteToDisk();

        GameChunkManager.Instance.ClearAllChunk();

        SceneManager.LoadScene("GameStartScene");

        Event_ExitGame_End.Invoke();
    }

    public void StartNewGame()
    {
        //创建随机数        // 初始化随机种子并创建系统随机实例
        SaveDataManager.Instance.SaveData.SaveSeed = Random.Range(0, int.MaxValue).ToString();
        SaveDataManager.Instance.SaveData.Seed = SaveDataManager.Instance.SaveData.SaveSeed.GetHashCode();
        Random.InitState(SaveDataManager.Instance.SaveData.Seed);

        //创建初始星球的数据
        SaveDataManager.Instance.SaveData.PlanetData_Dict["地球"] = Ready_planetData;

        ContinueGame();
    }

    public void ContinueGame()
    {
        StartGame_By_LoadPlayer(SaveDataManager.Instance.CurrentContrrolPlayerName);
    }
    public void StartGame_By_LoadPlayer(string PlayerName)
    {
        // 1. 根据存档立即确定星球名
        SaveDataManager.Instance.SaveData.PlayerData_Dict.TryGetValue(PlayerName, out Data_Player playerData);
        string planetName = playerData != null ? playerData.CurrentPlanetName : "地球";

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
    public void ChangeScene_ByPlayerData(Data_Player playerData)
    {
        if (playerData == null)
        {
            Debug.Log("Player data is null.");
            return;
        }
        var PlayerName = playerData.Name_User;

        string planetName = playerData.CurrentPlanetName;

        GameItemManager.Instance.SavePlayer();

        foreach (var go in GameChunkManager.Instance.Chunk_Dic.Values)
        {
            go.SaveChunk();
        }


        Scene newScene = SceneManager.CreateScene(planetName);
        // 3. 准备卸载旧场景（如有）
        Scene startScene = SceneManager.GetActiveScene();
        if (startScene.IsValid() && startScene.isLoaded)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(startScene);
            unloadOp.completed += _ =>
            {
                SceneManager.SetActiveScene(newScene);
                Debug.Log($"旧场景已卸载：{startScene.name}");
                Player player = GameItemManager.Instance.LoadPlayer(PlayerName);
            };
        }
    }

    /// <summary>
    /// 旧场景卸载完之后，再真正加载玩家、创建天体
    /// </summary>
    private void LoadPlayerAndCreateWorld(string playerName, Data_Player playerData)
    {
        GameItemManager.Instance.CleanupNullItems();

        Player player = GameItemManager.Instance.LoadPlayer(playerName);
        if (playerData == null)                // 新玩家：随机放到新场景
            GameItemManager.Instance.RandomDropInMap(player.gameObject,null,new Vector2Int(-1,-1));

        GameItemManager.Instance.Player_DIC[playerName] = player;

        // 创建天体
        SunAndMoonObj = Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
    }



}
