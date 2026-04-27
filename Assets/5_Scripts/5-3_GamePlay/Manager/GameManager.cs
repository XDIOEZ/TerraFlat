using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : SingletonAutoMono<GameManager>
{
    #region Events
    public static event Action<Player> Event_PlayerEnterWorld;
    #endregion

    #region 游戏生命周期状态
    /// <summary>
    /// 玩家是否已进入游戏世界。
    /// 管理器在 GameStartScene 中初始化时不应执行游戏逻辑，
    /// 只有玩家通过 ContinueGame/CreateNewWorld 进入世界后才为 true。
    /// 便捷查询属性，运行时控制请使用事件订阅。
    /// </summary>
    public bool IsInGameWorld { get; private set; } = false;



    #endregion

    [SerializeField]
    private GameObject SunAndMoonPrefab;
    [Header("寻路系统")]
    public GameObject PathFindingSystem;
    public GameObject SunAndMoonObj { get; private set; }

    [Header("准备好的星球数据")]
    public PlanetData ReadyPlanetData = new();
    [Header("准备好的时间数据")]
    public TimeData ReadyTimeData = new TimeData();
    [Header("存档数据")]
    public GameSaveData ReadyGameSaveData = new GameSaveData();

    #region  游戏核心循环的事件们
    public event Action Event_GameWorldEnter;

    public event Action Event_GameWorldExit;
    public UltEvent Event_GameStart_Start { get; set; } = new UltEvent(); //用户准备开始游戏的事件
    public UltEvent Event_GameStart_End { get; set; } = new UltEvent(); //用户已经开始游戏完毕的事件
    public UltEvent BackToHelloScene_Event_Start { get; set; } = new UltEvent();//用户准备退回到开始界面开始的事件
    public UltEvent BackToHelloScene_Event_End { get; set; } = new UltEvent();//用户退回到开始界面结束的事件
    #endregion
    [Header("UI 预制体")]
    public GameObject UIPrefab_HelloCanvas;
    public GameObject UIPrefab_SaveManager;
    public GameObject UIPrefab_NewGame;
    public GameObject UIPrefab_ContextMenu;

    [Header("UI 面板名称配置")]
    [SerializeField] private string saveManagerPanelName = "UI_GameSaveManager";
    [SerializeField] private string saveManagerPanelNameLegacy = "存档选择面板";

    [Header("新玩家出生点搜索配置")]
    [SerializeField, Min(1)] private int spawnLandMaxSearchRadius = 256;
    [SerializeField, Min(0)] private int spawnSeedAnchorRange = 256;
    [SerializeField, Min(1)] private int spawnSearchRetryFrames = 60;

    #region 生命周期方法
    private void Start()
    {
        DontDestroyOnLoad(gameObject);

        // 寻路系统不在 StartScene 激活，等玩家进入游戏世界后再启用
        // (PathFindingSystem 将在 RunWorld 时或由 AstarGameManager 自行延迟初始化)

        Time.timeScale = 1;

        BackToHelloScene_Event_End += OpenHellowCanvas;
    }
    #endregion

    #region 退出游戏相关
    /// <summary>
    /// 使用协程处理退出游戏逻辑，解决保存与销毁的时序问题
    /// </summary>
    /// <param name="onComplete">退出完成后的回调函数</param>
    /// <returns></returns>
    public IEnumerator BackToHelloScene_Coroutine(Item Player, System.Action onComplete = null)
    {
        Debug.Log("<color=yellow>[ExitGame]</color> 开始执行退出流程...");

        ////////////////////////////////////////////////////////////////////////////////////
        // 阶段 1：准备阶段
        ////////////////////////////////////////////////////////////////////////////////////

        // 标记已退出游戏世界，各管理器应停止运行
        IsInGameWorld = false;

        // 通知所有订阅者：游戏世界已退出
        Event_GameWorldExit?.Invoke();

        // 保存当前时间数据，包括日夜状态等
        SaveDataMgr.Instance.SaveData.DayTimeData = DayTimeSystem.Instance.GetSaveData();


        // 安全检查：确保核心管理器已初始化
        if (ItemMgr.Instance == null || ChunkMgr.Instance == null ||
            SaveDataMgr.Instance == null)
        {
            Debug.LogError("[ExitGame] 核心管理器未初始化，退出失败！");
            onComplete?.Invoke(); // 即使失败也调用回调
            yield break;
        }

        // 触发退出开始事件
        BackToHelloScene_Event_Start?.Invoke();

        ////////////////////////////////////////////////////////////////////////////////////
        // 阶段 2：数据保存阶段
        ////////////////////////////////////////////////////////////////////////////////////

        // 先执行基础清理
        ItemMgr.Instance.CleanupNullItems();
        ChunkMgr.Instance.CleanEmptyDicValues();

        // 保存所有区块数据
        Debug.Log("[ExitGame] 开始保存区块数据...");
        SaveAllChunks();

        // 提前保存玩家数据（在销毁逻辑执行前）
        Debug.Log("[ExitGame] 开始保存玩家数据...");
        ItemMgr.Instance.SavePlayer();

        // 保存数据到磁盘
        Debug.Log("[ExitGame] 写入存档文件...");
        SaveDataMgr.Instance.Save_And_WriteToDisk();

        ////////////////////////////////////////////////////////////////////////////////////
        // 阶段 3：清理阶段
        ////////////////////////////////////////////////////////////////////////////////////

        // 销毁玩家对象
        if (Player != null)
        {
            Destroy(Player.gameObject);
            Debug.Log("[ExitGame] 已销毁玩家对象");
        }

        // 延迟一帧，等待所有标记为销毁的对象实际销毁
        yield return null;

        // 销毁之前实例化的天体对象
        if (SunAndMoonObj != null)
        {
            Destroy(SunAndMoonObj);
            SunAndMoonObj = null;
            Debug.Log("[ExitGame] 已销毁 SunAndMoon 对象");
        }

        // 清理所有区块
        Debug.Log("[ExitGame] 开始清理区块...");
        ChunkMgr.Instance.ClearAllChunk();

        // 释放未使用的资源
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        ////////////////////////////////////////////////////////////////////////////////////
        // 阶段 4：场景切换阶段
        ////////////////////////////////////////////////////////////////////////////////////

        Debug.Log("[ExitGame] 准备加载 GameStartScene...");
        AsyncOperation loadOp = SceneManager.LoadSceneAsync("GameStartScene");
        while (!loadOp.isDone)
            yield return null;

        ////////////////////////////////////////////////////////////////////////////////////
        // 阶段 5：收尾阶段
        ////////////////////////////////////////////////////////////////////////////////////

        // 重置存档数据，准备下次游戏
        SaveDataMgr.Instance.SaveData = new GameSaveData();

        // 触发退出结束事件
        BackToHelloScene_Event_End?.Invoke();

        Debug.Log("<color=green>[ExitGame]</color> 游戏退出流程完成");

        // 最终调用回调函数
        onComplete?.Invoke();
    }

    /// <summary>
    /// 保存所有区块数据（提取为独立方法，提高可读性）
    /// </summary>
    private void SaveAllChunks()
    {
        var chunkDic = ChunkMgr.Instance.Chunk_Dic_ByPos;

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
    #endregion

    #region 游戏开始相关
    [Tooltip("开始新游戏,创建一个新世界")]
    public void CreateNewWorld()
    {
        SaveDataMgr.Instance.SaveData = new GameSaveData();

        string inputSeed = ReadyGameSaveData.SaveSeed?.Trim();
        bool isNoSeedInput = string.IsNullOrEmpty(inputSeed) || inputSeed == "0";

        if (isNoSeedInput)
        {
            string randomSeed = GenerateRandomSeedString();
            SaveDataMgr.Instance.SaveData.SaveSeed = randomSeed;
            SaveDataMgr.Instance.SaveData.Seed = ConvertSeedStringToStableInt(randomSeed);
            Debug.Log($"[GameManager] 玩家未输入种子，自动生成随机种子={randomSeed}");
        }
        else
        {
            SaveDataMgr.Instance.SaveData.SaveSeed = inputSeed;
            SaveDataMgr.Instance.SaveData.Seed = ConvertSeedStringToStableInt(inputSeed);
            Debug.Log($"[GameManager] 使用玩家输入种子={inputSeed}");
        }

        if (SaveDataMgr.Instance.SaveData.Seed == 0)
        {
            SaveDataMgr.Instance.SaveData.Seed = 1;
        }

        UnityEngine.Random.InitState(SaveDataMgr.Instance.SaveData.Seed);

        SetNewPlanetData(ReadyPlanetData, ReadyTimeData);
        BasePanel UI_NewGame = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_NewGame, "NewGame");
        string PlayerName = UI_NewGame.GetInputField("新增玩家名称输入框").text;
        ContinueGame(PlayerName);
    }

    private static string GenerateRandomSeedString()
    {
        int seedValue = Environment.TickCount ^ Guid.NewGuid().GetHashCode();
        if (seedValue == int.MinValue)
        {
            seedValue = int.MaxValue;
        }

        return Mathf.Abs(seedValue).ToString();
    }

    private static int ConvertSeedStringToStableInt(string seedText)
    {
        // FNV-1a 32-bit，确保同一字符串始终映射到同一整数种子
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;

            uint hash = offset;
            for (int i = 0; i < seedText.Length; i++)
            {
                hash ^= seedText[i];
                hash *= prime;
            }

            int result = (int)hash;
            if (result == 0)
            {
                result = 1;
            }

            return result;
        }
    }

    [Tooltip("创建一个新星球")]
    public void SetNewPlanetData(PlanetData ReadyPlanetData_, TimeData ReadyTimeData_)
    {
        //根据准备好的星球数据创建新星球存档
        SaveDataMgr.Instance.SaveData.PlanetData_Dict[ReadyPlanetData_.Name] = FastCloner.FastCloner.DeepClone(ReadyPlanetData_);
        SaveDataMgr.Instance.SaveData.DayTimeData.WorldTimeDict[ReadyPlanetData_.Name] = new SerializableTimeData(ReadyTimeData_);
        SaveDataMgr.Instance.SaveData.DayTimeData.SceneLightingRateDict[ReadyPlanetData_.Name] = 1.0f;
    }

    [Tooltip("继续游戏,加载传入的玩家名称,通过名称获取玩家数据, ")]
    public void ContinueGame(string PlayerName)
    {
        // 1. 根据存档立即确定玩家所在的星球名
        SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(PlayerName, out Data_Player playerData);
        string planetName = playerData != null ? playerData.CurrentSceneName : ReadyPlanetData.Name;


        //根据用户当前控制的玩家名称加载玩家 
        RunWorld(NewScenename: planetName, () =>
        {
            //旧场景被卸载完毕 新场景以及被加载完毕
            LoadPlayer(playerName: PlayerName);
        });
    }

    public void RunWorld(string NewScenename, Action onOldSceneUnloaded = null)
    {
        // 标记玩家已进入游戏世界，各管理器可开始运行
        IsInGameWorld = true;

        // 通知所有订阅者：游戏世界已进入
        Event_GameWorldEnter?.Invoke();

        //2. 实例化日月系统
        if (SunAndMoonPrefab != null)
        {
            SunAndMoonObj = Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
            // 确保天体对象在场景切换时不会被销毁
            DontDestroyOnLoad(SunAndMoonObj);
        }
        // 3. 加载时间数据
        DayTimeSystem.Instance.LoadFromSaveData(SaveDataMgr.Instance.SaveData.DayTimeData);

        string OldSceneName = SceneManager.GetActiveScene().name;
        // 2. 立刻创建并激活空场景
        Scene newScene = SceneManager.CreateScene(NewScenename);
        SceneManager.SetActiveScene(newScene);

        // 3. 准备卸载旧场景（如有）
        Scene startScene = SceneManager.GetSceneByName(OldSceneName);
        SceneManager.UnloadSceneAsync(startScene).completed += _ =>
        {
            onOldSceneUnloaded.Invoke();
        };

    }
    #endregion

    #region 场景切换相关
    [Tooltip("切换场景")]
    public void ChangeScene_By_SceneNames(string LastSceneName, string NextSceneName, Action onSceneUnloaded = null)
    {
        // 保存玩家和区块
        ItemMgr.Instance.SavePlayer();

        //保存场景数据
        foreach (var go in ChunkMgr.Instance.Chunk_Dic_ByPos.Values)
        {
            go.SaveChunk();

            SaveDataMgr.Instance.SaveData.PlanetData_Dict[LastSceneName].MapData_Dict[go.MapSave.Name] = go.MapSave;
        }

        ChunkMgr.Instance.OnSceneChange();

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

    public IEnumerator LoadSceneSingleAndInvokeWhenReady(string sceneName, Action onSceneReady = null, int extraWaitFrames = 1, int managerReadyTimeoutFrames = 300)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            throw new ArgumentException("sceneName 不能为空", nameof(sceneName));
        }

        AsyncOperation loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (loadOp == null)
        {
            throw new InvalidOperationException($"[GameManager] 加载场景失败：{sceneName}");
        }

        while (!loadOp.isDone)
        {
            yield return null;
        }

        int waitFrames = Mathf.Max(0, extraWaitFrames);
        for (int i = 0; i < waitFrames; i++)
        {
            yield return null;
        }

        int timeoutFrames = Mathf.Max(1, managerReadyTimeoutFrames);
        while (timeoutFrames > 0)
        {
            bool isReady = SpaceMgr.Instance != null && ItemMgr.Instance != null;
            if (isReady)
            {
                break;
            }

            timeoutFrames--;
            yield return null;
        }

        if (timeoutFrames <= 0)
        {
            throw new TimeoutException($"[GameManager] 场景已加载但管理器未就绪，scene={sceneName}");
        }

        onSceneReady?.Invoke();
    }

    public void StartSpaceTransferWithSpawn(string targetSceneName, ItemData rocketItemData, ItemData playerItemData, string planetBodyId, Vector3 rocketOffset, Vector3 playerOffset)
    {
        StartCoroutine(SpaceTransferWithSpawnCoroutine(targetSceneName, rocketItemData, playerItemData, planetBodyId, rocketOffset, playerOffset));
    }

    private IEnumerator SpaceTransferWithSpawnCoroutine(string targetSceneName, ItemData rocketItemData, ItemData playerItemData, string planetBodyId, Vector3 rocketOffset, Vector3 playerOffset)
    {
        if (string.IsNullOrEmpty(targetSceneName))
        {
            throw new ArgumentException("targetSceneName 不能为空", nameof(targetSceneName));
        }

        if (rocketItemData == null)
        {
            throw new ArgumentNullException(nameof(rocketItemData), "rocketItemData 不能为空");
        }

        if (playerItemData == null)
        {
            throw new ArgumentNullException(nameof(playerItemData), "playerItemData 不能为空");
        }

        if (string.IsNullOrEmpty(planetBodyId))
        {
            throw new ArgumentException("planetBodyId 不能为空", nameof(planetBodyId));
        }

        Exception transferException = null;

        yield return LoadSceneSingleAndInvokeWhenReady(targetSceneName, () =>
        {
            try
            {
                Item rocketItem = SpaceMgr.Instance.InstantiateItemNearPlanet(rocketItemData, planetBodyId, rocketOffset);
                Item playerItem = SpaceMgr.Instance.InstantiateItemNearPlanet(playerItemData, planetBodyId, playerOffset);

                Module_Fly spaceFly = rocketItem.GetMod<Module_Fly>();
                if (spaceFly == null)
                {
                    throw new InvalidOperationException($"[GameManager] 太空火箭缺少 Module_Fly，rocket={rocketItemData.IDName}");
                }

                spaceFly.EnterControlFromTransfer(playerItem);
                Debug.Log($"[GameManager] 太空接管调用完成，rocket={rocketItemData.IDName}, player={playerItemData.IDName}");

                Debug.Log($"[GameManager] 太空迁移完成，planetBodyId={planetBodyId}, rocket={rocketItemData.IDName}, player={playerItemData.IDName}");
            }
            catch (Exception ex)
            {
                transferException = ex;
            }
        });

        if (transferException != null)
        {
            Debug.LogException(transferException);
            throw transferException;
        }
    }
    #endregion

    #region 工具方法

    [Tooltip("在当前场景中实例化并加载玩家")]
    private void LoadPlayer(string playerName)
    {
        Player player = ItemMgr.Instance.LoadPlayer(playerName);

        if (player.Data.transform.position == Vector3.zero)
        {
            // 新玩家：区块地表是异步生成的，使用多帧重试避免同帧读取到空地表
            StartCoroutine(PlaceNewPlayerOnLandThenEnterWorld(player));
            return;
        }

        Event_PlayerEnterWorld?.Invoke(player);
    }

    private IEnumerator PlaceNewPlayerOnLandThenEnterWorld(Player player)
    {
        bool hasPlacedOnLand = false;
        int retryFrames = Mathf.Max(1, spawnSearchRetryFrames);

        for (int i = 0; i < retryFrames; i++)
        {
            if (TryPlaceNewPlayerOnNearestLand(player))
            {
                hasPlacedOnLand = true;
                break;
            }

            yield return null;
        }

        if (!hasPlacedOnLand)
        {
            ItemMgr.Instance.RandomDropInMap(player.gameObject, null, new Vector2Int(-1, -1));
            player.Data.transform.position = player.transform.position;
            Debug.LogWarning($"[GameManager] 新玩家陆地出生点搜索失败（重试帧数={retryFrames}），回退随机投放：{player.transform.position}");
        }

        Event_PlayerEnterWorld?.Invoke(player);
    }

    /// <summary>
    /// 为新玩家寻找最近陆地并设置出生位置
    /// </summary>
    public bool TryGetDefaultPlayerSpawnPosition(out Vector3 spawnPos)
    {
        Vector2Int seedAnchor = GetSeedAnchorPosition();
        if (!TryFindNearestLand(seedAnchor, out Vector2Int landPos))
        {
            spawnPos = Vector3.zero;
            return false;
        }

        spawnPos = new Vector3(landPos.x + 0.5f, landPos.y + 0.5f, 0f);
        return true;
    }

    /// <summary>
    /// 为新玩家寻找最近陆地并设置出生位置
    /// </summary>
    private bool TryPlaceNewPlayerOnNearestLand(Player player)
    {
        if (player == null)
        {
            Debug.LogError("[GameManager] TryPlaceNewPlayerOnNearestLand 失败：player 为空");
            return false;
        }

        if (!TryGetDefaultPlayerSpawnPosition(out Vector3 spawnPos))
        {
            return false;
        }

        player.transform.position = spawnPos;
        player.Data.transform.position = spawnPos;

        Vector2Int seedAnchor = GetSeedAnchorPosition();
        Debug.Log($"[GameManager] 新玩家出生点已定位到陆地：seed={SaveDataMgr.Instance.SaveData.Seed}, anchor={seedAnchor}, spawn={spawnPos}");
        return true;
    }

    /// <summary>
    /// 使用存档种子计算确定性的出生锚点
    /// </summary>
    private Vector2Int GetSeedAnchorPosition()
    {
        int seed = SaveDataMgr.Instance.SaveData.Seed;
        System.Random random = new System.Random(seed);
        int anchorRange = Mathf.Max(0, spawnSeedAnchorRange);

        // 将锚点限制在中心区域，降低首次搜索范围
        int anchorX = random.Next(-anchorRange, anchorRange + 1);
        int anchorY = random.Next(-anchorRange, anchorRange + 1);
        return new Vector2Int(anchorX, anchorY);
    }

    /// <summary>
    /// 以锚点为中心按螺旋环搜索最近陆地
    /// </summary>
    private bool TryFindNearestLand(Vector2Int anchor, out Vector2Int landPos)
    {
        landPos = anchor;
        int maxSearchRadius = Mathf.Max(1, spawnLandMaxSearchRadius);
        HashSet<string> loadedChunkCache = new HashSet<string>();

        if (IsLandTile(anchor, loadedChunkCache))
        {
            landPos = anchor;
            return true;
        }

        for (int radius = 1; radius <= maxSearchRadius; radius++)
        {
            int minX = anchor.x - radius;
            int maxX = anchor.x + radius;
            int minY = anchor.y - radius;
            int maxY = anchor.y + radius;

            for (int x = minX; x <= maxX; x++)
            {
                Vector2Int top = new Vector2Int(x, maxY);
                if (IsLandTile(top, loadedChunkCache))
                {
                    landPos = top;
                    return true;
                }

                Vector2Int bottom = new Vector2Int(x, minY);
                if (IsLandTile(bottom, loadedChunkCache))
                {
                    landPos = bottom;
                    return true;
                }
            }

            for (int y = minY + 1; y <= maxY - 1; y++)
            {
                Vector2Int left = new Vector2Int(minX, y);
                if (IsLandTile(left, loadedChunkCache))
                {
                    landPos = left;
                    return true;
                }

                Vector2Int right = new Vector2Int(maxX, y);
                if (IsLandTile(right, loadedChunkCache))
                {
                    landPos = right;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 判断指定世界坐标是否为可出生陆地
    /// </summary>
    private bool IsLandTile(Vector2Int worldPos, HashSet<string> loadedChunkCache)
    {
        Vector2Int chunkPos = Chunk.GetChunkPosition(worldPos);
        string chunkName = chunkPos.ToString();

        if (!loadedChunkCache.Contains(chunkName))
        {
            ChunkMgr.Instance.LoadChunk_By_Position(chunkPos);
            loadedChunkCache.Add(chunkName);
        }

        if (!ChunkMgr.Instance.TryGetActiveChunkByPos(chunkPos, out Chunk chunk) || chunk == null)
            return false;

        if (chunk.Map == null)
            return false;

        TileData topTile = chunk.Map.GetTopTile(worldPos);
        if (topTile == null)
            return false;

        if (topTile is TileData_Water)
            return false;

        return topTile.IsWalkable;
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

    #endregion

    #region UI相关

    public void OpenHellowCanvas()
    {
        if (UIManager.Instance.GetPanel("HelloCanvas") != null)
        {
            UIManager.Instance.GetPanel("HelloCanvas").Open();
            return;
        }

        if (UIPrefab_HelloCanvas != null)
        {
            BasePanel helloCanvas = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_HelloCanvas);

            helloCanvas.Open();
            helloCanvas.GetButton("选择存档").onClick.AddListener(OpenGameSaveManager);
            helloCanvas.GetButton("新游戏").onClick.AddListener(OpenNewGame);
        }
    }

    public void OpenContextMenu()
    {
        if (UIManager.Instance.GetPanel("ContextMenu") != null)
        {
            UIManager.Instance.GetPanel("ContextMenu").Open();
            return;
        }
        if (UIPrefab_ContextMenu != null)
        {
            BasePanel contextMenu = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_ContextMenu);
            contextMenu.Open();
        }
    }

    public void OpenNewGame()
    {
        if (UIManager.Instance.GetPanel("NewGame") != null)
        {
            UIManager.Instance.GetPanel("NewGame").Open();
            return;
        }
        if (UIPrefab_NewGame != null)
        {
            BasePanel UI_NewGame = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_NewGame, "NewGame");
            UI_NewGame.Open();
            UI_NewGame.GetButton("开始新游戏").onClick.AddListener(CreateNewWorld);
            UI_NewGame.GetButton("返回上一个界面").onClick.AddListener(UI_NewGame.Close);
            // 设置输入框值改变事件
            UI_NewGame.GetInputField("新增玩家名称输入框")?.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
            UI_NewGame.GetInputField("新增存档名称输入框")?.onValueChanged.AddListener(OnPlayerSaveNameChanged);
            UI_NewGame.GetInputField("星球半径输入框")?.onValueChanged.AddListener(OnPlanetReadiusChanged);
            UI_NewGame.GetInputField("噪声缩放输入框")?.onValueChanged.AddListener(OnPlanetNoiseScaleChanged);


        }
    }

    public void OpenGameSaveManager()
    {
        if (UIManager.Instance.GetPanel("UI_GameSaveManager") != null)
        {
            UIManager.Instance.GetPanel("UI_GameSaveManager").Open();
            return;
        }
        if (UIPrefab_SaveManager != null)
        {
            BasePanel saveManager = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_SaveManager, "UI_GameSaveManager");

            // 设置UI事件绑定
            // 设置按钮点击事件
            saveManager.SetButtonOnClick("开始游戏按钮", OnClick_StartGame_Button);
            saveManager.SetButtonOnClick("加载存档按钮", OnClick_LoadSaveData_Button);
            saveManager.GetInputField("选择或新增玩家名称输入框")?.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);

            saveManager.Open();
        }
    }



    #region UI事件处理方法

    private BasePanel GetSaveManagerPanel()
    {
        BasePanel panel = UIManager.Instance.GetPanel(saveManagerPanelName);
        if (panel != null)
            return panel;

        if (!string.IsNullOrEmpty(saveManagerPanelNameLegacy))
        {
            panel = UIManager.Instance.GetPanel(saveManagerPanelNameLegacy);
            if (panel != null)
                return panel;
        }

        Debug.LogError($"未找到存档管理面板: {saveManagerPanelName}");
        return null;
    }

    /// <summary>
    /// 点击开始游戏按钮
    /// </summary>
    public void OnClick_StartGame_Button()
    {
        if (SaveDataMgr.Instance?.SaveData == null || SaveDataMgr.Instance.SaveData.Seed == 0)
        {
            Debug.LogWarning("请先选择存档或创建新游戏");
            return;
        }

        BasePanel saveManager = GetSaveManagerPanel();
        if (saveManager != null)
        {
            ContinueGame(saveManager.GetInputField("选择或新增玩家名称输入框")?.text);
        }
    }

    /// <summary>
    /// 点击开始新游戏按钮
    /// </summary>
    private void OnClick_StartNewGame_Button()
    {
        if (SaveDataMgr.Instance != null)
        {
            BasePanel saveManager = GetSaveManagerPanel();
            if (saveManager != null)
            {
                CreateNewWorld();
            }
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }
    }

    /// <summary>
    /// 点击加载存档按钮
    /// </summary>
    public void OnClick_LoadSaveData_Button()
    {
        if (SaveDataMgr.Instance != null)
        {
            BasePanel saveManager = GetSaveManagerPanel();
            if (saveManager != null)
            {
                var selectedSaveText = saveManager.GetText("选中的存档名称");
                if (selectedSaveText != null)
                {
                    string path = Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData", selectedSaveText.text + ".bytes");
                    SaveDataMgr.Instance.LoadSaveByDisk(path);
                }
            }
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }
        // 生成玩家按钮
        SaveDataManager_UI.Instance.GeneratePlayerButtons();
    }

    /// <summary>
    /// 点击删除存档按钮
    /// </summary>
    public void OnClick_DeletSave_Button()
    {
        if (SaveMenuRightMenuUI.Instance.SelectInfo.Path == "")
        {
            //删除玩家
            SaveDataMgr.Instance.SaveData.PlayerData_Dict.Remove(SaveMenuRightMenuUI.Instance.SelectInfo.Name);
        }
        else if (SaveMenuRightMenuUI.Instance.SelectInfo.Path != "")
        {
            // 删除存档
            if (SaveDataMgr.Instance != null)
            {
                BasePanel saveManager = GetSaveManagerPanel();
                if (saveManager != null)
                {
                    var selectedSaveText = saveManager.GetText("选中的存档名称");
                    if (selectedSaveText != null)
                    {
                        string saveDir = Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData");
                        SaveDataMgr.Instance.DeleteSave(saveDir, selectedSaveText.text);
                    }
                }
            }
        }
        SaveDataManager_UI.Instance.Refresh();
    }

    /// <summary>
    /// 玩家名字输入框实时更新事件
    /// </summary>
    private void OnUpdate_PlayerNameChanged_Text(string newName)
    {
        if (SaveDataMgr.Instance != null)
        {
            SaveDataMgr.Instance.CurrentContrrolPlayerName = newName;
        }
    }

    /// <summary>
    /// 存档名字输入框实时更新事件
    /// </summary>
    private void OnPlayerSaveNameChanged(string newName)
    {
        if (SaveDataMgr.Instance != null && SaveDataMgr.Instance.SaveData != null)
        {
            SaveDataMgr.Instance.SaveData.saveName = newName;
        }
    }

    /// <summary>
    /// 星球半径输入框实时更新事件
    /// </summary>
    private void OnPlanetReadiusChanged(string newValue)
    {
        // 检测传入的字符串是否为有效的整数
        if (int.TryParse(newValue, out int radius))
        {
            SaveDataManager_UI.Instance.Ready_planetData.Radius = radius;
        }
        else
        {
            // 非法输入，不做处理，必要时可提示用户
            Debug.LogWarning($"输入的半径值无效：{newValue}");
        }
    }

    /// <summary>
    /// 星球噪声缩放输入框实时更新事件
    /// </summary>
    private void OnPlanetNoiseScaleChanged(string newValue)
    {
        // 检测传入的字符串是否为有效的浮点数
        if (float.TryParse(newValue, out float noiseScale))
        {
            SaveDataManager_UI.Instance.Ready_planetData.NoiseScale = noiseScale;
        }
        else
        {
            // 非法输入，不做处理，必要时可提示用户
            Debug.LogWarning($"输入的噪声缩放值无效：{newValue}");
        }
    }

    #endregion

    #endregion
}