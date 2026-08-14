using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// AI-Context: Item 实例、运行时注册和网络玩家副本的生命周期管理器；远程视觉物不得进入本地 Tick/存档索引。

public partial class ItemMgr : SingletonMono<ItemMgr>
{
    /// <summary>
    /// AI-Context: 网络层通过这两个事件观察运行时 Item 生命周期；GamePlay 层不反向依赖 Mirror。
    /// 事件发生在注册完成后、销毁注销前，订阅方不得在回调内再次销毁同一 Item。
    /// </summary>
    public static event Action<Item> RuntimeItemInstantiated;
    public static event Action<Item> RuntimeItemDespawning;

    private const string GROUP_MAP_CORE = "MapCore";

    #region Runtime Data

    [ShowInInspector]
    public Dictionary<int, Item> WorldRunTimeItems => _runtimeRegistry.ItemsByGuid;

    [ShowInInspector]
    public Dictionary<string, List<Item>> RuntimeItemsGroup => _runtimeRegistry.Groups;

    private Map _cachedMap;
    private Transform _externalPlayerTransform;

    #endregion

    #region 分级更新调度

    private readonly ItemTickScheduler _tickScheduler = new();

    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int EveryFrameItemCount => _tickScheduler.EveryFrameCount;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int FastTickItemCount => _tickScheduler.FastCount;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int NormalTickItemCount => _tickScheduler.NormalCount;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int SlowTickItemCount => _tickScheduler.SlowCount;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int DormantItemCount => Mathf.Max(
        0,
        RuntimeItems.Count - EveryFrameItemCount - FastTickItemCount - NormalTickItemCount - SlowTickItemCount);

    #endregion

    #region Properties

    public string PlayerInSceneName
    {
        get
        {
            string playerName = SaveDataMgr.Instance.CurrentContrrolPlayerName;
            if (Player_DIC.TryGetValue(playerName, out Player runtimePlayer) && runtimePlayer?.Data != null)
            {
                return runtimePlayer.Data.CurrentSceneName;
            }

            // 联机玩家尚未完成 Item 实例创建时，从同步后的玩家存档提供当前场景作为保底。
            if (SaveDataMgr.Instance.SaveData?.PlayerData_Dict != null &&
                SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out Data_Player playerData) &&
                !string.IsNullOrWhiteSpace(playerData.CurrentSceneName))
            {
                return playerData.CurrentSceneName;
            }

            return SceneManager.GetActiveScene().name;
        }
    }

    public Player User_Player
    {
        get
        {
            if (Player_DIC.TryGetValue(SaveDataMgr.Instance.CurrentContrrolPlayerName, out var player))
            {
                return player;
            }

            return null;
        }
    }

    /// <summary>
    /// 当前本地玩家的 Transform。单机与联机模式均优先返回核心 Player Item。
    /// </summary>
    public Transform UserPlayerTransform
    {
        get
        {
            string playerName = SaveDataMgr.Instance.CurrentContrrolPlayerName;
            if (Player_DIC.TryGetValue(playerName, out Player runtimePlayer) && runtimePlayer != null)
                return runtimePlayer.transform;

            return _externalPlayerTransform;
        }
    }

    public void RegisterExternalPlayerTransform(Transform playerTransform)
    {
        _externalPlayerTransform = playerTransform;
    }

    public void UnregisterExternalPlayerTransform(Transform playerTransform)
    {
        if (_externalPlayerTransform == playerTransform)
            _externalPlayerTransform = null;
    }

    public Map Map
    {
        get
        {
            if (_cachedMap == null)
            {
                if (RuntimeItemsGroup.TryGetValue(GROUP_MAP_CORE, out var list) && list.Count > 0)
                {
                    _cachedMap = (Map)list[0];
                }
            }
            return _cachedMap;
        }
    }

    #endregion

    #region Unity Lifecycle

    protected override void Awake()
    {
        base.Awake();
        // 不在 Awake 中自动加载场景物品，避免破坏游戏生命周期。手动或在合适的时机调用 LoadAllRuntimeItems()
    }

    [Button("加载所有Runtime物品")]
    public void LoadAllRuntimeItems()
    {
        // 第一步：获取场景中所有的 Item（包括非激活状态）
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: true);

        foreach (Item item in allItems)
        {
            if (item == null)
            {
                continue;
            }

            var pooled = item.GetComponent<PooledItemMarker>();
            if (pooled != null && pooled.InPool)
            {
                continue;
            }

            RegisterRuntimeItem(item, item.name);
        }
    }

    public void Start()
    {
        // Debug.Log("物品加载完毕");
        GameManager.Instance.BackToHelloScene_Event_Start += CleanupNullItems;
    }

    private void OnDestroy()
    {
        CompletePerceptionBatch(false);
        DisposePerceptionJobData();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.BackToHelloScene_Event_Start -= CleanupNullItems;
        }
    }

    private void OnDisable()
    {
        CompletePerceptionBatch(false);
    }

    private void Update()
    {
        if (!IsWorldItemRuntimeActive())
        {
            // 退出世界后只结算并释放已提交的感知 Job，禁止旧场景实体继续 Tick。
            CompletePerceptionBatch(applyResults: false);
            return;
        }

        CompletePerceptionBatch();

        if (RuntimeItems.Count == 0)
        {
            return;
        }

        _tickScheduler.Update(RuntimeItems, Time.deltaTime, RefreshRuntimeItemIndexes);
    }

    private void LateUpdate()
    {
        if (!IsWorldItemRuntimeActive())
            return;

        SchedulePerceptionBatch();
    }

    /// <summary>仅在真实游戏世界中调度 Item，菜单与退出回收阶段不允许旧实体继续运行。</summary>
    private static bool IsWorldItemRuntimeActive()
    {
        GameManager gameManager = GameManager.Instance;
        return gameManager != null && gameManager.IsInGameWorld;
    }

    #endregion

    #region Public Maintenance API

    [Button("清理空引用")]
    public void CleanupNullItems()
    {
        _runtimeRegistry.CleanupNullItems();
        CleanupRuntimeAiIndex();
        RebuildSpatialIndex();
        _tickScheduler.Rebuild(RuntimeItems);

        // 分组变化后，缓存需要重新计算
        _cachedMap = null;
    }

    public void NotifyItemScheduleChanged(Item item)
    {
        _tickScheduler.NotifyChanged(item);
    }

    #endregion
}
