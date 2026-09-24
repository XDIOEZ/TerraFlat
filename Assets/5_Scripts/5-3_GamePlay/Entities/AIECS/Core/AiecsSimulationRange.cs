using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    #region 距离档数据

    /// <summary>正式生态的三个世界距离平方及玩家位置；开发模拟可关闭距离筛选。</summary>
    public struct AiecsSimulationRange
    {
        public float2 PlayerPosition;
        public float NearSquared, MiddleSquared, FarSquared;
        public byte Enabled;
    }

    /// <summary>可启停的每实体模拟脉冲；禁用时退出玩法查询，表现查询仍保留该实体。</summary>
    public struct AiecsSimulationPulse : IComponentData, IEnableableComponent
    {
        public double LastTickTime, PausedAt;
        public float DeltaTime;
        public byte Tier;
    }

    #endregion

    #region 模拟脉冲选择

    /// <summary>
    /// 在 60 Hz 基础时钟上按距离选择 60、30、10 或 0 Hz。每个实体用身份错峰，
    /// 离开三级范围时冻结计时，重新进入时只恢复一次，不追算暂停期间的玩法 Tick。
    /// </summary>
    [BurstCompile]
    internal partial struct AiecsSelectSimulationPulseJob : IJobEntity
    {
        public AiecsSimulationRange Range;
        public WorldTopologyDomain Domain;
        public double Time;
        public float BaseDeltaTime;
        public ulong Tick;

        private void Execute(Entity entity, in AiecsIdentity identity, in AiecsFlowAgent actor,
            ref AiecsSimulationPulse pulse, EnabledRefRW<AiecsSimulationPulse> enabled,
            ref AiecsBrain brain, ref AiecsAttackState attack, ref AiecsLocalMotion local,
            ref AiecsStatus status, ref AiecsVital vital, ref AiecsWorkCounters counters,
            ref DynamicBuffer<AiecsBuff> buffs)
        {
            counters = default;
            byte tier = ResolveTier(identity, actor.Position);
            if (tier == 0)
            {
                if (pulse.Tier != 0 || pulse.PausedAt <= 0d) pulse.PausedAt = Time;
                pulse.Tier = 0;
                pulse.LastTickTime = Time;
                pulse.DeltaTime = 0f;
                enabled.ValueRW = false;
                return;
            }

            if (pulse.Tier == 0 && pulse.PausedAt > 0d)
                ShiftTimers(Time - pulse.PausedAt, ref brain, ref attack, ref local,
                    ref status, ref vital, ref buffs);

            int divisor = tier == 1 ? 1 : tier == 2 ? 2 : 6;
            bool due = tier != pulse.Tier ||
                (Tick + (uint)entity.Index) % (ulong)divisor == 0UL;
            pulse.Tier = tier;
            pulse.PausedAt = 0d;
            if (!due)
            {
                enabled.ValueRW = false;
                return;
            }

            double elapsed = Time - pulse.LastTickTime;
            pulse.DeltaTime = math.min(0.1f, (float)math.max(BaseDeltaTime, elapsed));
            pulse.LastTickTime = Time;
            enabled.ValueRW = true;
        }

        private byte ResolveTier(AiecsIdentity identity, float2 position)
        {
            if (Range.Enabled == 0 || identity.External != 0)
                return 1;

            float distance = Domain.SqrDistance(position, Range.PlayerPosition);
            if (distance <= Range.NearSquared) return 1;
            if (distance <= Range.MiddleSquared) return 2;
            if (distance <= Range.FarSquared) return 3;
            return 0;
        }

        /// <summary>只在休眠结束时平移玩法时间戳，Buff 与攻击不在苏醒帧集中补算。</summary>
        private static void ShiftTimers(double paused, ref AiecsBrain brain,
            ref AiecsAttackState attack, ref AiecsLocalMotion local,
            ref AiecsStatus status, ref AiecsVital vital, ref DynamicBuffer<AiecsBuff> buffs)
        {
            if (paused <= 0d) return;
            if (brain.RetryTargetAfter > 0d) brain.RetryTargetAfter += paused;
            if (brain.NextPerception > 0d) brain.NextPerception += paused;
            if (brain.NextDecision > 0d) brain.NextDecision += paused;
            if (brain.MemoryUntil > 0d) brain.MemoryUntil += paused;
            if (brain.BehaviorUntil > 0d) brain.BehaviorUntil += paused;
            if (brain.EnteredAt > 0d) brain.EnteredAt += paused;
            if (attack.PhaseEnds > 0d) attack.PhaseEnds += paused;
            if (attack.NextAttack > 0d) attack.NextAttack += paused;
            if (local.RefreshAt > 0d) local.RefreshAt += paused;
            if (status.SlowUntil > 0d) status.SlowUntil += paused;
            if (vital.LastDamageTime > 0d) vital.LastDamageTime += paused;
            if (vital.DeathTime > 0d) vital.DeathTime += paused;
            for (int i = 0; i < buffs.Length; i++)
            {
                AiecsBuff buff = buffs[i];
                buff.Expires += paused;
                buff.NextTick += paused;
                buffs[i] = buff;
            }
        }
    }

    #endregion
}
