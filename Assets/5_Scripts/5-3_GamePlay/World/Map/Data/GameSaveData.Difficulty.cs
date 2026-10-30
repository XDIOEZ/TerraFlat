using Sirenix.OdinInspector;

public partial class GameSaveData
{
    [ShowInInspector]
    [MemoryPack.MemoryPackOrder(17)] public GameDifficultyId Difficulty = GameDifficultyId.Level0;

    [ShowInInspector]
    [MemoryPack.MemoryPackOrder(4)] public bool CustomDifficultyDropAllCarriedItems;

    [ShowInInspector] [MemoryPack.MemoryPackOrder(10)] public float CustomPlayerAttackMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(1)] public float CustomCreatureAttackMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(2)] public float CustomCreatureHealthMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(8)] public float CustomHungerDrainMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(13)] public float CustomStaminaConsumptionMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(14)] public float CustomStaminaRecoveryMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(7)] public float CustomHealingMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(5)] public float CustomEnvironmentalDamageMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(15)] public float CustomTimeSpeedMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(11)] public float CustomSpawnFrequencyMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(12)] public float CustomSpawnPopulationMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(9)] public float CustomLootAmountMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(3)] public float CustomCropGrowthMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(6)] public float CustomFuelConsumptionMultiplier = 1f;
    [ShowInInspector] [MemoryPack.MemoryPackOrder(0)] public float CustomCraftingOutputMultiplier = 1f;
}
