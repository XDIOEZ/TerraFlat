using System;

/// <summary>业务规则的可写候选值，由配置来源在发布前组合修正。</summary>
public sealed class GameRuleValues
{
    #region 业务基础值

    public bool DropAllCarriedItems;
    public float PlayerAttackMultiplier = 1f;
    public float CreatureAttackMultiplier = 1f;
    public float CreatureHealthMultiplier = 1f;
    public float PlayerDamageTakenMultiplier = 1f;
    public float HungerDrainMultiplier = 1f;
    public float StaminaConsumptionMultiplier = 1f;
    public float StaminaRecoveryMultiplier = 1f;
    public float HealingMultiplier = 1f;
    public float EnvironmentalDamageMultiplier = 1f;
    public float TimeSpeedMultiplier = 1f;
    public float SpawnFrequencyMultiplier = 1f;
    public float SpawnPopulationMultiplier = 1f;
    public float LootAmountMultiplier = 1f;
    public float CropGrowthMultiplier = 1f;
    public float FuelConsumptionMultiplier = 1f;
    public float CraftingOutputMultiplier = 1f;

    public GameRuleValues Clone() => (GameRuleValues)MemberwiseClone();

    internal void Validate()
    {
        RequireMultiplier(PlayerAttackMultiplier, nameof(PlayerAttackMultiplier));
        RequireMultiplier(CreatureAttackMultiplier, nameof(CreatureAttackMultiplier));
        RequireMultiplier(CreatureHealthMultiplier, nameof(CreatureHealthMultiplier));
        RequireMultiplier(PlayerDamageTakenMultiplier, nameof(PlayerDamageTakenMultiplier));
        RequireMultiplier(HungerDrainMultiplier, nameof(HungerDrainMultiplier));
        RequireMultiplier(StaminaConsumptionMultiplier, nameof(StaminaConsumptionMultiplier));
        RequireMultiplier(StaminaRecoveryMultiplier, nameof(StaminaRecoveryMultiplier));
        RequireMultiplier(HealingMultiplier, nameof(HealingMultiplier));
        RequireMultiplier(EnvironmentalDamageMultiplier, nameof(EnvironmentalDamageMultiplier));
        RequireMultiplier(TimeSpeedMultiplier, nameof(TimeSpeedMultiplier));
        RequireMultiplier(SpawnFrequencyMultiplier, nameof(SpawnFrequencyMultiplier));
        RequireMultiplier(SpawnPopulationMultiplier, nameof(SpawnPopulationMultiplier));
        RequireMultiplier(LootAmountMultiplier, nameof(LootAmountMultiplier));
        RequireMultiplier(CropGrowthMultiplier, nameof(CropGrowthMultiplier));
        RequireMultiplier(FuelConsumptionMultiplier, nameof(FuelConsumptionMultiplier));
        RequireMultiplier(CraftingOutputMultiplier, nameof(CraftingOutputMultiplier));
    }

    private static void RequireMultiplier(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new ArgumentOutOfRangeException(name, value, "业务规则倍率必须是非负有限值。");
    }

    #endregion
}
