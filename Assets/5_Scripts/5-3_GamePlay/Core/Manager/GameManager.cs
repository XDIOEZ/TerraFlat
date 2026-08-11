// AI-Context: 游戏世界总生命周期与出生点服务；出生点必须按新版纯生成 Profile 计算，先传送玩家、再由 Mod_ChunkLoader 正常流送周围 Chunk。严禁搜索时注册运行时 Chunk 或默认投放到水面。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

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

    /// <summary>最近一次手动保存结果；保存进行中为 null。</summary>
    public bool? LastSaveSucceeded { get; private set; }



    #endregion

    [SerializeField]
    private GameObject SunAndMoonPrefab;
    [Header("寻路系统")]
    [FormerlySerializedAs("PathFindingSystem")]
    public GameObject NavigationSystem;
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
    [SerializeField, Min(1)] private int spawnTerrainSampleBudget = 4096;
    [SerializeField, Min(0)] private int spawnSeedAnchorRange = 256;

    #region 生命周期方法
    protected override void Awake()
    {
        if (instance == null)
        {
            base.Awake();
            return;
        }

        if (instance == this)
            return;

        // GameManager 依赖 WorldManager Prefab 上的序列化配置。若较早的代码路径
        // 通过 SingletonAutoMono 自动创建了空实例，必须由场景中的已配置实例接管。
        if (UIPrefab_HelloCanvas != null && instance.UIPrefab_HelloCanvas == null)
        {
            Debug.LogWarning(
                "[GameManager] 已用 GameStartScene 中配置完整的实例替换自动创建的空实例。",
                this);
            Destroy(instance);
            instance = this;
            return;
        }

        Destroy(gameObject);
    }

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        InitializeWorldEntryPresentation();
        AutoSaveController.Ensure(this);
        _ = DimensionManager.Instance;
        _ = FlatWorld.Gameplay.Events.GameEventManager.Instance;

        // 寻路系统不在 StartScene 激活，等玩家进入游戏世界后再启用
        // WorldNavigationManager 在进入世界后注册当前已加载区块。

        Time.timeScale = 1;

        BackToHelloScene_Event_End += OpenHellowCanvas;
    }

    protected override void OnDestroy()
    {
        ChunkGenerator_River.ClearHydrologyCache();
        ResetWorldEntryLifecycle();
        DisposeWorldEntryPresentation();
        base.OnDestroy();
    }
    #endregion

    #region 退出游戏相关
    /// <summary>
    /// 使用协程处理退出游戏逻辑，解决保存与销毁的时序问题
    /// </summary>
    /// <param name="onComplete">退出完成后的回调函数</param>
    /// <returns></returns>
    public IEnumerator BackToHelloScene_Coroutine(
        Item playerItem,
        System.Action onComplete = null,
        bool saveCurrentGame = true)
    {
        Debug.Log("<color=yellow>[ExitGame]</color> 开始执行退出流程...");

        // 记录退出前的动态世界场景；LoadSceneMode.Single 理论上会卸载它，
        // 后续仍会显式核验，防止异常路径留下同名空场景阻塞下一次进入。
        Scene exitingWorldScene = SceneManager.GetActiveScene();

        ////////////////////////////////////////////////////////////////////////////////////
        // 阶段 1：准备阶段
        ////////////////////////////////////////////////////////////////////////////////////

        // 标记已退出游戏世界，各管理器应停止运行
        IsInGameWorld = false;
        ResetWorldEntryLifecycle();

        // 通知所有订阅者：游戏世界已退出
        Event_GameWorldExit?.Invoke();
        ChunkGenerator_River.ClearHydrologyCache();

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

        if (saveCurrentGame)
        {
            // 提前保存玩家数据（在销毁逻辑执行前）
            Debug.Log("[ExitGame] 开始保存玩家数据...");
            ItemMgr.Instance.SavePlayer();

            // SaveDataMgr 在写盘前统一保存全部已加载区块，避免同一批区块被扫描两次。
            Debug.Log("[ExitGame] 写入存档文件...");
            SaveDataMgr.Instance.Save_And_WriteToDiskAndRecordExitTime();
        }
        else
        {
            Debug.Log("[ExitGame] 已选择不保存，跳过玩家、区块与退出时间写盘。");
        }

        ////////////////////////////////////////////////////////////////////////////////////
        // 阶段 3：清理阶段
        ////////////////////////////////////////////////////////////////////////////////////

        // 通过 ItemMgr 正式注销玩家，确保运行时索引、感知空间哈希和 Player_DIC 同步清理。
        if (playerItem is Player player)
        {
            ItemMgr.Instance.ReleasePlayerForWorldTransition(player);
            Debug.Log("[ExitGame] 已注销玩家对象");
        }
        else if (playerItem != null)
        {
            ItemMgr.Instance.DespawnItem(playerItem, saveData: false);
            Debug.Log("[ExitGame] 已注销非 Player 玩家对象");
        }

        // 延迟一帧，等待所有标记为销毁的对象实际销毁
        yield return null;
        ItemMgr.Instance.CleanupNullItems();

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

        // Chunk 对象池会在帧末销毁旧 Item，确认完成后再重建索引，
        // 避免下次 Event_GameWorldEnter 读取到已销毁的 GameItem。
        yield return null;
        ItemMgr.Instance.CleanupNullItems();

        // 等待 Unity 在主线程完成资源卸载。不在场景切换中强制执行托管终结器，
        // 避免与 Unity 原生对象的延迟销毁发生重入。
        AsyncOperation unloadUnusedAssets = Resources.UnloadUnusedAssets();
        if (unloadUnusedAssets != null)
        {
            while (!unloadUnusedAssets.isDone)
                yield return null;
        }

        ////////////////////////////////////////////////////////////////////////////////////
        // 阶段 4：场景切换阶段
        ////////////////////////////////////////////////////////////////////////////////////

        Debug.Log("[ExitGame] 准备加载 GameStartScene...");
        AsyncOperation loadOp = SceneManager.LoadSceneAsync("GameStartScene");
        while (!loadOp.isDone)
            yield return null;

        // Single 加载应已卸载旧动态场景；若平台/异常时序未完成，补一次显式卸载并等待结束。
        if (exitingWorldScene.IsValid() &&
            exitingWorldScene.isLoaded &&
            !string.Equals(exitingWorldScene.name, "GameStartScene", StringComparison.Ordinal))
        {
            Debug.LogWarning($"[ExitGame] 检测到残留世界场景，补充卸载：{exitingWorldScene.name}");
            AsyncOperation unloadWorldScene = SceneManager.UnloadSceneAsync(exitingWorldScene);
            while (unloadWorldScene != null && !unloadWorldScene.isDone)
                yield return null;
        }

        // 场景卸载后再清理一次，覆盖不归属 Chunk 的运行时 Item。
        ItemMgr.Instance?.CleanupNullItems();

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
    /// <summary>
    /// 使用完整请求创建新世界。该入口不读取任何 UI，可由菜单、自动化测试或其他系统调用。
    /// </summary>
    public bool CreateNewWorld(NewWorldCreationRequest request)
    {
        if (!EnsureContentReady("创建新世界"))
            return false;

        if (request == null)
        {
            Debug.LogError("[GameManager] 创建新世界失败：请求为空");
            return false;
        }

        if (!request.TryValidate(out string validationError))
        {
            Debug.LogError($"[GameManager] 创建新世界失败：{validationError}");
            return false;
        }

        SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
        if (saveDataMgr == null)
        {
            Debug.LogError("[GameManager] 创建新世界失败：SaveDataMgr 未就绪");
            return false;
        }

        if (!BeginWorldEntry("正在创建新世界", "正在准备新存档数据…", 0.08f))
            return false;

        StartCoroutine(CreateNewWorldCoroutine(saveDataMgr, request));
        return true;
    }

    private IEnumerator CreateNewWorldCoroutine(
        SaveDataMgr saveDataMgr,
        NewWorldCreationRequest request)
    {
        // 先让加载 Prefab 完成一帧渲染，再执行存档和世界初始化。
        yield return null;

        try
        {
            saveDataMgr.ResetChunkDifferenceState();
            saveDataMgr.SaveData = new GameSaveData();
            saveDataMgr.CurrentContrrolPlayerName = request.PlayerName;
            ApplyNewWorldDifficulty(saveDataMgr.SaveData, request);
            ReportWorldEntryProgress("正在创建新世界", "正在生成世界种子…", 0.2f);

            string inputSeed = request.Seed;
            bool isNoSeedInput = string.IsNullOrWhiteSpace(inputSeed);

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

            ReadyPlanetData = FastCloner.FastCloner.DeepClone(request.PlanetData);
            ReadyTimeData = request.TimeData.CreateRuntimeCopy();
            ReadyGameSaveData = new GameSaveData
            {
                SaveSeed = saveDataMgr.SaveData.SaveSeed,
                Seed = saveDataMgr.SaveData.Seed
            };

            ReportWorldEntryProgress("正在创建新世界", "正在创建星球数据…", 0.32f);
            SetNewPlanetData(ReadyPlanetData, ReadyTimeData);
            ReportWorldEntryProgress("正在创建新世界", "正在写入首个存档…", 0.45f);
            if (!saveDataMgr.TryCreateNewSave(saveDataMgr.SaveData, request.SaveName, out string createdSaveName))
            {
                FailWorldEntry("首个存档文件未能写入磁盘。");
                yield break;
            }

            Debug.Log($"[GameManager] 已创建新世界存档：{createdSaveName}");
            ReportWorldEntryProgress("正在创建新世界", "存档已创建，正在进入世界…", 0.55f);
            ContinueGameInternal(request.PlayerName, request.PlanetData.Name);
        }
        catch (Exception exception)
        {
            FailWorldEntry("创建新世界时发生错误。", exception);
        }
    }

    private static void ApplyNewWorldDifficulty(
        GameSaveData saveData,
        NewWorldCreationRequest request)
    {
        if (saveData == null || request == null)
            return;

        saveData.Difficulty = request.Difficulty;
        GameDifficultyCatalog.WriteCustomRules(saveData, request.CustomDifficultyRules);
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

        if (!BeginWorldEntry("正在进入存档", "正在准备世界数据…", 0.12f))
            return;

        StartCoroutine(ContinueGameCoroutine(PlayerName));
    }

    private IEnumerator ContinueGameCoroutine(string playerName)
    {
        // 确保玩家至少看到一帧加载面板，避免同步准备阶段表现为卡死。
        yield return null;
        ContinueGameInternal(playerName);
    }

    private void ContinueGameInternal(string playerName, string fallbackPlanetName = null)
    {
        try
        {
            SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
            if (saveDataMgr?.SaveData == null)
            {
                FailWorldEntry("当前没有可进入的存档数据。");
                return;
            }

            saveDataMgr.CurrentContrrolPlayerName = playerName;

            // 1. 根据存档立即确定玩家所在的星球名
            saveDataMgr.SaveData.PlayerData_Dict.TryGetValue(playerName, out Data_Player playerData);
            string planetName = ResolveWorldKeyForPlayer(
                saveDataMgr.SaveData,
                playerData,
                fallbackPlanetName);
            if (string.IsNullOrWhiteSpace(planetName))
            {
                FailWorldEntry("存档中没有可进入的星球数据。");
                return;
            }

            ReportWorldEntryProgress("正在进入存档", $"正在加载星球：{planetName}", 0.38f);

            // 根据用户当前控制的玩家名称加载玩家。
            RunWorld(NewScenename: planetName, () =>
            {
                try
                {
                    ReportWorldEntryProgress("正在进入存档", "正在创建玩家并准备出生区域…", 0.66f);
                    LoadPlayer(playerName: playerName);
                }
                catch (Exception exception)
                {
                    FailWorldEntry("加载玩家时发生错误。", exception);
                }
            });
        }
        catch (Exception exception)
        {
            FailWorldEntry("进入存档时发生错误。", exception);
        }
    }

    private string ResolveWorldKeyForPlayer(
        GameSaveData saveData,
        Data_Player playerData,
        string preferredWorldKey)
    {
        if (saveData?.PlanetData_Dict == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(playerData?.CurrentSceneName))
            return playerData.CurrentSceneName;

        if (!string.IsNullOrWhiteSpace(preferredWorldKey) &&
            saveData.PlanetData_Dict.ContainsKey(preferredWorldKey))
        {
            return preferredWorldKey;
        }

        if (!string.IsNullOrWhiteSpace(ReadyPlanetData?.Name) &&
            saveData.PlanetData_Dict.ContainsKey(ReadyPlanetData.Name))
        {
            return ReadyPlanetData.Name;
        }

        foreach (string worldKey in saveData.PlanetData_Dict.Keys)
            return worldKey;

        return string.Empty;
    }

    public void RunWorld(string NewScenename, Action onOldSceneUnloaded = null)
    {
        if (!EnsureContentReady("进入世界"))
            return;

        WorldAddress worldAddress = WorldAddress.FromWorldKey(NewScenename);
        DimensionManager.Instance.EnsureWorldData(worldAddress);

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
        DimensionManager.Instance.ActivateWorld(worldAddress);

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
        if (player?.Data != null)
        {
            player.Data.CurrentSceneName = SceneManager.GetActiveScene().name;
            SaveDataMgr.Instance.SaveData.PlayerData_Dict[player.Data.Name_User] = player.Data;
        }

        if (RequiresInitialPlayerPlacement(player))
        {
            // 新玩家：先按种子纯计算出生位置，再通过玩家进入事件触发常规 Chunk 流送。
            StartCoroutine(PlaceNewPlayerOnLandThenEnterWorld(player));
            return;
        }

        Event_PlayerEnterWorld?.Invoke(player);
    }

    private static bool RequiresInitialPlayerPlacement(Player player)
    {
        // 世界原点是合法存档坐标，不能再用 Vector3.zero 充当“新玩家”哨兵。
        return player != null && player.IsNewProfile;
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
        if (player == null || player.Data == null)
        {
            FailWorldEntry("新玩家数据无效，无法准备出生区域。");
            yield break;
        }

        Vector2Int seedAnchor = GetSeedAnchorPosition();
        int worldSeed = GetActiveWorldGenerationSeed();
        ReportWorldEntryProgress("正在进入存档", "正在根据世界种子定位安全出生点…", 0.7f);

        // 读取已加载的 MapCore Prefab 配置做纯采样，不实例化 Map 或 Chunk。
        // 玩家位置设置完成后才触发 Event_PlayerEnterWorld，由 Mod_ChunkLoader 正常流送周围区块。
        if (!TryFindNearestLand(seedAnchor, out Vector2Int landPosition, out string failureReason))
        {
            FailWorldEntry(
                $"{failureReason} seed={worldSeed}, anchor={seedAnchor}。请检查世界噪声缩放和生物群系配置。");
            yield break;
        }

        Vector3 spawnPosition = new Vector3(landPosition.x + 0.5f, landPosition.y + 0.5f, 0f);
        player.transform.position = spawnPosition;
        player.Data.transform.position = spawnPosition;
        // 以最终确认的安全陆地坐标写入一次主世界出生点，确保复活不会回到死亡位置。
        PlayerMainWorldSpawnStore.SetMainWorldSpawn(
            player.Data,
            SceneManager.GetActiveScene().name,
            spawnPosition);
        Debug.Log($"[GameManager] 新玩家出生点已按种子定位到安全陆地：seed={worldSeed}, anchor={seedAnchor}, spawn={spawnPosition}");
        Event_PlayerEnterWorld?.Invoke(player);
    }

    /// <summary>
    /// 为新玩家寻找附近陆地并设置出生位置。
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

        return TryGetLoadedLandTile(Vector2Int.FloorToInt(position));
    }

    /// <summary>
    /// 从指定世界坐标向外寻找附近的可行走陆地，供联机玩家错位修正和多人错峰出生。
    /// </summary>
    public bool TryGetNearestLandSpawnPosition(Vector3 preferredPosition, out Vector3 spawnPos)
    {
        if (float.IsNaN(preferredPosition.x) || float.IsInfinity(preferredPosition.x) ||
            float.IsNaN(preferredPosition.y) || float.IsInfinity(preferredPosition.y))
        {
            spawnPos = Vector3.zero;
            return false;
        }

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
    /// 用当前维度的纯生成 Profile 与存档星球数据采样候选陆地。
    /// 该路径只创建可释放的临时地形，不实例化 Map，也不注册运行时 Chunk。
    /// </summary>
    private bool TryFindNearestLand(Vector2Int anchor, out Vector2Int landPos)
    {
        return TryFindNearestLand(anchor, out landPos, out _);
    }

    private bool TryFindNearestLand(
        Vector2Int anchor,
        out Vector2Int landPos,
        out string failureReason)
    {
        landPos = anchor;
        failureReason = string.Empty;
        ChunkMgr runtimeManager = ChunkMgr.Instance;
        if (runtimeManager?.WorldRuntime != null && runtimeManager.Chunks.Count > 0 &&
            runtimeManager.TryFindRuntimeWalkableLandNear(
                anchor,
                Mathf.Max(1, spawnLandMaxSearchRadius),
                Mathf.Max(1, spawnTerrainSampleBudget),
                out landPos))
        {
            return true;
        }

        if (!TryGetSpawnGenerationInput(
                out FlatWorld.WorldModel.ChunkGenerationProfileSnapshot profile,
                out FlatWorld.WorldModel.ChunkGenerationTopologySnapshot topology,
                out string dimensionId,
                out failureReason))
        {
            return false;
        }

        var generator = new FlatWorld.WorldModel.DeterministicChunkGenerator();
        if (generator.TryFindWalkableSurfaceNear(
                dimensionId,
                GetActiveWorldGenerationSeed(),
                profile,
                topology,
                new FlatWorld.WorldModel.Int2(anchor.x, anchor.y),
                Mathf.Max(1, spawnLandMaxSearchRadius),
                Mathf.Max(1, spawnTerrainSampleBudget),
                out FlatWorld.WorldModel.Int2 result))
        {
            landPos = new Vector2Int(result.X, result.Y);
            return true;
        }

        failureReason = "未能在出生范围内采样到非水且可行走的陆地。";
        return false;
    }

    /// <summary>
    /// 从当前维度取得正式纯生成快照、坐标缩放与有限世界拓扑。
    /// </summary>
    private static bool TryGetSpawnGenerationInput(
        out FlatWorld.WorldModel.ChunkGenerationProfileSnapshot profile,
        out FlatWorld.WorldModel.ChunkGenerationTopologySnapshot topology,
        out string dimensionId,
        out string failureReason)
    {
        profile = null;
        topology = default;
        dimensionId = string.Empty;
        failureReason = string.Empty;

        SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
        if (saveDataMgr?.SaveData?.PlanetData_Dict == null)
        {
            failureReason = "当前星球存档数据未就绪。";
            return false;
        }

        PlanetData planetData = saveDataMgr.GetCurrentPlanetData();
        if (planetData == null)
        {
            failureReason = "当前场景未找到对应的星球生成数据。";
            return false;
        }

        DimensionManager dimensionManager = DimensionManager.Instance;
        if (dimensionManager == null)
        {
            failureReason = "维度管理器未就绪。";
            return false;
        }

        if (dimensionManager.ActiveDefinition?.GenerationMode == DimensionGenerationMode.Cave)
        {
            failureReason = "矿洞维度不使用地表陆地出生采样。";
            return false;
        }

        ChunkGenerationProfileSO profileAsset = dimensionManager.GetActiveGenerationProfile();
        if (profileAsset == null)
        {
            failureReason = "当前维度缺少区块生成 Profile。";
            return false;
        }

        float noiseScale = planetData.NoiseScale;
        if (float.IsNaN(noiseScale) || float.IsInfinity(noiseScale) || noiseScale <= 0f)
            noiseScale = PlanetData.DefaultNoiseScale;
        noiseScale = PlanetData.NormalizeNoiseScale(noiseScale);
        profile = profileAsset.CreateSnapshot().WithNumericParameter(
            "world.coordinateScale", noiseScale);
        // 出生搜索与 ChunkMgr 后台生成必须经过同一运行时覆盖，避免选中的陆地生成后变成水。
        profile = WorldGenerationRuntimeHooks.ApplyBeforeWorldModelGeneration(profile);
        dimensionId = dimensionManager.ActiveDefinition?.DimensionId;
        if (string.IsNullOrWhiteSpace(dimensionId))
            dimensionId = WorldAddress.SurfaceDimensionId;

        if (WorldTopologyBounds.TryCreate(planetData, out WorldTopologyBounds bounds))
        {
            topology = new FlatWorld.WorldModel.ChunkGenerationTopologySnapshot(
                new FlatWorld.WorldModel.Int2(bounds.Min.x, bounds.Min.y),
                new FlatWorld.WorldModel.Int2(bounds.Span.x, bounds.Span.y));
        }

        return true;
    }

    private int GetActiveWorldGenerationSeed()
    {
        int baseSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        if (baseSeed == 0)
            baseSeed = 1;

        return DimensionManager.Instance != null
            ? DimensionManager.Instance.GetActiveGenerationSeed(baseSeed)
            : baseSeed;
    }

    private static bool TryGetLoadedLandTile(Vector2Int worldPosition)
    {
        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (chunkMgr != null && chunkMgr.TryGetRuntimeTerrainTile(
                worldPosition + new Vector2(0.5f, 0.5f), out RuntimeTerrainTileSample sample))
        {
            return (sample.Cell.Flags & FlatWorld.WorldModel.TerrainCellFlags.Water) == 0 &&
                   sample.Terrain.IsWalkable(sample.LocalCell.x, sample.LocalCell.y);
        }

        if (chunkMgr == null ||
            !chunkMgr.TryGetActiveChunkByPos(Chunk.GetChunkPosition(worldPosition), out Chunk chunk) ||
            chunk?.Map?.Data == null ||
            !chunk.Map.Data.TileLoaded)
        {
            return false;
        }

        return IsWalkableLandTile(chunk.Map.GetTopTile(worldPosition));
    }

    private static bool IsWalkableLandTile(TileData tile)
    {
        return tile != null && !(tile is TileData_Water) && tile.IsWalkable;
    }

    /// <summary>
    /// 保存游戏数据专用方法；分帧采集区块并后台原子写盘，不阻塞主线程。
    /// </summary>
    public void SaveGame()
    {
        if (manualSaveCoroutine != null)
            return;

        LastSaveSucceeded = null;
        BeginSaveStatus();
        manualSaveCoroutine = StartCoroutine(SaveGameInBackgroundCoroutineWithStatus());
    }

    private Coroutine manualSaveCoroutine;

    /// <summary>等待分帧快照与后台写盘完成，结束后更新右上角保存状态。</summary>
    private IEnumerator SaveGameInBackgroundCoroutineWithStatus()
    {
        Task<bool> writeTask = null;
        bool succeeded = false;
        Exception saveFailure = null;

        yield return SaveGameInBackgroundCoroutine(task => writeTask = task);
        if (writeTask == null)
        {
            saveFailure = new InvalidOperationException("手动保存未创建后台写入任务。");
        }
        else
        {
            while (!writeTask.IsCompleted)
                yield return null;

            try
            {
                succeeded = writeTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                saveFailure = exception;
            }
        }

        if (saveFailure != null)
        {
            Debug.LogException(new InvalidOperationException("[GameManager] 手动保存失败。", saveFailure));
        }

        LastSaveSucceeded = succeeded;
        CompleteSaveStatus(succeeded);
        manualSaveCoroutine = null;
    }

    /// <summary>
    /// 自动保存入口：保持实体和输入继续运行，主线程负责采集/构建字节快照，磁盘原子写入由后台任务完成。
    /// </summary>
    public Task<bool> SaveGameInBackground()
    {
        CaptureSaveGameState();
        return SaveDataMgr.Instance.Save_And_WriteToDiskInBackground();
    }

    /// <summary>自动保存的分帧入口；快照完成后只返回后台文件写入任务。</summary>
    public IEnumerator SaveGameInBackgroundCoroutine(Action<Task<bool>> onWriteQueued)
    {
        Exception captureFailure = null;
        try
        {
            CaptureSaveGameState();
        }
        catch (Exception exception)
        {
            captureFailure = exception;
        }

        if (captureFailure != null)
        {
            onWriteQueued?.Invoke(Task.FromException<bool>(captureFailure));
            yield break;
        }

        yield return SaveDataMgr.Instance.Save_And_WriteToDiskInBackgroundCoroutine(onWriteQueued);
    }

    /// <summary>采集世界时钟和本地玩家的持久化状态。</summary>
    private static void CaptureSaveGameState()
    {
        if (SaveDataMgr.Instance?.SaveData == null)
            throw new InvalidOperationException("存档管理器或当前存档尚未就绪，无法保存世界。");
        if (DayTimeSystem.Instance == null)
            throw new InvalidOperationException("世界时间系统尚未就绪，无法保存世界。");
        if (ItemMgr.Instance == null)
            throw new InvalidOperationException("物品管理器尚未就绪，无法保存世界。");

        // 保存时间数据
        SaveDataMgr.Instance.SaveData.DayTimeData = DayTimeSystem.Instance.GetSaveData();

        // 先保存玩家数据
        ItemMgr.Instance.SavePlayer();
    }

    #endregion

}
