using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    #region 模块标识

    /// <summary>Actor JSON 组合共享能力；新增物种无需增加物种脚本。</summary>
    [Flags]
    public enum AiecsCapability : uint
    {
        None = 0,
        Movement = 1u << 0,
        Perception = 1u << 1,
        Nutrition = 1u << 2,
        Feeding = 1u << 3,
        Sleep = 1u << 4,
        Flee = 1u << 5,
        Reproduction = 1u << 6,
        EggLaying = 1u << 7,
        Flight = 1u << 8,
        Combat = 1u << 9,
        Charge = 1u << 10,
        Chase = 1u << 11,
        Pack = 1u << 12,
        Predator = 1u << 13,
        HiveMember = 1u << 14,
        Darkness = 1u << 15
    }

    #endregion

    #region 可选运行态

    public enum AiecsFoodResource : byte { Grass, Nectar }
    public enum AiecsFoodTarget : byte { Grass, Crop, Flower }

    public struct AiecsNutrition : IComponentData
    {
        public float Current, Maximum, DrainPerSecond, FeedBelow, FeedGain;
        public double NextSearchTime;
        public int2 FoodCell;
        public float2 FoodPosition;
        public int FoodGuid;
        public AiecsFoodResource Resource;
        public AiecsFoodTarget TargetKind;
        public byte HasFoodTarget;
        public byte CanFeed;
    }

    public struct AiecsSleep : IComponentData
    {
        public float DayStartRatio, DayEndRatio, HealthBelow;
        public byte Sleeping;
    }

    public struct AiecsFlight : IComponentData
    {
        public float Height, TargetHeight, CruiseHeight, Speed, GroundSpeed;
        public float Stamina, StaminaMaximum, DrainPerSecond, RecoveryPerSecond;
        public float TakeoffRecoveryRatio, TakeoffChancePerSecond;
        public float BaseWaterPushSpeed;
        public byte Airborne;
        public byte Recovering; // 耐力耗尽后进入恢复态，达到阈值后才允许随机选择起飞。
    }

    public struct AiecsReproduction : IComponentData
    {
        public float CooldownDays;
        public double NextBirthTime;
        public byte EggLaying;
        public FixedString64Bytes EggItemId;
    }

    public struct AiecsHiveMember : IComponentData
    {
        public int HomeGuid;
        public float2 Home;
        public float PatrolRadius;
        public float Alert;
        public float DefenseRemaining;
        public byte ReturnHome;
        public float2 DefensePosition;
        public byte HasDefenseTarget;
        public byte Orphaned;
        public byte CarryingHoney;
    }

    public struct AiecsDarkness : IComponentData
    {
        public float LightLevel;
        public float DamageThreshold, DamageFractionPerSecond, RetreatRadius;
        public float2 DarkDestination;
        public double NextDamageTime;
        public double NextSampleTime;
        public byte HasDarkDestination;
    }

    public struct AiecsTactics : IComponentData
    {
        public AiecsCapability Flags;
        public float BaseSpeed, ChargeMultiplier, ChargeSeconds, ChargeCooldown;
        public float PredatorMultiplier, ChargeRemaining;
        public double NextChargeTime;
    }

    public struct AiecsPack : IComponentData
    {
        public float AssistRadius, CohesionRadius;
        public int CandidateBudget;
    }

    #endregion
}
