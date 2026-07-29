using Sirenix.OdinInspector;

public partial class GameSaveData
{
    [ShowInInspector]
    public GameDifficultyId Difficulty = GameDifficultyId.Simple;

    [ShowInInspector]
    public bool CustomDifficultyDropAllCarriedItems;

    [ShowInInspector] public int CustomDifficultyDataVersion = 1;
    [ShowInInspector] public float CustomPlayerAttackMultiplier = 1f;
    [ShowInInspector] public float CustomCreatureAttackMultiplier = 1f;
    [ShowInInspector] public float CustomCreatureHealthMultiplier = 1f;
    [ShowInInspector] public float CustomHungerDrainMultiplier = 1f;
    [ShowInInspector] public float CustomStaminaConsumptionMultiplier = 1f;
    [ShowInInspector] public float CustomStaminaRecoveryMultiplier = 1f;
    [ShowInInspector] public float CustomHealingMultiplier = 1f;
    [ShowInInspector] public float CustomEnvironmentalDamageMultiplier = 1f;
    [ShowInInspector] public float CustomTimeSpeedMultiplier = 1f;
    [ShowInInspector] public float CustomSpawnFrequencyMultiplier = 1f;
    [ShowInInspector] public float CustomSpawnPopulationMultiplier = 1f;
    [ShowInInspector] public float CustomLootAmountMultiplier = 1f;
    [ShowInInspector] public float CustomCropGrowthMultiplier = 1f;
    [ShowInInspector] public float CustomSmeltingSpeedMultiplier = 1f;
    [ShowInInspector] public float CustomFuelConsumptionMultiplier = 1f;
    [ShowInInspector] public float CustomCraftingOutputMultiplier = 1f;
}
