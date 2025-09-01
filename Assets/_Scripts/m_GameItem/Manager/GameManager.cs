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
            //SaveDataManager.Instance.SaveChunk_To_SaveData(go);
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
        SaveDataManager.Instance.SaveData.Active_PlanetData = Ready_planetData;


        ContinueGame();
    }
    public void ContinueGame()
    {
        StartGame_By_LoadPlayer(SaveDataManager.Instance.CurrentContrrolPlayerName);
    }
    public void StartGame_By_LoadPlayer(string PlayerName)
    {
        // 获取玩家数据
        SaveDataManager.Instance.SaveData.PlayerData_Dict.TryGetValue(PlayerName, out var playerData);

        // 从玩家数据中读取星球名称，如果没有则用默认名字
        string planetName = playerData != null ? playerData.CurrentPlanetName : "地球";

        // 创建并切换到新场景
        Scene newScene = SceneManager.CreateScene(planetName);
        SceneManager.SetActiveScene(newScene);

        // 清理无效物体
        GameItemManager.Instance.CleanupNullItems();

        Player player;
        if (playerData != null)
        {
            // 玩家数据存在则加载
            player = GameItemManager.Instance.LoadPlayer(PlayerName);
        }
        else
        {
            // 玩家数据不存在 → 创建默认模板并随机放置
            player = GameItemManager.Instance.LoadPlayer(PlayerName);
            GameItemManager.Instance.RandomDropInMap(player.gameObject);
        }

        GameItemManager.Instance.Player_DIC[PlayerName] = player;

        // 创建天体
        Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
    }


}
