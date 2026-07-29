// AI-Context: 游戏世界总生命周期与出生点服务；出生点查询可能触发区块加载，调用方应允许跨帧重试，严禁在搜索失败时默认投放到水面。
using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class GameManager : SingletonAutoMono<GameManager>
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
    [Header("新玩家出生点搜索配置")]
    [SerializeField, Min(1)] private int spawnLandMaxSearchRadius = 256;
    [SerializeField, Min(0)] private int spawnSeedAnchorRange = 256;
    [SerializeField, Min(1)] private int spawnSearchRetryFrames = 60;

    #region 生命周期方法
    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        AutoSaveController.Ensure(this);

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
        SaveDataMgr.Instance.Save_And_WriteToDiskAndRecordExitTime();

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
        SaveDataMgr.Instance.ResetChunkDifferenceState();
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
        if (!EnsureContentReady("创建新世界"))
            return;

        SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
        if (saveDataMgr == null)
        {
            Debug.LogError("[GameManager] 创建新世界失败：SaveDataMgr 未就绪");
            return;
        }

        ReadNewGameCreationInputs(out string requestedSaveName, out string playerName);

        if (!BeginWorldEntryLoading("正在创建新世界", "正在准备新存档数据…", 0.08f))
            return;

        StartCoroutine(CreateNewWorldCoroutine(saveDataMgr, requestedSaveName, playerName));
    }

    private IEnumerator CreateNewWorldCoroutine(
        SaveDataMgr saveDataMgr,
        string requestedSaveName,
        string playerName)
    {
        // 先让加载 Prefab 完成一帧渲染，再执行存档和世界初始化。
        yield return null;

        try
        {
            saveDataMgr.ResetChunkDifferenceState();
            saveDataMgr.SaveData = new GameSaveData();
            ApplyPendingNewWorldDifficulty(saveDataMgr.SaveData);
            SetWorldLoadingView("正在创建新世界", "正在生成世界种子…", 0.2f);

            string inputSeed = ReadyGameSaveData.SaveSeed?.Trim();
            bool isNoSeedInput = string.IsNullOrEmpty(inputSeed) || inputSeed == "0";

            if (isNoSeedInput)
            {
                string randomSeed = GenerateRandomSeedString();
                saveDataMgr.SaveData.SaveSeed = randomSeed;
                saveDataMgr.SaveData.Seed = ConvertSeedStringToStableInt(randomSeed);
                Debug.Log($"[GameManager] 玩家未输入种子，自动生成随机种子={randomSeed}");
            }
            else
            {
                saveDataMgr.SaveData.SaveSeed = inputSeed;
                saveDataMgr.SaveData.Seed = ConvertSeedStringToStableInt(inputSeed);
                Debug.Log($"[GameManager] 使用玩家输入种子={inputSeed}");
            }

            if (saveDataMgr.SaveData.Seed == 0)
                saveDataMgr.SaveData.Seed = 1;

            UnityEngine.Random.InitState(saveDataMgr.SaveData.Seed);

            SetWorldLoadingView("正在创建新世界", "正在创建星球数据…", 0.32f);
            SetNewPlanetData(ReadyPlanetData, ReadyTimeData);
            SetWorldLoadingView("正在创建新世界", "正在写入首个存档…", 0.45f);
            if (!saveDataMgr.TryCreateNewSave(saveDataMgr.SaveData, requestedSaveName, out string createdSaveName))
            {
                FailWorldEntryLoading("首个存档文件未能写入磁盘。");
                yield break;
            }

            Debug.Log($"[GameManager] 已创建新世界存档：{createdSaveName}");
            SetWorldLoadingView("正在创建新世界", "存档已创建，正在进入世界…", 0.55f);
            ContinueGameInternal(playerName);
        }
        catch (Exception exception)
        {
            FailWorldEntryLoading("创建新世界时发生错误。", exception);
        }
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
        if (ReadyPlanetData_ == null || string.IsNullOrEmpty(ReadyPlanetData_.Name))
        {
            Debug.LogError("[GameManager] 创建新星球失败：ReadyPlanetData 或星球名称为空");
            return;
        }

        TimeData timeData = ReadyTimeData_ ?? new TimeData();

        //根据准备好的星球数据创建新星球存档
        SaveDataMgr.Instance.SaveData.PlanetData_Dict[ReadyPlanetData_.Name] = FastCloner.FastCloner.DeepClone(ReadyPlanetData_);
        SaveDataMgr.Instance.SaveData.DayTimeData.WorldTimeDict[ReadyPlanetData_.Name] = new SerializableTimeData(timeData);
        SaveDataMgr.Instance.SaveData.DayTimeData.SceneLightingRateDict[ReadyPlanetData_.Name] = 1.0f;
    }

    [Tooltip("继续游戏,加载传入的玩家名称,通过名称获取玩家数据, ")]
    public void ContinueGame(string PlayerName)
    {
        if (!EnsureContentReady("继续游戏"))
            return;

        if (!BeginWorldEntryLoading("正在进入存档", "正在准备世界数据…", 0.12f))
            return;

        StartCoroutine(ContinueGameCoroutine(PlayerName));
    }

    private IEnumerator ContinueGameCoroutine(string playerName)
    {
        // 确保玩家至少看到一帧加载面板，避免同步准备阶段表现为卡死。
        yield return null;
        ContinueGameInternal(playerName);
    }

    private void ContinueGameInternal(string playerName)
    {
        try
        {
            SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
            if (saveDataMgr?.SaveData == null)
            {
                FailWorldEntryLoading("当前没有可进入的存档数据。");
                return;
            }

            // 1. 根据存档立即确定玩家所在的星球名
            saveDataMgr.SaveData.PlayerData_Dict.TryGetValue(playerName, out Data_Player playerData);
            string planetName = playerData != null ? playerData.CurrentSceneName : ReadyPlanetData.Name;
            SetWorldLoadingView("正在进入存档", $"正在加载星球：{planetName}", 0.38f);

            // 根据用户当前控制的玩家名称加载玩家。
            RunWorld(NewScenename: planetName, () =>
            {
                try
                {
                    SetWorldLoadingView("正在进入存档", "正在创建玩家并准备出生区域…", 0.66f);
                    LoadPlayer(playerName: playerName);
                }
                catch (Exception exception)
                {
                    FailWorldEntryLoading("加载玩家时发生错误。", exception);
                }
            });
        }
        catch (Exception exception)
        {
            FailWorldEntryLoading("进入存档时发生错误。", exception);
        }
    }

    public void RunWorld(string NewScenename, Action onOldSceneUnloaded = null)
    {
        if (!EnsureContentReady("进入世界"))
            return;

        // 标记玩家已进入游戏世界，各管理器可开始运行
        IsInGameWorld = true;

        // 光照层管理器需要在区块开始加载前就绪，以便持续维护每格光照数据。
        _ = LightLayerMgr.Instance;

        //2. 实例化日月系统
        if (SunAndMoonPrefab != null)
        {
            SunAndMoonObj = Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
            // 确保天体对象在场景切换时不会被销毁
            DontDestroyOnLoad(SunAndMoonObj);
        }

        string OldSceneName = SceneManager.GetActiveScene().name;
        // 2. 立刻创建并激活空场景
        Scene newScene = SceneManager.CreateScene(NewScenename);
        SceneManager.SetActiveScene(newScene);

        // 通知所有订阅者：游戏世界已进入
        Event_GameWorldEnter?.Invoke();

        // 3. 准备卸载旧场景（如有）
        Scene startScene = SceneManager.GetSceneByName(OldSceneName);
        SceneManager.UnloadSceneAsync(startScene).completed += _ =>
        {
            onOldSceneUnloaded?.Invoke();
        };

    }

    private static bool EnsureContentReady(string actionName)
    {
        if (GameRes.Instance == null || !GameRes.Instance.isLoadFinish)
        {
            Debug.LogWarning($"[GameManager] 无法{actionName}：游戏资源仍在加载。");
            return false;
        }

        ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
        if (modRuntime == null || !modRuntime.IsReady)
        {
            string reason = modRuntime?.FailureReason;
            Debug.LogError($"[GameManager] 无法{actionName}：MOD 框架未就绪。{reason}");
            return false;
        }

        return true;
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

    /// <summary>
    /// 联机身份完成核心 Player Item 初始化后，复用单机玩家进入世界事件。
    /// </summary>
    public void NotifyNetworkLocalPlayerEntered(Player player)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));

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
    /// 判断当前位置所在格是否为可行走陆地；联机服务端用它验证旧存档出生点。
    /// </summary>
    public bool IsValidLandSpawnPosition(Vector3 position)
    {
        if (float.IsNaN(position.x) || float.IsInfinity(position.x) ||
            float.IsNaN(position.y) || float.IsInfinity(position.y))
        {
            return false;
        }

        return IsLandTile(Vector2Int.FloorToInt(position), new HashSet<string>());
    }

    /// <summary>
    /// 从指定世界坐标向外寻找最近的可行走陆地，供联机玩家错位修正和多人错峰出生。
    /// </summary>
    public bool TryGetNearestLandSpawnPosition(Vector3 preferredPosition, out Vector3 spawnPos)
    {
        Vector2Int anchor = Vector2Int.FloorToInt(preferredPosition);
        if (!TryFindNearestLand(anchor, out Vector2Int landPos))
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

}
