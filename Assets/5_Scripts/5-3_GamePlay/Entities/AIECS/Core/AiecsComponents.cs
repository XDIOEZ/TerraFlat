using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Navigation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    #region 身份、体型与生命
    /// <summary>正式运行身份与共享定义索引；Group 是战略目标组，Faction 是关系表索引，二者不是同一概念。</summary>
    public struct AiecsIdentity : IComponentData
    {
        public CombatIdentity Key; // 后端无关身份。
        public int Definition, Group, Faction; // 定义、目标组与阵营索引。
        public byte External, Player, HasBlood, Targetable; // 外部代理、玩家难度身份、Blood 与可攻击资格。
    }

    /// <summary>独立的感知与受击形状；位置来自 FlowAgent，形状只保存局部数据。</summary>
    public struct AiecsBody : IComponentData
    {
        public PerceptionShape2D Perception, Hit; // 本地圆/AABB。
        public float Radius; // 移动扫掠圆半径，小于半格。
        public float2 Facing; // 逻辑朝向，动画不改变它。
    }

    /// <summary>生命权威只由结算层写入；外部代理的值由 Bridge 单向刷新。</summary>
    public struct AiecsVital : IComponentData
    {
        public float Hp, MaxHp, DamageInterval, ReceivedMultiplier; // 生命、受伤间隔与受击倍率。
        public double LastDamageTime; // 同一游戏时间域的最近受伤时间。
        public CombatIdentity LastAttacker, LastCredit; // 最近来源与击杀归因。
        public byte Dead, DeathPublished, LootPublished; // 先锁存再发布的一次性状态。
        public double DeathTime; // 死亡时间。
    }

    /// <summary>四类防御不包含临时 Buff 倍率。</summary>
    public struct AiecsDefense : IComponentData { public float4 Values; }

    /// <summary>一个身体部位，权重等于可见面积乘受伤概率；没有物种分支。</summary>
    public struct AiecsBodyPart
    {
        public int Id; // 与当前内容的部位枚举数值一致。
        public float Hp, MaxHp, Weight; // 生命及命中权重。
    }

    /// <summary>固定容量的部位运行态，默认八部位；避免每次命中分配托管列表。</summary>
    public struct AiecsAnatomy : IComponentData
    {
        public FixedList512Bytes<AiecsBodyPart> Parts; // 仅保存实例生命。
        public float TwoPartChance; // 第二个不同部位的命中概率。
    }
    #endregion

    #region 感知、决策与行为意图
    /// <summary>内置通用行为；扩展系统可使用大于等于 1024 的行为 ID，不添加物种专属系统。</summary>
    public enum AiecsBehavior : int { Idle, Wander, Chase, Flee, Attack, Custom = 1024 }

    /// <summary>可组合条件位；核心 Brain 只匹配规则并提交意图，不负责路径或伤害。</summary>
    [System.Flags]
    public enum AiecsDecisionFacts : uint
    {
        None = 0, HasTarget = 1, InAttackRange = 2, LowHealth = 4,
        HasThreat = 8, RestFinished = 16, AttackLocked = 32
    }

    /// <summary>一条共享优先级规则；新增能力可通过规则或前置提议系统接入。</summary>
    public struct AiecsDecisionRule
    {
        public AiecsDecisionFacts Require, Exclude; // 必需与禁止事实。
        public int Behavior, Priority; // 行为 ID 与优先级。
    }

    /// <summary>稳定锁定、必要记忆与错峰时钟，不创建逐实体托管状态机。</summary>
    public struct AiecsBrain : IComponentData
    {
        public Entity Target; // 当前 World 内的目标引用，版本由 ECS 校验。
        public CombatIdentity TargetKey; // 同时验证外部代理代际。
        public CombatIdentity RejectedTarget; // 暂时不可达的目标，避免释放后同批重新锁定。
        public double RetryTargetAfter; // 到期后允许重试，墙体改变不会永久拉黑目标。
        public float2 ThreatPosition; // 最后一次有效威胁位置。
        public float Threat; // 当前威胁评分。
        public double NextPerception, NextDecision, MemoryUntil, BehaviorUntil, EnteredAt; // 错峰与行为计时。
        public int Behavior; // 已提交的通用行为 ID。
        public uint RandomState; // 独立可重现随机状态。
    }

    /// <summary>能力系统的一个优先级提议；由 DecisionSystem 消费并复位，扩展不必重写 Brain。</summary>
    public struct AiecsBehaviorProposal : IComponentData
    {
        public int Behavior, Priority; // 本 tick 的扩展提议。
        public float2 Destination; // 可选扩展行为位置。
        public byte HasDestination; // 是否携带位置。
    }

    /// <summary>决策到执行的单向契约；导航与攻击分别消费位置/目标与攻击请求。</summary>
    public struct AiecsBehaviorIntent : IComponentData
    {
        public int Behavior; // 想做什么。
        public Entity Target; // 想影响谁。
        public CombatIdentity TargetKey; // 目标版本。
        public float2 Destination; // 局部意图位置。
        public byte HasDestination; // 扩展行为指定位置。
    }

    /// <summary>游荡/逃跑的局部移动记忆；这里只存一个局部目标，不保存逐 AI 路径或场。</summary>
    public struct AiecsLocalMotion : IComponentData
    {
        public float2 Destination; // 当前局部目标。
        public double RefreshAt; // 下一次允许选择目标的时刻。
        public int Behavior; // 产生该目标的行为。
        public byte Valid; // 当前目标有效。
    }
    #endregion

    #region 攻击、Buff 与事件
    /// <summary>攻击权威阶段，Active 的有效 Tick 才生成命中。</summary>
    public enum AiecsAttackPhase : byte { Ready, Windup, Active, Recovery, Cooldown }

    /// <summary>一个单位的攻击时序；开始后锁定本次目标与朝向，不随 Brain 换敌而改写正在挥出的攻击。</summary>
    public struct AiecsAttackState : IComponentData
    {
        public AiecsAttackPhase Phase; // 当前阶段。
        public Entity Target; // 起手目标。
        public CombatIdentity TargetKey; // 起手目标身份。
        public float2 Facing; // 起手朝向。
        public double PhaseEnds, NextAttack; // 阶段结束与冷却终点。
        public uint Sequence, Pulse; // 去重身份。
    }

    /// <summary>战斗状态容器；慢速状态按到期时间恢复，不反复乘除基础速度。</summary>
    public struct AiecsStatus : IComponentData
    {
        public float MoveMultiplier; // 当前减速倍率。
        public double SlowUntil; // 减速终点。
        public float Water; // 出血 Buff 的失水运行态，正式生存模块后续消费同一数据。
        public int BleedingTier; // 当前互斥出血等级。
    }

    /// <summary>可组合 Buff 实例；效果由共享定义编译，运行态不保存托管定义。</summary>
    [InternalBufferCapacity(4)]
    public struct AiecsBuff : IBufferElementData
    {
        public int Definition; // 共享 Buff 索引。
        public int Stacks; // 同一定义的层数，旧零值按一层解释。
        public double Expires, NextTick; // 续期和周期时钟。
        public CombatIdentity Credit; // 持续伤害归因。
    }

    /// <summary>并行攻击产生的纯值命中事件，后续按目标分桶并串行结算同一目标。</summary>
    public struct AiecsHitEvent
    {
        public Entity Target; // 当前 ECS 目标引用。
        public CombatIdentity TargetKey; // 双重身份验证。
        public CombatDamageContext Context; // 无 managed CombatDamage。
    }

    /// <summary>本 Tick 周期效果的局部事件缓冲；每个 Buff 保留自己的归因，清空后复用容量。</summary>
    [InternalBufferCapacity(0)]
    public struct AiecsPeriodicHit : IBufferElementData { public AiecsHitEvent Hit; }

    /// <summary>命中后的只读结果；负值表示拒绝，零是有效零伤害，死亡与实际类型损失均属于同一笔结算。</summary>
    public struct AiecsDamageResult
    {
        public CombatIdentity Target; // 被命中的实例。
        public CombatDamageContext Context; // 本次生效来源与 Tick。
        public float Loss, Hp; // 实际生命损失与提交后生命。
        public float4 TypedLoss; // 实际四类损失，已裁掉过量伤害。
        public byte Fatal; // 本次首次死亡。
    }

    /// <summary>死亡的权威一次性输出；Bridge 只消费结果创建掉落，不重新判定谁死亡。</summary>
    public struct AiecsDeathEvent
    {
        public Entity Entity; // 尸体回收直接定位，不按死亡事件反查全部单位。
        public CombatIdentity Actor, Killer; // 死者与击杀归因。
        public float2 Position; // 死亡位置。
        public int Definition; // 当前静态掉落定义索引。
        public CombatClock Clock; // 死亡 Tick。
    }
    #endregion

    #region 共享定义与观测
    /// <summary>Actor 的纯数据编译结果；定义按来源配置组合能力，不按物种代码分派。</summary>
    public struct AiecsDefinition
    {
        public FixedString128Bytes Id, Faction, LootTable; // 当前内容的稳定 ID。
        public float SenseRange, ChaseRange, PerceptionPeriod, DecisionPeriod; // 感知与决策节奏。
        public float ChaseRetryDelay; // 不可达目标的重试间隔。
        public float FleeHealthRatio, FleeSeconds, MemorySeconds; // 生存规则与记忆。
        public float WanderRadius, WanderSeconds, IdleSeconds; // 游荡规则。
        public float MoveSpeed, AttackStartRange, HitRange, AttackArcCos; // 移动与起手/实际命中条件。
        public float Windup, Active, Recovery, Cooldown; // 攻击时间轴。
        public float4 Damage; // 四类基础攻击。
        public float SlowMultiplier, SlowDuration; // 命中减速。
        public FixedList512Bytes<CombatOnHitBuff> OnHitBuffs; // 与普通攻击组合的状态能力。
        public int CandidateBudget; // 一次感知最多检查的候选数，密集格不退化到平方工作量。
        public byte RequireLos; // 特殊能力可明确关闭墙体感知遮挡。
        public FixedList512Bytes<AiecsDecisionRule> Rules; // 可组合决策规则。
    }

    /// <summary>Buff 的已编译周期效果；受支持操作数直接批量执行。</summary>
    public struct AiecsBuffDefinition
    {
        public FixedString128Bytes Id; // 稳定 Buff ID。
        public float Duration, Interval, TrueDamage, WaterDelta; // 生命周期与本轮支持的周期效果。
        public float TrueDamagePerStack; // 只放明确声明按层数缩放的伤害，避免放大其它效果。
        public int MaxStacks; // 当前内容目录的层数上限。
        public int BleedingTier; // 零表示普通状态，正值参与互斥等级。
        public byte StackMode; // 与当前 Buff 配置枚举一致。
    }

    /// <summary>单个实体的实际工作计数，统计 Job 汇总；不从输入单位数推算。</summary>
    public struct AiecsWorkCounters : IComponentData
    {
        public int PerceptionRequests, Candidates, Los, Attacks, Hits, DamageResults; // 本 tick 工作数。
    }

    /// <summary>一次性生成的表现快照；表现不会反写生命或伤害。</summary>
    public struct AiecsDisplayRecord
    {
        public CombatIdentity Key; // 真实身份。
        public float2 Position, Facing; // 当前位置与逻辑朝向。
        public float Hp, MaxHp, ActionElapsed; // 生命与动作时钟。
        public float WaterDepth, WaterBlend; // 有效水深与入水表现混合。
        public int Definition, Group, Behavior; // 目录索引、分组颜色与行为。
        public AiecsAttackPhase AttackPhase; // 映射表现动作。
        public byte Dead, External, HasTarget; // 可见对象分类与实际锁定状态。
    }
    #endregion
}
