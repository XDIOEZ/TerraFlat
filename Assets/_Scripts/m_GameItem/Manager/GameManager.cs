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

    [Header("准备好的星球数据")]
    public PlanetData Ready_planetData = new ();
    [Header("准备好的时间数据")]
    public TimeData Ready_timeData = new TimeData();

    public UltEvent Event_GameStart { get; set; } = new UltEvent();
    public UltEvent Event_ExitGame_Start { get;  set; } = new UltEvent();
    public UltEvent Event_ExitGame_End { get;  set; } = new UltEvent();

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        PathFindingSystem.SetActive(true);
        Time.timeScale = 1;
    }
    
    
/// <summary>
/// 使用协程处理退出游戏逻辑，解决保存与销毁的时序问题
/// </summary>
/// <param name="onComplete">退出完成后的回调函数</param>
/// <returns></returns>
public IEnumerator ExitGameCoroutine(System.Action onComplete = null)
{
    // TODO完成：在退出游戏时删除实例化出来的SunAndMoonPrefab
    // 保存时间数据
    SaveDataMgr.Instance.SaveData.DayTimeData = DayTimeSystem.Instance.GetSaveData();

    // 销毁之前实例化的天体对象
    if (SunAndMoonObj != null)
    {
        Destroy(SunAndMoonObj);
        SunAndMoonObj = null;
        Debug.Log("已销毁SunAndMoon对象");
    }
    
    // 安全检查：确保核心管理器已初始化
    if (ItemMgr.Instance == null || ChunkMgr.Instance == null ||
        SaveDataMgr.Instance == null)
    {
        Debug.LogError("核心管理器未初始化，退出失败！");
        onComplete?.Invoke(); // 即使失败也调用回调
        yield break;
    }

    // 触发退出开始事件
    Event_ExitGame_Start?.Invoke();

    // 先执行基础清理
    ItemMgr.Instance.CleanupNullItems();
    ChunkMgr.Instance.CleanEmptyDicValues();

    // 提前保存玩家数据（在销毁逻辑执行前）
    ItemMgr.Instance.SavePlayer();

    // 延迟一帧，等待所有标记为销毁的对象实际销毁
    yield return null;

    // 延迟一帧后再保存区块数据（此时已完成销毁，不会保存脏数据）
    SaveAllChunks();

    // 保存数据到磁盘（使用同步方法）
    SaveDataMgr.Instance.Save_And_WriteToDisk();

    // 清理所有区块
    ChunkMgr.Instance.ClearAllChunk();

    // 异步加载开始场景
    var loadOp = SceneManager.LoadSceneAsync("GameStartScene");
    while (!loadOp.isDone)
    {
        yield return null; // 等待场景加载完成
    }

    // 所有操作完成后触发结束事件
    Event_ExitGame_End?.Invoke();
    
    // 调用回调函数
    onComplete?.Invoke();

        SaveDataMgr.Instance.SaveData = new GameSaveData();
}

    /// <summary>
    /// 保存所有区块数据（提取为独立方法，提高可读性）
    /// </summary>
    private void SaveAllChunks()
    {
        var chunkDic = ChunkMgr.Instance.Chunk_Dic;

        if (chunkDic.Count <= 0)
        {
            Debug.LogWarning("区块字典为空，退出时未保存任何区块，请检查加载逻辑");
            return;
        }

        foreach (var chunk in chunkDic.Values)
        {
            if (chunk == null)
            {
                Debug.LogWarning("发现空区块对象，已跳过保存");
                continue;
            }

            chunk.SaveChunk();
            if (chunk.MapSave != null && !string.IsNullOrEmpty(chunk.MapSave.Name))
            {
                SaveDataMgr.Instance.Active_PlanetData.MapData_Dict[chunk.MapSave.Name] = chunk.MapSave;
            }
        }
    }

    // 调用方式：在需要退出游戏的地方启动协程
    // StartCoroutine(ExitGameCoroutine());


public void StartNewGame()
{
    SaveDataMgr.Instance.SaveData = new GameSaveData();
    //创建随机数        // 初始化随机种子并创建系统随机实例
    SaveDataMgr.Instance.SaveData.SaveSeed = UnityEngine.Random.Range(0, int.MaxValue).ToString();
    SaveDataMgr.Instance.SaveData.Seed = SaveDataMgr.Instance.SaveData.SaveSeed.GetHashCode();
    UnityEngine.Random.InitState(SaveDataMgr.Instance.SaveData.Seed);

    //创建初始星球的数据
    SaveDataMgr.Instance.SaveData.PlanetData_Dict["地球"] = Ready_planetData;
    //创建初始时间数据 - 修复类型转换问题
    SaveDataMgr.Instance.SaveData.DayTimeData.WorldTimeDict["地球"] = new SerializableTimeData(Ready_timeData);
    SaveDataMgr.Instance.SaveData.DayTimeData.SceneLightingRateDict["地球"] = 1.0f;

    ContinueGame();
}

    /// <summary>
/// 保存游戏数据专用方法，仅执行保存操作不进行其他逻辑处理
/// </summary>
public void SaveGame()
{
    // 保存时间数据
    SaveDataMgr.Instance.SaveData.DayTimeData = DayTimeSystem.Instance.GetSaveData();

    // 先保存玩家数据
    ItemMgr.Instance.SavePlayer();

    // 保存所有区块数据
    SaveAllChunks();

    // 将数据保存到磁盘
    SaveDataMgr.Instance.Save_And_WriteToDisk();
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

        // TODO完成：实例化SunAndMoonPrefab
        if (SunAndMoonPrefab != null)
        {
            SunAndMoonObj = Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
            // 确保天体对象在场景切换时不会被销毁
            DontDestroyOnLoad(SunAndMoonObj);
            Debug.Log("已实例化SunAndMoon对象");
        }
        else
        {
            Debug.LogWarning("SunAndMoonPrefab为空，无法实例化天体对象");
        }

        DayTimeSystem.Instance.LoadFromSaveData(SaveDataMgr.Instance.SaveData.DayTimeData);

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

    [Tooltip("切换场景")]
    public void ChangeScene_By_SceneNames(string LastSceneName, string NextSceneName,Action onSceneUnloaded = null)
    {
        // 保存玩家和区块
        ItemMgr.Instance.SavePlayer();

        //保存场景数据
        foreach (var go in ChunkMgr.Instance.Chunk_Dic.Values)
        {
            go.SaveChunk();

            SaveDataMgr.Instance.SaveData.PlanetData_Dict[LastSceneName].MapData_Dict[go.MapSave.Name] = go.MapSave;
        }

        ChunkMgr.Instance.CleanDic();

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
        ChunkMgr.Instance.ClearAllChunk();
    
        Player player = ItemMgr.Instance.LoadPlayer(playerName);
           
        ItemMgr.Instance.Player_DIC[playerName] = player;

        player.Load();

        if (player.Data.transform.position == Vector3.zero)
        {
            // 新玩家：随机放到新场景
            ItemMgr.Instance.RandomDropInMap(player.gameObject, null, new Vector2Int(-1, -1));
        }
    }
}