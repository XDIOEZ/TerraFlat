using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using UnityEngine;

namespace FlatWorld.Gameplay.Quests
{
    /// <summary>
    /// 全局任务协调器；为每个本地玩家创建独立运行时，并把统一玩法信号路由给正确玩家。
    /// 管理器由 GameManager 显式绑定，不依赖场景 Prefab，也不会为远端网络玩家写入本地任务进度。
    /// </summary>
    public sealed class QuestManager : SingletonAutoMono<QuestManager>
    {
        #region 状态

        private readonly Dictionary<Player, PlayerQuestRuntime> runtimes = new();
        private readonly HashSet<Player> pendingPlayers = new();
        private GameManager boundGameManager;
        private bool eventsBound;

        /// <summary>本地玩家任务运行时完成初始化后发送，供只读 UI 和日志绑定。</summary>
        public event Action<Player, PlayerQuestRuntime> RuntimeReady;

        /// <summary>世界退出或管理器销毁前发送，订阅者必须立即释放旧运行时引用。</summary>
        public event Action<Player> RuntimeRemoving;

        public PlayerQuestRuntime LocalRuntime
        {
            get
            {
                foreach (KeyValuePair<Player, PlayerQuestRuntime> pair in runtimes)
                {
                    if (pair.Key != null && pair.Key.IsLocalProfile)
                        return pair.Value;
                }

                return null;
            }
        }

        #endregion

        #region 生命周期

        protected override void Awake()
        {
            base.Awake();
            DontDestroyOnLoad(gameObject);
            BindStaticEvents();
        }

        public void BindGameManager(GameManager gameManager)
        {
            if (boundGameManager == gameManager)
                return;

            if (boundGameManager != null)
                boundGameManager.Event_GameWorldExit -= HandleWorldExit;

            boundGameManager = gameManager;
            if (boundGameManager != null)
                boundGameManager.Event_GameWorldExit += HandleWorldExit;
        }

        private void Update()
        {
            TryStartPendingRuntimes();
        }

        protected override void OnDestroy()
        {
            if (boundGameManager != null)
                boundGameManager.Event_GameWorldExit -= HandleWorldExit;
            boundGameManager = null;

            if (eventsBound)
            {
                GameManager.Event_PlayerEnterWorld -= HandlePlayerEntered;
                GameplayProgressEvents.SignalPublished -= HandleSignal;
                eventsBound = false;
            }

            ClearRuntimes();
            RuntimeReady = null;
            RuntimeRemoving = null;
            base.OnDestroy();
        }

        #endregion

        #region 查询

        public bool TryGetRuntime(Player player, out PlayerQuestRuntime runtime)
        {
            if (player == null)
            {
                runtime = null;
                return false;
            }

            return runtimes.TryGetValue(player, out runtime);
        }

        #endregion

        #region 事件路由

        private void BindStaticEvents()
        {
            if (eventsBound)
                return;

            GameManager.Event_PlayerEnterWorld += HandlePlayerEntered;
            GameplayProgressEvents.SignalPublished += HandleSignal;
            eventsBound = true;
        }

        private void HandlePlayerEntered(Player player)
        {
            if (player == null || !player.IsLocalProfile || player.Data == null)
                return;

            if (runtimes.TryGetValue(player, out PlayerQuestRuntime existing))
            {
                existing.Refresh();
                return;
            }

            if (!IsQuestContentReady())
            {
                if (pendingPlayers.Add(player))
                    Debug.Log("[Quest] 任务内容仍在加载，本地玩家任务运行时已进入等待队列。");
                return;
            }

            StartRuntime(player);
        }

        /// <summary>只在本体资源、任务目录和 MOD 定义形成同一份最终快照后启动等待中的运行时。</summary>
        private void TryStartPendingRuntimes()
        {
            if (pendingPlayers.Count == 0 || !IsQuestContentReady())
                return;

            var readyPlayers = new List<Player>(pendingPlayers);
            pendingPlayers.Clear();
            foreach (Player player in readyPlayers)
            {
                if (player != null && player.IsLocalProfile && player.Data != null)
                    StartRuntime(player);
            }
        }

        /// <summary>资源热重载期间目录会短暂 Ready，仍须等待 GameRes 与 MOD 完整收尾。</summary>
        private static bool IsQuestContentReady()
        {
            if (!QuestCatalog.IsReady)
                return false;

            GameRes gameRes = GameRes.ExistingInstance;
            if (gameRes != null && !gameRes.isLoadFinish)
                return false;

            ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
            return modRuntime == null || modRuntime.IsReady;
        }

        /// <summary>为已确认内容就绪的本地玩家创建任务运行时。</summary>
        private void StartRuntime(Player player)
        {
            if (runtimes.ContainsKey(player))
                return;

            var runtime = new PlayerQuestRuntime(player);
            if (!runtime.Initialize(out string error))
            {
                Debug.LogError($"[Quest] 无法为本地玩家启动任务运行时：{error}");
                return;
            }

            runtimes.Add(player, runtime);
            NotifyRuntimeReady(player, runtime);
            Debug.Log($"[Quest] 本地玩家任务运行时已启动，任务记录数={runtime.GetSnapshots().Count}");
        }

        private void HandleSignal(GameplayProgressSignal signal)
        {
            if (signal.Actor != null && runtimes.TryGetValue(signal.Actor, out PlayerQuestRuntime runtime))
                runtime.HandleSignal(signal);
        }

        private void HandleWorldExit()
        {
            ClearRuntimes();
        }

        private void ClearRuntimes()
        {
            pendingPlayers.Clear();

            foreach (KeyValuePair<Player, PlayerQuestRuntime> pair in runtimes)
            {
                NotifyRuntimeRemoving(pair.Key);
                pair.Value?.Dispose();
            }

            runtimes.Clear();
        }

        /// <summary>逐订阅者隔离 UI 绑定异常，避免破坏任务运行时创建。</summary>
        private void NotifyRuntimeReady(Player player, PlayerQuestRuntime runtime)
        {
            if (RuntimeReady == null)
                return;

            foreach (Delegate callback in RuntimeReady.GetInvocationList())
            {
                try
                {
                    ((Action<Player, PlayerQuestRuntime>)callback)(player, runtime);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        /// <summary>逐订阅者通知运行时即将失效。</summary>
        private void NotifyRuntimeRemoving(Player player)
        {
            if (RuntimeRemoving == null)
                return;

            foreach (Delegate callback in RuntimeRemoving.GetInvocationList())
            {
                try
                {
                    ((Action<Player>)callback)(player);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        #endregion
    }
}
