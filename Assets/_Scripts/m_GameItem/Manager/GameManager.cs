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

        GameItemManager.Instance.SavePlayer();
        GameItemManager.Instance.CleanupNullItems();

        foreach (var go in GameChunkManager.Instance.Chunk_Dic.Values)
        {
            SaveDataManager.Instance.SaveChunk_To_SaveData(go);
        }

        SaveDataManager.Instance.Save_And_WriteToDisk();

        GameChunkManager.Instance.ClearAllChunk();

        SceneManager.LoadScene("GameStartScene");

        Event_ExitGame_End.Invoke();
    }
    public void StartNewGame()
    {

        SaveDataManager.Instance.SaveData.SaveSeed = Random.Range(0, int.MaxValue).ToString();
        // 初始化随机种子并创建系统随机实例
        SaveDataManager.Instance.SaveData.Seed = SaveDataManager.Instance.SaveData.SaveSeed.GetHashCode();
        Random.InitState(SaveDataManager.Instance.SaveData.Seed);
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
        // 注册场景加载完成后的回调方法
        SceneManager.sceneLoaded += OnScene_Loaded;

        // 切换到 GameScene（统一的游戏主场景）
        SceneManager.LoadSceneAsync("GameScene");

        void OnScene_Loaded(Scene loadedScene, LoadSceneMode mode)
        {
            // 1. 注销事件监听，避免重复调用
            SceneManager.sceneLoaded -= OnScene_Loaded;

            GameItemManager.Instance.CleanupNullItems();
            // 玩家数据存在则加载，否则创建并初始化默认位置
            if (SaveDataManager.Instance.SaveData.PlayerData_Dict.TryGetValue(PlayerName, out var playerData))
            {
                Player player = GameItemManager.Instance.LoadPlayer(PlayerName);

                GameItemManager.Instance.Player_DIC[PlayerName] = player;
            }
            else//玩家数据不存在 创建默认模板
            {
                Player player = GameItemManager.Instance.LoadPlayer(PlayerName);

                GameItemManager.Instance.RandomDropInMap(player.gameObject);

                GameItemManager.Instance.Player_DIC[PlayerName] = player;
            }

            Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
        }
    }
}
