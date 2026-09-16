using System;
using FlatWorld.Navigation;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>
    /// 实际游戏导航网格到 ECS 的开发接入：每组一个目标 Transform，全部成员共用同一缓存和避让索引。
    /// 用户显式创建时才生成真实导航 Entity，默认 48、最多 20000；不接入生命、战斗、存档或正式渲染。
    /// 不为成员创建 GameObject/导航 Agent；Gizmos 只显示少量轨迹，不能用作同屏性能证据。
    /// </summary>
    [AddComponentMenu("FlatWorld/AIECS/共享导航群体")]
    public sealed class AiecsNavigationCrowd : MonoBehaviour
    {
        #region 配置与观测
        // 数组下标就是共享目标组；支持一个玩家或两军的少量共同目的地。
        public Transform[] GoalTargets = Array.Empty<Transform>();
        public Transform[] GroupOrigins = Array.Empty<Transform>();
        [Range(1, 20000)] public int RequestedEntities = 48;
        public Vector2 GroupLayout = new Vector2(8f, 8f);
        [Range(0.01f, 0.49f)] public float BodyRadius = 0.15f;
        [Min(0.01f)] public float MoveSpeed = 3f;
        [Min(0.01f)] public float StopDistance = 0.2f;
        [Range(1, 32)] public int NeighboursPerBucket = 8;
        [Range(0f, 2f)] public float SeparationWeight = 0.8f;
        [Range(0, 512)] public int GizmoLimit = 128;
        public int CreatedEntities { get; private set; }
        public int RejectedSpawnPositions { get; private set; }
        public int SharedGoals => handles?.Length ?? 0;
        public int CachedChunks => cache?.CachedChunkCount ?? 0;
        public int CachedPortals => cache?.CachedPortalCount ?? 0;
        public long ChunkBuilds => cache?.ChunkContentBuilds ?? 0;
        public long ExitBuilds => cache?.ExitFieldBuilds ?? 0;
        public long TargetBuilds => cache?.TargetFieldBuilds ?? 0;
        public long RouteBuilds => cache?.HighLevelRouteBuilds ?? 0;
        #endregion

        #region 所有权
        private World world;
        private EntityQuery query;
        private FlowNavigationCache cache;
        private FlowGoalHandle[] handles;
        private AiecsJobSchedulerSystem jobScheduler;
        private AiecsFlowCrowdScheduler scheduler;
        private JobHandle pending;
        private uint tick;
        #endregion

        #region 显式开发入口
        /// <summary>从当前真实导航窗口建立少量共享目标和独立 ECS 群体。</summary>
        [ContextMenu("在当前游戏世界创建共享导航群体")]
        public void CreateCrowd()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("请先由用户进入临时游戏世界，再创建导航群体。");
            WorldNavigationManager manager = WorldNavigationManager.ExistingInstance;
            if (manager == null || !manager.IsNavigationReady) throw new InvalidOperationException("当前世界还没有可用的导航窗口。");
            ValidateInputs(); StopCrowd();
            try
            {
                cache = manager.GetSharedNavigation();
                handles = new FlowGoalHandle[GoalTargets.Length];
                for (int group = 0; group < handles.Length; group++)
                    handles[group] = cache.CreateGoal(PositionOf(GoalTargets[group]));
                FlowNavigationSnapshot navigation = cache.Read();
                world = new World("AIECS 共享导航群体");
                jobScheduler = world.GetOrCreateSystemManaged<AiecsJobSchedulerSystem>();
                EntityManager entities = world.EntityManager;
                EntityArchetype archetype = entities.CreateArchetype(typeof(AiecsFlowAgent));
                query = entities.CreateEntityQuery(ComponentType.ReadWrite<AiecsFlowAgent>());
                int perGroup = Mathf.CeilToInt((float)RequestedEntities / handles.Length);
                int columns = Mathf.CeilToInt(Mathf.Sqrt(perGroup * GroupLayout.x / GroupLayout.y));
                int rows = Mathf.CeilToInt((float)perGroup / columns);
                RejectedSpawnPositions = 0;
                for (int index = 0; index < RequestedEntities; index++)
                {
                    int group = index % handles.Length, local = index / handles.Length;
                    float2 center = PositionOf(GroupOrigins[group]);
                    float2 position = navigation.Domain.Normalize(center + new float2(
                        ((local % columns + 0.5f) / columns - 0.5f) * GroupLayout.x,
                        ((local / columns + 0.5f) / rows - 0.5f) * GroupLayout.y));
                    if (!navigation.CanOccupy(position, BodyRadius)) { RejectedSpawnPositions++; continue; }
                    Entity entity = entities.CreateEntity(archetype);
                    entities.SetComponentData(entity, new AiecsFlowAgent { Position = position, Goal = handles[group],
                        Radius = BodyRadius, Speed = MoveSpeed, StopDistance = StopDistance });
                }
                CreatedEntities = query.CalculateEntityCount();
                scheduler = new AiecsFlowCrowdScheduler();
                Debug.Log($"[AIECS Navigation] 真实 Entity={CreatedEntities}，共享目标={handles.Length}，阻挡/未加载出生点={RejectedSpawnPositions}；没有执行战斗或性能验收。", this);
            }
            catch { StopCrowd(); throw; }
        }

        /// <summary>先完成移动，再销毁本入口拥有的 ECS World，并归还共享目标。</summary>
        [ContextMenu("清理共享导航群体")]
        public void StopCrowd()
        {
            pending.Complete(); pending = default;
            scheduler?.Dispose(); scheduler = null;
            if (world != null && world.IsCreated) world.Dispose();
            world = null; jobScheduler = null;
            if (cache != null && handles != null)
                foreach (FlowGoalHandle handle in handles) cache.RemoveGoal(handle);
            handles = null; cache = null; CreatedEntities = 0;
        }

        /// <summary>在实例化前明确拒绝尚未支持的体型或不完整目标配置。</summary>
        private void ValidateInputs()
        {
            if (GoalTargets == null || GoalTargets.Length == 0 || GroupOrigins == null || GroupOrigins.Length != GoalTargets.Length)
                throw new InvalidOperationException("每个共享目标必须对应一个群体出生中心。");
            if (RequestedEntities < 1 || RequestedEntities > 20000 || GroupLayout.x <= 0 || GroupLayout.y <= 0 ||
                BodyRadius <= 0f || BodyRadius >= 0.5f || MoveSpeed <= 0f || StopDistance <= 0f)
                throw new InvalidOperationException("导航群体数量/布局/速度必须为正；当前点格通行配置只支持半径小于半格的圆体。");
            foreach (Transform target in GoalTargets) if (target == null) throw new InvalidOperationException("共享目标不能为空。");
            foreach (Transform origin in GroupOrigins) if (origin == null) throw new InvalidOperationException("出生中心不能为空。");
        }
        #endregion

        #region 帧循环与清理
        /// <summary>每帧仅采集少量目标 Transform，其余采样与移动通过同一批 Burst Job 完成。</summary>
        private void Update()
        {
            if (world == null) return;
            WorldNavigationManager manager = WorldNavigationManager.ExistingInstance;
            if (manager == null || !manager.IsNavigationReady || !ReferenceEquals(cache, manager.GetSharedNavigation()) || GoalTargets.Length != handles.Length)
            { StopCrowd(); return; }
            for (int group = 0; group < handles.Length; group++)
            {
                if (GoalTargets[group] == null || !cache.IsValid(handles[group])) { StopCrowd(); return; }
                cache.UpdateGoal(handles[group], PositionOf(GoalTargets[group]));
            }
            pending = scheduler.Schedule(jobScheduler, query, cache, Time.deltaTime, ++tick, NeighboursPerBucket, SeparationWeight);
        }

        /// <summary>在表现边界完成一个移动批次，编辑器只读观察不会与 Job 并发访问。</summary>
        private void LateUpdate() { pending.Complete(); }

        /// <summary>禁用或离开世界时清理本入口拥有的所有状态。</summary>
        private void OnDisable() { StopCrowd(); }

        /// <summary>只采集桥接对象的二维坐标。</summary>
        private static float2 PositionOf(Transform target) => new float2(target.position.x, target.position.y);

        /// <summary>有限数量的 Scene 视图观测点，不替代正式渲染或可见实体计数。</summary>
        private void OnDrawGizmosSelected()
        {
            if (scheduler == null) return;
            Gizmos.color = Color.cyan;
            int count = math.min(GizmoLimit, scheduler.SampleCount);
            for (int index = 0; index < count; index++)
            {
                float2 position = scheduler.GetDebugPosition(index);
                Gizmos.DrawWireSphere(new Vector3(position.x, position.y, 0f), BodyRadius);
            }
        }
        #endregion
    }
}
