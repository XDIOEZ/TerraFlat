using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.DroppedItems
{
    /// <summary>独立世界中的掉落运动系统；主线程冻结拓扑，Burst 只处理具有短期运动组件的实体。</summary>
    [DisableAutoCreation]
    public partial class DroppedMotionSystem : SystemBase
    {
        public WorldTopologyDomain Domain;
        public float StepSeconds;
        public NativeList<DroppedChange> Changes;
        private EntityQuery flights;
        private EntityQuery water;

        protected override void OnCreate()
        {
            Changes = new NativeList<DroppedChange>(128, Allocator.Persistent);
            flights = GetEntityQuery(ComponentType.ReadWrite<DroppedBody>(), ComponentType.ReadWrite<DroppedFlight>());
            water = GetEntityQuery(ComponentType.ReadWrite<DroppedBody>(), ComponentType.ReadWrite<DroppedWaterTransition>());
        }

        protected override void OnUpdate()
        {
            int flightCount = flights.CalculateEntityCount();
            int waterCount = water.CalculateEntityCount();
            // 每个活动实体恰好写一个变更，按查询索引直写固定范围，无全局队列池或并行扩容。
            // 常驻列表归当前 System 所有；完全静止时连空 Job 都不提交。
            Changes.ResizeUninitialized(flightCount + waterCount);
            NativeArray<DroppedChange> output = Changes.AsArray();
            if (flightCount != 0)
                Dependency = new FlightJob { Delta = StepSeconds, Domain = Domain, Changes = output }
                    .ScheduleParallel(flights, Dependency);
            if (waterCount != 0)
                Dependency = new WaterJob { Delta = StepSeconds, Offset = flightCount, Changes = output }
                    .ScheduleParallel(water, Dependency);
        }

        /// <summary>结构变化、保存、表现上传与销毁必须等待本轮写入完成。</summary>
        public void Complete() => Dependency.Complete();

        protected override void OnDestroy()
        {
            Complete();
            if (Changes.IsCreated) Changes.Dispose();
        }

        [BurstCompile]
        private partial struct FlightJob : IJobEntity
        {
            public float Delta;
            public WorldTopologyDomain Domain;
            [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<DroppedChange> Changes;

            private void Execute([EntityIndexInQuery] int index, ref DroppedBody body, ref DroppedFlight flight)
            {
                flight.Elapsed = math.min(flight.Duration, flight.Elapsed + Delta);
                float t = math.saturate(flight.Elapsed / math.max(0.0001f, flight.Duration));
                float2 ground = math.lerp(flight.Start, flight.End, t);
                float mt = 1f - t;
                float2 visual = mt * mt * flight.Start + 2f * mt * t * flight.Control + t * t * flight.End;
                body.Position = Domain.Normalize(ground);
                body.VisualHeight = t >= 1f ? 0f : visual.y - ground.y + math.sin(t * math.PI) * flight.ArcHeight;
                body.Rotation += flight.RotationSpeed * Delta;
                Changes[index] = new DroppedChange { Id = body.Id, Kind = (byte)(t >= 1f ? 1 : 0) };
            }
        }

        [BurstCompile]
        private partial struct WaterJob : IJobEntity
        {
            public float Delta;
            public int Offset;
            [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<DroppedChange> Changes;

            private void Execute([EntityIndexInQuery] int index, ref DroppedBody body, ref DroppedWaterTransition transition)
            {
                float endTime = transition.Duration + (body.WaterKind == 2 ? math.max(0f, transition.RecedeDuration) : 0f);
                transition.Elapsed = math.min(endTime, transition.Elapsed + Delta);
                float t = math.saturate(transition.Elapsed / math.max(0.0001f, transition.Duration));
                float weight = body.WaterKind == 1 ? t * t * (3f - 2f * t) : t;
                body.LiquidDepth = math.lerp(transition.StartDepth, transition.TargetDepth, weight);
                body.SubmergedProgress = body.WaterKind == 2
                    ? math.saturate((transition.Elapsed - transition.Duration) / math.max(0.0001f, transition.RecedeDuration)) : 0f;
                Changes[Offset + index] = new DroppedChange { Id = body.Id,
                    Kind = (byte)(transition.Elapsed < endTime ? 0 : body.WaterKind == 2 ? 3 : 2) };
            }
        }
    }
}
