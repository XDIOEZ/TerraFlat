using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>可手测的真实垂直切片模式；仅影响这个开发入口创建的 ECS 单位。</summary>
    public enum AiecsPlaygroundMode { PlayerDuel, Armies, Wander, Flee }

    /// <summary>
    /// AIECS 实战开发入口：从正式启动场景进入临时世界后自动创建少量真实单位，可用按钮切换场景。
    /// 默认单挑 3 只、两军各 100 只（总计 200）；只有本入口和有限渲染批次是 GameObject，AI 本身完全是 Entity。
    /// </summary>
    [AddComponentMenu("FlatWorld/AIECS/实战开发入口")]
    public sealed class AiecsPlayground : MonoBehaviour
    {
        #region 配置与状态
        public AiecsAnimationCatalog Catalog; // 当前导出的共享动画目录。
        public bool LoadGameStartOnPlay = true; // 专用入口场景加载正式启动流程。
        public string TeamAActor = "Wolf", TeamBActor = "WildBoar"; // 当前 Actor 目录 ID，可在 Inspector 改配置。
        [Range(1, 10000)] public int UnitsPerArmy = 100; // 两军模式每次增援每军数量；默认总量 200，每次点击再增加 200。
        public AiecsPlaygroundMode InitialMode = AiecsPlaygroundMode.PlayerDuel;
        public bool ShowHealth = true; // 开发血条有显示上限。
        public string Status { get; private set; } = "等待正式游戏世界；请新建临时世界。";
        private static AiecsPlayground active;
        private Player player;
        private AiecsGameplayBridge bridge;
        private AiecsWorldRenderer display;
        private AiecsPlaygroundMode mode;
        private bool startRequested;
        private Camera targetCamera;
        private double simulationTime;
        private GameManager manager;
        private int spawned;
        private int armyReinforcementWave;
        private float2 armyBattleCenter;
        #endregion

        #region 生命周期
        /// <summary>入口跨启动/游戏场景存活，但同一个 Play 只允许一个实例写入模拟。</summary>
        private void Awake()
        {
            if (active != null && active != this) { Destroy(gameObject); return; }
            active = this; DontDestroyOnLoad(gameObject);
            mode = InitialMode; GameManager.Event_PlayerEnterWorld += OnPlayerEntered;
        }

        /// <summary>复用游戏本来的新建世界流程，不替用户打开或覆盖存档。</summary>
        private void Start()
        {
            if (active != this) return;
            if (LoadGameStartOnPlay && SceneManager.GetActiveScene().name != "GameStartScene")
                SceneManager.LoadScene("GameStartScene");
            else if (ItemMgr.Instance != null && ItemMgr.Instance.User_Player != null)
                OnPlayerEntered(ItemMgr.Instance.User_Player);
        }

        /// <summary>正式玩家入场后等待真实导航窗口，不能在数据尚未准备好时创建替代网格。</summary>
        private void OnPlayerEntered(Player value)
        {
            player = value; startRequested = true;
            if (manager != null) manager.Event_GameWorldExit -= OnWorldExit;
            manager = GameManager.Instance;
            if (manager != null) manager.Event_GameWorldExit += OnWorldExit;
        }

        /// <summary>固定 30Hz 模拟保留实际 Tick/Time，积压顺延而不把多 Tick 都标成渲染帧时间。</summary>
        private void Update()
        {
            if (active != this) return;
            if (player == null) return;
            var navigation = WorldNavigationManager.ExistingInstance;
            if (startRequested && navigation != null && navigation.IsNavigationReady)
            { startRequested = false; StartScenario(mode); }
            if (bridge == null) return;
            if (!bridge.IsCurrentWorld(player)) { StopScenario(); startRequested = true; return; }
            const float step = 1f / 30f;
            int budget = 4;
            try
            {
                while (simulationTime + step <= Time.timeAsDouble && budget-- > 0)
                { simulationTime += step; bridge.EnsurePlayer(player); bridge.Step(step, simulationTime); }
            }
            catch (Exception exception)
            { SetStatus("模拟已停止：" + exception.Message); Debug.LogException(exception, this); StopScenario(); }
        }

        /// <summary>相机与显示只消费提交后的状态，单挑和两军使用同一套正式系统。</summary>
        private void LateUpdate()
        {
            if (bridge == null) return;
            if (targetCamera == null || !targetCamera.isActiveAndEnabled) targetCamera = Camera.main;
            display.Draw(bridge.Simulation, targetCamera, bridge.Navigation.Read().Domain);
        }

        /// <summary>退出世界先停止开发模拟，避免旧世界桥接命中进入新存档。</summary>
        private void OnWorldExit()
        {
            StopScenario(); player = null; startRequested = false;
            SetStatus("等待临时世界。");
        }

        /// <summary>释放静态订阅、模拟和批次，兼容关闭 Domain Reload 的编辑器。</summary>
        private void OnDestroy()
        {
            if (active != this) return;
            GameManager.Event_PlayerEnterWorld -= OnPlayerEntered;
            if (manager != null) manager.Event_GameWorldExit -= OnWorldExit;
            StopScenario(); active = null;
        }
        #endregion

        #region 场景配置
        /// <summary>按显式模式重新创建开发单位，保留玩家的真实生命和当前武器。</summary>
        public void StartScenario(AiecsPlaygroundMode requested)
        {
            if (player == null || WorldNavigationManager.ExistingInstance == null) return;
            StopScenario(); mode = requested; spawned = 0; armyReinforcementWave = 0;
            try
            {
                string[] ids = { TeamAActor, TeamBActor };
                var cache = WorldNavigationManager.ExistingInstance.GetSharedNavigation();
                float sense = requested == AiecsPlaygroundMode.Wander ? 4f : 18f;
                bridge = new AiecsGameplayBridge(player, cache, ids, new[] { "aiecs.demo.a", "aiecs.demo.b" }, sense);
                bridge.PlayerParticipates = requested != AiecsPlaygroundMode.Armies;
                display = new AiecsWorldRenderer(Catalog, ids, player.gameObject.scene);
                simulationTime = Time.timeAsDouble;
                float2 center = (Vector2)player.transform.position;
                if (requested == AiecsPlaygroundMode.Armies)
                {
                    armyBattleCenter = center;
                    SpawnArmyReinforcement();
                }
                else if (requested == AiecsPlaygroundMode.Wander) SpawnGroup(0, 6, center + new float2(9, 0), 1f);
                else if (requested == AiecsPlaygroundMode.Flee) SpawnGroup(0, 3, center + new float2(4, 0), 0.15f);
                else SpawnGroup(0, 3, center + new float2(5, 0), 1f);
                // 零时长首批只建立实际状态/索引，普通攻击仍需完整前摇。
                bridge.Step(0f, simulationTime);
                SetStatus("真实 ECS 单位 " + spawned + "；使用当前武器攻击，蓝/红血条显示实际生命。");
            }
            catch (Exception exception)
            { SetStatus("创建失败：" + exception.Message); Debug.LogException(exception, this); StopScenario(); }
        }

        /// <summary>两军按钮：首次切入时创建 200 只，已在两军模式时每次继续追加 200 只。</summary>
        public void StartOrReinforceArmies()
        {
            if (bridge == null || mode != AiecsPlaygroundMode.Armies)
            {
                StartScenario(AiecsPlaygroundMode.Armies);
                return;
            }

            int before = spawned;
            SpawnArmyReinforcement();
            bridge.Step(0f, simulationTime);
            int added = spawned - before;
            SetStatus($"两军交战：真实 ECS 单位 {spawned}；本次增援 {added} / 目标 {math.clamp(UnitsPerArmy, 1, 10000) * 2}。继续点击可再增援。");
        }

        /// <summary>按波次在玩家附近两侧追加单位，避免每次点击都重置已有战斗。</summary>
        private void SpawnArmyReinforcement()
        {
            int perArmy = math.clamp(UnitsPerArmy, 1, 10000);
            float waveOffset = armyReinforcementWave * 3.5f;
            SpawnGroup(0, perArmy, armyBattleCenter + new float2(-5f - waveOffset, 2f), 1f);
            SpawnGroup(1, perArmy, armyBattleCenter + new float2(5f + waveOffset, 2f), 1f);
            armyReinforcementWave++;
        }

        /// <summary>只选择可站立的已加载格；阻挡候选被跳过，不能为了演示强行改地形。</summary>
        private void SpawnGroup(int group, int count, float2 origin, float hpRatio)
        {
            int columns = math.max(1, (int)math.ceil(math.sqrt(count)));
            for (int i = 0; i < count; i++)
            {
                float2 position = origin + new float2(i % columns - (columns - 1) * 0.5f, i / columns - (columns - 1) * 0.5f) * 0.75f;
                if (bridge.Spawn(group, position, hpRatio)) { spawned++; continue; }
                for (int ring = 1; ring <= 3; ring++)
                {
                    bool placed = false;
                    for (int side = 0; side < 8; side++)
                    {
                        float angle = side * math.PI * 0.25f;
                        if (!bridge.Spawn(group, position + new float2(math.cos(angle), math.sin(angle)) * ring, hpRatio)) continue;
                        spawned++; placed = true; break;
                    }
                    if (placed) break;
                }
            }
        }

        /// <summary>主动结束当前开发群体并归还所有共享资源。</summary>
        private void StopScenario()
        {
            display?.Dispose(); display = null; bridge?.Dispose(); bridge = null;
        }
        #endregion

        #region GM 调试接口
        /// <summary>当前唯一的 AIECS 实战开发入口，供 GM 分页读取，不再由 Prefab 自建独立 UI。</summary>
        public static AiecsPlayground Active => active;

        /// <summary>玩家与共享导航均已就绪时才允许切换测试场景。</summary>
        public bool IsReady => player != null &&
                               WorldNavigationManager.ExistingInstance != null &&
                               WorldNavigationManager.ExistingInstance.IsNavigationReady;

        public bool HasActiveScenario => bridge != null; // 当前是否存在开发模拟。
        public bool PlayerParticipates => bridge != null && bridge.PlayerParticipates; // 玩家是否参与当前感知/战斗。
        public int ReinforcementUnitCount => math.clamp(UnitsPerArmy, 1, 10000) * 2; // 两军按钮单次增援总量。

        /// <summary>清空当前开发群体；不会自动重新创建默认模式。</summary>
        public void ClearScenario()
        {
            StopScenario();
            startRequested = false;
            SetStatus("已清理 AIECS 开发单位。可通过按钮重新启动场景。");
        }

        /// <summary>GM 开关只修改当前开发 Bridge，不改变正式生态配置。</summary>
        public void SetPlayerParticipates(bool value)
        {
            if (bridge != null)
                bridge.PlayerParticipates = value;
        }

        /// <summary>供 GM 页低频读取的当前行为统计。</summary>
        public string StatisticsSummary
        {
            get
            {
                if (bridge == null)
                    return "存活 0 / 目标 0 / 游荡 0 / 追击 0 / 逃跑 0 / 攻击 0";

                var stats = bridge.Simulation.Statistics;
                return $"存活 {stats[(int)AiecsStatistic.Alive]} / 目标 {stats[(int)AiecsStatistic.Targets]} / 游荡 {stats[(int)AiecsStatistic.Wander]} / 追击 {stats[(int)AiecsStatistic.Chase]} / 逃跑 {stats[(int)AiecsStatistic.Flee]} / 攻击 {stats[(int)AiecsStatistic.Attack]}";
            }
        }

        /// <summary>供 GM 页低频读取的当前 Tick 统计。</summary>
        public string TickSummary
        {
            get
            {
                if (bridge == null)
                    return "本 Tick：感知请求 0，候选 0，LOS 0，热点格 0";

                var stats = bridge.Simulation.Statistics;
                return $"本 Tick：感知请求 {stats[(int)AiecsStatistic.PerceptionRequests]}，候选 {stats[(int)AiecsStatistic.Candidates]}，LOS {stats[(int)AiecsStatistic.Los]}，热点格 {stats[(int)AiecsStatistic.HotBucket]}";
            }
        }

        /// <summary>供 GM 页低频读取的累计战斗统计。</summary>
        public string CumulativeSummary
        {
            get
            {
                if (bridge == null)
                    return "累计：玩家受击 0（-0.0 HP），武器→ECS 命中 0，死亡 0，掉落 0";

                return $"累计：玩家受击 {bridge.PlayerHits}（-{bridge.PlayerDamage:0.0} HP），武器→ECS 命中 {bridge.PlayerToEcsHits}，死亡 {bridge.Deaths}，掉落 {bridge.Drops}";
            }
        }

        /// <summary>供 GM 页低频读取的共享导航统计。</summary>
        public string NavigationSummary
        {
            get
            {
                if (bridge == null)
                    return "共享目标 0 / Chunk 0 / 出口图 0 / 目标图 0 / 区块路线 0";

                return $"共享目标 {bridge.SharedGoalCount} / Chunk {bridge.Navigation.CachedChunkCount} / 出口图 {bridge.Navigation.ExitFieldBuilds} / 目标图 {bridge.Navigation.TargetFieldBuilds} / 区块路线 {bridge.Navigation.HighLevelRouteBuilds}";
            }
        }
        #endregion

        #region 开发观察
        /// <summary>状态只由模拟入口维护，具体展示统一交给 GM 分页。</summary>
        private void SetStatus(string value) => Status = value ?? string.Empty;

        /// <summary>血条仍用无节点 IMGUI 批量绘制，避免为了 64 个临时血条创建逐实体 uGUI。</summary>
        private void OnGUI()
        {
            if (active != this || !ShowHealth || bridge == null || display == null) return;
            display.DrawHealth(bridge.Simulation, targetCamera, bridge.Navigation.Read().Domain);
        }
        #endregion
    }
}
