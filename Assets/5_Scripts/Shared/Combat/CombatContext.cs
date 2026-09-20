using System;
using Unity.Collections;
using Unity.Mathematics;

namespace FlatWorld.Combat
{
    #region 身份与时间
    /// <summary>身份后端只用于路由，不代表阵营或物种。</summary>
    public enum CombatBackend : byte { None, Entity, External, Environment }

    /// <summary>跨后端的纯值身份；Value 在实例存活期间稳定，Generation 防止池复用，World/Dimension 防止跨世界消息。</summary>
    public struct CombatIdentity : IEquatable<CombatIdentity>, IComparable<CombatIdentity>
    {
        public ulong Value; // 稳定实例编号。
        public uint Generation, World, Dimension; // 实例代际、世界纪元与维度标识。
        public CombatBackend Backend; // 命中结果路由。
        public bool IsValid => Backend != CombatBackend.None && Value != 0;

        /// <summary>完整身份相同才是同一实例。</summary>
        public bool Equals(CombatIdentity other) => Value == other.Value && Generation == other.Generation &&
            World == other.World && Dimension == other.Dimension && Backend == other.Backend;
        /// <summary>托管容器入口；Burst 使用强类型比较。</summary>
        public override bool Equals(object value) => value is CombatIdentity other && Equals(other);
        /// <summary>稳定散列所有身份字段。</summary>
        public override int GetHashCode() => (int)math.hash(new uint4((uint)Value ^ (uint)(Value >> 32), Generation, World, Dimension ^ (uint)Backend));
        /// <summary>为事件排序提供与线程执行次序无关的稳定顺序。</summary>
        public int CompareTo(CombatIdentity other)
        {
            int order = World.CompareTo(other.World);
            if (order == 0) order = Dimension.CompareTo(other.Dimension);
            if (order == 0) order = ((byte)Backend).CompareTo((byte)other.Backend);
            if (order == 0) order = Value.CompareTo(other.Value);
            return order != 0 ? order : Generation.CompareTo(other.Generation);
        }
        /// <summary>比较身份。</summary>
        public static bool operator ==(CombatIdentity left, CombatIdentity right) => left.Equals(right);
        /// <summary>比较身份。</summary>
        public static bool operator !=(CombatIdentity left, CombatIdentity right) => !left.Equals(right);
    }

    /// <summary>攻击实例、窗口与脉冲共同标识一次生效；目标身份另由 HitContext 保存。</summary>
    public struct CombatAttackKey : IEquatable<CombatAttackKey>
    {
        public CombatIdentity Source; // 实际伤害来源，武器与拥有者不合并。
        public uint Sequence, Window, Pulse; // 攻击序号、窗口和脉冲。
        /// <summary>同一次生效的完整比较。</summary>
        public bool Equals(CombatAttackKey other) => Source == other.Source && Sequence == other.Sequence && Window == other.Window && Pulse == other.Pulse;
    }

    /// <summary>冻结的模拟时钟；桥接不得把同一渲染帧内的多个模拟 Tick 改写成同一个 Time.time。</summary>
    public struct CombatClock
    {
        public ulong Tick; // 逻辑 Tick。
        public double Time; // 与当前游戏时间同域的生效时间。
        public float DeltaTime; // 本次逻辑步长。
    }
    #endregion

    #region 攻击上下文与共同规则
    /// <summary>投送能力独立于伤害材质；穿刺近战不自动获得空中命中能力。</summary>
    [Flags]
    public enum CombatDeliveryCapabilities : uint
    {
        None = 0,
        Projectile = 1,
        AirborneTargets = 2,
        AirborneOnly = 4
    }

    /// <summary>飞行身份是可组合目标能力，不按鸟类物品 ID 或伤害材质写特判。</summary>
    public interface ICombatAirborneTarget { bool IsAirborne { get; } }

    /// <summary>四类伤害按切割/穿刺/劈砍/钝击的固定顺序存储；该顺序同时用于防御和结算结果。</summary>
    public struct CombatDamageContext
    {
        public CombatDeliveryCapabilities DeliveryCapabilities;
        public CombatAttackKey Attack; // 去重与攻击者身份。
        public CombatIdentity Credit; // 击杀归因；武器可以与 Attack.Source 不同。
        public FixedString128Bytes Faction; // 来源阵营的稳定 ID。
        public float4 Damage; // 四类基础伤害。
        public float2 Origin, HitPoint; // 来源与最终命中位置。
        public CombatClock Clock; // 生效 Tick/时间。
        public byte SourceIsPlayer, IsTrueDamage; // 难度身份与是否跳过类型防御。
        public byte SlowEnabled; // 可选受击减速。
        public float SlowMultiplier, SlowDuration; // 受击减速参数。
        public float BuildingMultiplier; // 防御后的建筑倍率。
        public FixedList512Bytes<CombatOnHitBuff> OnHitBuffs; // 少量命中附加状态，超出固定契约容量在内容装配时拒绝。
    }

    /// <summary>后端无关的命中附加状态；概率由结算层在有效命中后执行。</summary>
    public struct CombatOnHitBuff
    {
        public FixedString128Bytes Id; // 当前 Buff 定义 ID。
        public float Chance; // 0..1 应用概率。
        public int Stacks; // 一次施加层数；旧生产者的零值按一层处理。
    }

    /// <summary>由 Gameplay 冻结的难度规则，两个后端使用同一个计算函数。</summary>
    public struct CombatDifficulty
    {
        public float PlayerAttack, CreatureAttack, CreatureHealth; // 保持现有攻击倍率/生物血量归一化语义。
        /// <summary>按攻击来源与接收方身份选择难度倍率。</summary>
        public float Resolve(bool sourceIsPlayer, bool targetIsPlayer) =>
            math.max(0f, sourceIsPlayer ? PlayerAttack : CreatureAttack) /
            (targetIsPlayer ? 1f : math.max(0.1f, CreatureHealth));
    }

    /// <summary>无托管伤害对象的共同数值核心；生命提交、事件与掉落由各后端的结算层负责。</summary>
    public static class CombatRules
    {
        /// <summary>先应用难度，再逐类减防御，最后应用目标受击倍率。</summary>
        public static float4 Resolve(float4 damage, float4 defense, float difficulty, float receivedMultiplier) =>
            math.max(0f, math.max(0f, damage) * math.max(0f, difficulty) - math.max(0f, defense)) * math.max(0f, receivedMultiplier);

        /// <summary>把结算分量按实际生命损失裁掉过量伤害。</summary>
        public static float4 ActualLoss(float4 resolved, float loss)
        {
            float total = math.csum(resolved);
            return total > 0f ? resolved * math.saturate(loss / total) : float4.zero;
        }

        /// <summary>带 Blood 资格的刃伤按既有规则计算概率和等级；纯钝击、零损失和死亡不出血。</summary>
        public static int BleedingTier(float4 actualLoss, bool hasBlood, bool alive, float random)
            => BleedingTier(actualLoss.x + actualLoss.y + actualLoss.z, hasBlood, alive, random);

        /// <summary>旧后端已有实际刃伤汇总时复用相同概率与等级阈值。</summary>
        public static int BleedingTier(float edged, bool hasBlood, bool alive, float random)
        {
            if (!hasBlood || !alive || edged <= 0f || random >= math.min(0.9f, edged * 0.04f)) return 0;
            return edged >= 12f ? 3 : edged >= 6f ? 2 : 1;
        }
    }
    #endregion
}
