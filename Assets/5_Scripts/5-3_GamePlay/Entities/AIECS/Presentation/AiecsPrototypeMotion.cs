using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    /// <summary>
    /// P1 独立渲染实体的数据；Slot 只在本次原型 World 内有效，不能用作存档 ID。
    /// 每个实体具有独立位置和动画时钟，移动是可复现的穿行轨迹，不冒充 AI、导航或战斗。
    /// </summary>
    public struct AiecsPrototypeActor : IComponentData
    {
        // 原型生命周期内唯一的输出槽位。
        public int Slot;
        // 共享目录的物种和动作索引。
        public int Definition;
        public int Action;
        // 原点、当前位置和动画时钟。
        public float2 Origin;
        public float2 Position;
        public float Elapsed;
        public float Phase;
        public float Speed;
        // 平滑的视觉入水混合，不是水中生存状态。
        public float WaterBlend;
    }

    /// <summary>批量更新 P1 的穿行轨迹并输出一致快照；全批次只在渲染边界同步一次。</summary>
    [BurstCompile]
    public partial struct AiecsPrototypeMotion : IJobEntity
    {
        // 主线程冻结的本帧参数。
        public float DeltaTime;
        public float Travel;
        public float WaterBoundaryY;
        public float TransitionSeconds;
        // Slot 在创建时唯一分配，每个实体只写自己的位置。
        [NativeDisableParallelForRestriction] public NativeArray<AiecsPrototypeActor> Snapshot;

        /// <summary>使用个体相位移动、推进动画，并平滑跨水边的视觉参数。</summary>
        private void Execute(ref AiecsPrototypeActor actor)
        {
            actor.Elapsed += DeltaTime * actor.Speed;
            float phase = actor.Elapsed * 0.6f + actor.Phase;
            actor.Position = actor.Origin + new float2(math.sin(phase) * Travel, math.cos(phase) * Travel);
            float target = actor.Position.y < WaterBoundaryY ? 1f : 0f;
            actor.WaterBlend = math.lerp(actor.WaterBlend, target,
                1f - math.exp(-DeltaTime / math.max(0.0001f, TransitionSeconds)));
            Snapshot[actor.Slot] = actor;
        }
    }
}
