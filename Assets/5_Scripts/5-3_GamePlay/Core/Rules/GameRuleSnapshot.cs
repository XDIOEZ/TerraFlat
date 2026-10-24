using UnityEngine;

/// <summary>业务只接收已经完成外部修正的不可变配置。</summary>
public sealed class GameRuleSnapshot
{
    #region 分域规则快照

    public PlayerDeathRuleSet PlayerDeath { get; }
    public PlayerSurvivalRuleSet PlayerSurvival { get; }
    public CreatureCombatRuleSet CreatureCombat { get; }
    public WorldRuleSet World { get; }
    public ProductionRuleSet Production { get; }

    internal GameRuleSnapshot(GameRuleValues values)
    {
        values.Validate();
        PlayerDeath = new PlayerDeathRuleSet(values);
        PlayerSurvival = new PlayerSurvivalRuleSet(values);
        CreatureCombat = new CreatureCombatRuleSet(values);
        World = new WorldRuleSet(values);
        Production = new ProductionRuleSet(values);
    }

    #endregion
}

public readonly struct PlayerDeathRuleSet
{
    #region 死亡规则
    public bool DropAllCarriedItems { get; }
    internal PlayerDeathRuleSet(GameRuleValues values) => DropAllCarriedItems = values.DropAllCarriedItems;
    #endregion
}

public readonly struct PlayerSurvivalRuleSet
{
    #region 生存规则
    public float HungerDrainMultiplier { get; }
    public float StaminaConsumptionMultiplier { get; }
    public float StaminaRecoveryMultiplier { get; }
    public float HealingMultiplier { get; }
    public float EnvironmentalDamageMultiplier { get; }

    internal PlayerSurvivalRuleSet(GameRuleValues values)
    {
        HungerDrainMultiplier = values.HungerDrainMultiplier;
        StaminaConsumptionMultiplier = values.StaminaConsumptionMultiplier;
        StaminaRecoveryMultiplier = values.StaminaRecoveryMultiplier;
        HealingMultiplier = values.HealingMultiplier;
        EnvironmentalDamageMultiplier = values.EnvironmentalDamageMultiplier;
    }
    #endregion
}

public readonly struct CreatureCombatRuleSet
{
    #region 战斗规则
    public float PlayerAttackMultiplier { get; }
    public float AttackMultiplier { get; }
    public float MaxHealthMultiplier { get; }
    public float PlayerDamageTakenMultiplier { get; }

    internal CreatureCombatRuleSet(GameRuleValues values)
    {
        PlayerAttackMultiplier = values.PlayerAttackMultiplier;
        AttackMultiplier = values.CreatureAttackMultiplier;
        MaxHealthMultiplier = values.CreatureHealthMultiplier;
        PlayerDamageTakenMultiplier = values.PlayerDamageTakenMultiplier;
    }

    // 生物直接攻击玩家的受伤规则与环境、玩家自身攻击分别结算。
    public float ResolveReceivedDamageMultiplier(bool sourceIsPlayer, bool targetIsPlayer, bool environmental)
        => !sourceIsPlayer && targetIsPlayer && !environmental ? Mathf.Max(0f, PlayerDamageTakenMultiplier) : 1f;
    #endregion
}

public readonly struct WorldRuleSet
{
    #region 世界规则
    public float TimeSpeedMultiplier { get; }
    public float SpawnFrequencyMultiplier { get; }
    public float SpawnPopulationMultiplier { get; }
    public float LootAmountMultiplier { get; }

    internal WorldRuleSet(GameRuleValues values)
    {
        TimeSpeedMultiplier = values.TimeSpeedMultiplier;
        SpawnFrequencyMultiplier = values.SpawnFrequencyMultiplier;
        SpawnPopulationMultiplier = values.SpawnPopulationMultiplier;
        LootAmountMultiplier = values.LootAmountMultiplier;
    }
    #endregion
}

public readonly struct ProductionRuleSet
{
    #region 生产规则
    public float CropGrowthMultiplier { get; }
    public float FuelConsumptionMultiplier { get; }
    public float CraftingOutputMultiplier { get; }

    internal ProductionRuleSet(GameRuleValues values)
    {
        CropGrowthMultiplier = values.CropGrowthMultiplier;
        FuelConsumptionMultiplier = values.FuelConsumptionMultiplier;
        CraftingOutputMultiplier = values.CraftingOutputMultiplier;
    }
    #endregion
}
