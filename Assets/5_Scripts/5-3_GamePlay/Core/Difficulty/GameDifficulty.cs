using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameDifficultyId
{
    Simple = 0,
    Hard = 1,
    Custom = 2
}

public sealed class PlayerDeathDifficultyRules
{
    public bool DropAllCarriedItems { get; }

    public PlayerDeathDifficultyRules(bool dropAllCarriedItems)
    {
        DropAllCarriedItems = dropAllCarriedItems;
    }
}

/// <summary>玩家生存消耗、恢复与环境威胁规则。</summary>
public sealed class PlayerSurvivalDifficultyRules
{
    public float HungerDrainMultiplier { get; }
    public float StaminaConsumptionMultiplier { get; }
    public float StaminaRecoveryMultiplier { get; }
    public float HealingMultiplier { get; }
    public float EnvironmentalDamageMultiplier { get; }

    public PlayerSurvivalDifficultyRules(
        float hungerDrainMultiplier,
        float staminaConsumptionMultiplier,
        float staminaRecoveryMultiplier,
        float healingMultiplier,
        float environmentalDamageMultiplier)
    {
        HungerDrainMultiplier = hungerDrainMultiplier;
        StaminaConsumptionMultiplier = staminaConsumptionMultiplier;
        StaminaRecoveryMultiplier = staminaRecoveryMultiplier;
        HealingMultiplier = healingMultiplier;
        EnvironmentalDamageMultiplier = environmentalDamageMultiplier;
    }
}

/// <summary>
/// 生物战斗规则的统一扩展点。当前难度不改变战斗数值，
/// 后续攻击力、血量等系统应读取这里的倍率，而不是自行判断难度枚举。
/// </summary>
public sealed class CreatureCombatDifficultyRules
{
    public float PlayerAttackMultiplier { get; }
    public float AttackMultiplier { get; }
    public float MaxHealthMultiplier { get; }

    public CreatureCombatDifficultyRules(
        float playerAttackMultiplier,
        float attackMultiplier,
        float maxHealthMultiplier)
    {
        PlayerAttackMultiplier = playerAttackMultiplier;
        AttackMultiplier = attackMultiplier;
        MaxHealthMultiplier = maxHealthMultiplier;
    }
}

/// <summary>昼夜推进、生物生态与战利品规则。</summary>
public sealed class WorldDifficultyRules
{
    public float TimeSpeedMultiplier { get; }
    public float SpawnFrequencyMultiplier { get; }
    public float SpawnPopulationMultiplier { get; }
    public float LootAmountMultiplier { get; }

    public WorldDifficultyRules(
        float timeSpeedMultiplier,
        float spawnFrequencyMultiplier,
        float spawnPopulationMultiplier,
        float lootAmountMultiplier)
    {
        TimeSpeedMultiplier = timeSpeedMultiplier;
        SpawnFrequencyMultiplier = spawnFrequencyMultiplier;
        SpawnPopulationMultiplier = spawnPopulationMultiplier;
        LootAmountMultiplier = lootAmountMultiplier;
    }
}

/// <summary>种植、熔炼、燃料与制作产出规则。</summary>
public sealed class ProductionDifficultyRules
{
    public float CropGrowthMultiplier { get; }
    public float SmeltingSpeedMultiplier { get; }
    public float FuelConsumptionMultiplier { get; }
    public float CraftingOutputMultiplier { get; }

    public ProductionDifficultyRules(
        float cropGrowthMultiplier,
        float smeltingSpeedMultiplier,
        float fuelConsumptionMultiplier,
        float craftingOutputMultiplier)
    {
        CropGrowthMultiplier = cropGrowthMultiplier;
        SmeltingSpeedMultiplier = smeltingSpeedMultiplier;
        FuelConsumptionMultiplier = fuelConsumptionMultiplier;
        CraftingOutputMultiplier = craftingOutputMultiplier;
    }
}

/// <summary>
/// 自定义难度的可编辑值对象。它本身不进入 MemoryPack，存档仍由 GameSaveData 的基础字段承担。
/// </summary>
public sealed class GameDifficultyRuleValues
{
    public bool DropAllCarriedItems;
    public float PlayerAttackMultiplier = 1f;
    public float CreatureAttackMultiplier = 1f;
    public float CreatureHealthMultiplier = 1f;
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
    public float SmeltingSpeedMultiplier = 1f;
    public float FuelConsumptionMultiplier = 1f;
    public float CraftingOutputMultiplier = 1f;

    public GameDifficultyRuleValues Clone()
    {
        return (GameDifficultyRuleValues)MemberwiseClone();
    }

    public void Normalize()
    {
        PlayerAttackMultiplier = NormalizeMultiplier(PlayerAttackMultiplier, 0f);
        CreatureAttackMultiplier = NormalizeMultiplier(CreatureAttackMultiplier, 0f);
        CreatureHealthMultiplier = NormalizeMultiplier(CreatureHealthMultiplier);
        HungerDrainMultiplier = NormalizeMultiplier(HungerDrainMultiplier, 0f);
        StaminaConsumptionMultiplier = NormalizeMultiplier(StaminaConsumptionMultiplier, 0f);
        StaminaRecoveryMultiplier = NormalizeMultiplier(StaminaRecoveryMultiplier, 0f);
        HealingMultiplier = NormalizeMultiplier(HealingMultiplier, 0f);
        EnvironmentalDamageMultiplier = NormalizeMultiplier(EnvironmentalDamageMultiplier, 0f);
        TimeSpeedMultiplier = NormalizeMultiplier(TimeSpeedMultiplier, 0f);
        SpawnFrequencyMultiplier = NormalizeMultiplier(SpawnFrequencyMultiplier, 0f);
        SpawnPopulationMultiplier = NormalizeMultiplier(SpawnPopulationMultiplier, 0.25f);
        LootAmountMultiplier = NormalizeMultiplier(LootAmountMultiplier, 0f);
        CropGrowthMultiplier = NormalizeMultiplier(CropGrowthMultiplier, 0f);
        SmeltingSpeedMultiplier = NormalizeMultiplier(SmeltingSpeedMultiplier, 0.1f);
        FuelConsumptionMultiplier = NormalizeMultiplier(FuelConsumptionMultiplier, 0f);
        CraftingOutputMultiplier = NormalizeMultiplier(CraftingOutputMultiplier, 0.25f);
    }

    private static float NormalizeMultiplier(float value, float minimum = 0.1f)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 1f;

        return Mathf.Clamp(value, minimum, 4f);
    }
}

public sealed class GameDifficultyDefinition
{
    public GameDifficultyId Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public PlayerDeathDifficultyRules PlayerDeath { get; }
    public PlayerSurvivalDifficultyRules PlayerSurvival { get; }
    public CreatureCombatDifficultyRules CreatureCombat { get; }
    public WorldDifficultyRules World { get; }
    public ProductionDifficultyRules Production { get; }

    public GameDifficultyDefinition(
        GameDifficultyId id,
        string displayName,
        string description,
        PlayerDeathDifficultyRules playerDeath,
        PlayerSurvivalDifficultyRules playerSurvival,
        CreatureCombatDifficultyRules creatureCombat,
        WorldDifficultyRules world,
        ProductionDifficultyRules production)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        PlayerDeath = playerDeath;
        PlayerSurvival = playerSurvival;
        CreatureCombat = creatureCombat;
        World = world;
        Production = production;
    }
}

public static class GameDifficultyCatalog
{
    private static readonly GameDifficultyDefinition Simple = new GameDifficultyDefinition(
        GameDifficultyId.Simple,
        "简单",
        "保持当前游戏配置。玩家死亡后不会掉落随身物品。",
        new PlayerDeathDifficultyRules(dropAllCarriedItems: false),
        new PlayerSurvivalDifficultyRules(1f, 1f, 1f, 1f, 1f),
        new CreatureCombatDifficultyRules(1f, 1f, 1f),
        new WorldDifficultyRules(1f, 1f, 1f, 1f),
        new ProductionDifficultyRules(1f, 1f, 1f, 1f));

    private static readonly GameDifficultyDefinition Hard = new GameDifficultyDefinition(
        GameDifficultyId.Hard,
        "困难",
        "敌对生物更危险、生存消耗更快、恢复更慢，且死亡会掉落全部随身物品。",
        new PlayerDeathDifficultyRules(dropAllCarriedItems: true),
        new PlayerSurvivalDifficultyRules(1.25f, 1.2f, 0.8f, 0.8f, 1.25f),
        new CreatureCombatDifficultyRules(0.9f, 1.35f, 1.35f),
        new WorldDifficultyRules(1f, 1.35f, 1.3f, 0.85f),
        new ProductionDifficultyRules(0.9f, 0.9f, 1.2f, 0.9f));

    private static readonly IReadOnlyList<GameDifficultyDefinition> Definitions =
        new[] { Simple, Hard };

    /// <summary>官方预设列表，后续新增官方难度时只需追加到这里。</summary>
    public static IReadOnlyList<GameDifficultyDefinition> All => Definitions;

    public static GameDifficultyDefinition Get(GameDifficultyId id)
    {
        return id switch
        {
            GameDifficultyId.Hard => Hard,
            GameDifficultyId.Custom => CreateCustom(ReadCustomRules(SaveDataMgr.Instance?.SaveData)),
            _ => Simple
        };
    }

    public static GameDifficultyId Normalize(GameDifficultyId id)
    {
        return id switch
        {
            GameDifficultyId.Hard => GameDifficultyId.Hard,
            GameDifficultyId.Custom => GameDifficultyId.Custom,
            _ => GameDifficultyId.Simple
        };
    }

    public static GameDifficultyDefinition CreateCustom(GameDifficultyRuleValues values)
    {
        values ??= new GameDifficultyRuleValues();
        values = values.Clone();
        values.Normalize();

        string description = values.DropAllCarriedItems
            ? "使用玩家自定义规则；死亡时掉落全部随身物品。"
            : "使用玩家自定义规则；死亡后保留全部随身物品。";

        return new GameDifficultyDefinition(
            GameDifficultyId.Custom,
            "自定义",
            description,
            new PlayerDeathDifficultyRules(values.DropAllCarriedItems),
            new PlayerSurvivalDifficultyRules(
                values.HungerDrainMultiplier,
                values.StaminaConsumptionMultiplier,
                values.StaminaRecoveryMultiplier,
                values.HealingMultiplier,
                values.EnvironmentalDamageMultiplier),
            new CreatureCombatDifficultyRules(
                values.PlayerAttackMultiplier,
                values.CreatureAttackMultiplier,
                values.CreatureHealthMultiplier),
            new WorldDifficultyRules(
                values.TimeSpeedMultiplier,
                values.SpawnFrequencyMultiplier,
                values.SpawnPopulationMultiplier,
                values.LootAmountMultiplier),
            new ProductionDifficultyRules(
                values.CropGrowthMultiplier,
                values.SmeltingSpeedMultiplier,
                values.FuelConsumptionMultiplier,
                values.CraftingOutputMultiplier));
    }

    public static GameDifficultyRuleValues ReadCustomRules(GameSaveData saveData)
    {
        if (saveData == null)
            return new GameDifficultyRuleValues();

        if (saveData.CustomDifficultyDataVersion <= 0)
        {
            return new GameDifficultyRuleValues
            {
                DropAllCarriedItems = saveData.CustomDifficultyDropAllCarriedItems
            };
        }

        var values = new GameDifficultyRuleValues
        {
            DropAllCarriedItems = saveData.CustomDifficultyDropAllCarriedItems,
            PlayerAttackMultiplier = saveData.CustomPlayerAttackMultiplier,
            CreatureAttackMultiplier = saveData.CustomCreatureAttackMultiplier,
            CreatureHealthMultiplier = saveData.CustomCreatureHealthMultiplier,
            HungerDrainMultiplier = saveData.CustomHungerDrainMultiplier,
            StaminaConsumptionMultiplier = saveData.CustomStaminaConsumptionMultiplier,
            StaminaRecoveryMultiplier = saveData.CustomStaminaRecoveryMultiplier,
            HealingMultiplier = saveData.CustomHealingMultiplier,
            EnvironmentalDamageMultiplier = saveData.CustomEnvironmentalDamageMultiplier,
            TimeSpeedMultiplier = saveData.CustomTimeSpeedMultiplier,
            SpawnFrequencyMultiplier = saveData.CustomSpawnFrequencyMultiplier,
            SpawnPopulationMultiplier = saveData.CustomSpawnPopulationMultiplier,
            LootAmountMultiplier = saveData.CustomLootAmountMultiplier,
            CropGrowthMultiplier = saveData.CustomCropGrowthMultiplier,
            SmeltingSpeedMultiplier = saveData.CustomSmeltingSpeedMultiplier,
            FuelConsumptionMultiplier = saveData.CustomFuelConsumptionMultiplier,
            CraftingOutputMultiplier = saveData.CustomCraftingOutputMultiplier
        };
        values.Normalize();
        return values;
    }

    public static void WriteCustomRules(GameSaveData saveData, GameDifficultyRuleValues values)
    {
        if (saveData == null)
            return;

        values ??= new GameDifficultyRuleValues();
        values = values.Clone();
        values.Normalize();

        saveData.CustomDifficultyDataVersion = 1;
        saveData.CustomDifficultyDropAllCarriedItems = values.DropAllCarriedItems;
        saveData.CustomPlayerAttackMultiplier = values.PlayerAttackMultiplier;
        saveData.CustomCreatureAttackMultiplier = values.CreatureAttackMultiplier;
        saveData.CustomCreatureHealthMultiplier = values.CreatureHealthMultiplier;
        saveData.CustomHungerDrainMultiplier = values.HungerDrainMultiplier;
        saveData.CustomStaminaConsumptionMultiplier = values.StaminaConsumptionMultiplier;
        saveData.CustomStaminaRecoveryMultiplier = values.StaminaRecoveryMultiplier;
        saveData.CustomHealingMultiplier = values.HealingMultiplier;
        saveData.CustomEnvironmentalDamageMultiplier = values.EnvironmentalDamageMultiplier;
        saveData.CustomTimeSpeedMultiplier = values.TimeSpeedMultiplier;
        saveData.CustomSpawnFrequencyMultiplier = values.SpawnFrequencyMultiplier;
        saveData.CustomSpawnPopulationMultiplier = values.SpawnPopulationMultiplier;
        saveData.CustomLootAmountMultiplier = values.LootAmountMultiplier;
        saveData.CustomCropGrowthMultiplier = values.CropGrowthMultiplier;
        saveData.CustomSmeltingSpeedMultiplier = values.SmeltingSpeedMultiplier;
        saveData.CustomFuelConsumptionMultiplier = values.FuelConsumptionMultiplier;
        saveData.CustomCraftingOutputMultiplier = values.CraftingOutputMultiplier;
    }
}

/// <summary>
/// 当前世界难度的唯一运行时入口。难度选择属于存档，不使用全局 PlayerPrefs。
/// </summary>
public static class GameDifficultyService
{
    public static event Action<GameDifficultyId> DifficultyChanged;

    private static GameSaveData cachedSaveData;
    private static GameDifficultyId cachedDifficultyId;
    private static GameDifficultyDefinition cachedDefinition;

    public static GameDifficultyId CurrentId
    {
        get
        {
            GameSaveData saveData = SaveDataMgr.Instance?.SaveData;
            return saveData == null
                ? GameDifficultyId.Simple
                : GameDifficultyCatalog.Normalize(saveData.Difficulty);
        }
    }

    public static GameDifficultyDefinition Current
    {
        get
        {
            GameSaveData saveData = SaveDataMgr.Instance?.SaveData;
            GameDifficultyId difficultyId = saveData == null
                ? GameDifficultyId.Simple
                : GameDifficultyCatalog.Normalize(saveData.Difficulty);

            if (cachedDefinition != null &&
                ReferenceEquals(cachedSaveData, saveData) &&
                cachedDifficultyId == difficultyId)
            {
                return cachedDefinition;
            }

            cachedSaveData = saveData;
            cachedDifficultyId = difficultyId;
            cachedDefinition = difficultyId == GameDifficultyId.Custom
                ? GameDifficultyCatalog.CreateCustom(GameDifficultyCatalog.ReadCustomRules(saveData))
                : GameDifficultyCatalog.Get(difficultyId);
            return cachedDefinition;
        }
    }

    public static bool TrySetCurrent(GameDifficultyId difficulty, out string error)
    {
        GameSaveData saveData = SaveDataMgr.Instance?.SaveData;
        if (saveData == null)
        {
            error = "当前没有已加载的游戏存档，无法修改难度。";
            return false;
        }

        GameDifficultyId normalized = GameDifficultyCatalog.Normalize(difficulty);
        if (saveData.Difficulty == normalized)
        {
            error = null;
            return true;
        }

        saveData.Difficulty = normalized;
        InvalidateCache();
        DifficultyChanged?.Invoke(normalized);
        error = null;
        return true;
    }

    public static bool TrySetCustom(GameDifficultyRuleValues values, out string error)
    {
        GameSaveData saveData = SaveDataMgr.Instance?.SaveData;
        if (saveData == null)
        {
            error = "当前没有已加载的游戏存档，无法修改难度。";
            return false;
        }

        saveData.Difficulty = GameDifficultyId.Custom;
        GameDifficultyCatalog.WriteCustomRules(saveData, values);
        InvalidateCache();
        DifficultyChanged?.Invoke(GameDifficultyId.Custom);

        error = null;
        return true;
    }

    #region 通用倍率工具

    public static int ScaleCount(int baseValue, float multiplier, int minimum = 0)
    {
        if (baseValue <= 0 || multiplier <= 0f)
            return minimum;

        return Mathf.Max(minimum, Mathf.RoundToInt(baseValue * multiplier));
    }

    public static int ScaleRandomizedAmount(int baseValue, float multiplier)
    {
        if (baseValue <= 0 || multiplier <= 0f)
            return 0;

        float scaled = baseValue * multiplier;
        int result = Mathf.FloorToInt(scaled);
        if (UnityEngine.Random.value < scaled - result)
            result++;

        return Mathf.Max(0, result);
    }

    public static bool IsPlayer(Item candidate)
    {
        if (candidate == null)
            return false;

        Item owner = candidate.Owner != null ? candidate.Owner : candidate;
        return owner is Player || owner.GetComponent<Player>() != null;
    }

    public static float ResolveDirectDamageMultiplier(Item attacker, Item receiver)
    {
        GameDifficultyDefinition difficulty = Current;
        float multiplier = IsPlayer(attacker)
            ? difficulty.CreatureCombat.PlayerAttackMultiplier
            : difficulty.CreatureCombat.AttackMultiplier;

        if (!IsPlayer(receiver))
            multiplier /= Mathf.Max(0.1f, difficulty.CreatureCombat.MaxHealthMultiplier);

        return Mathf.Max(0f, multiplier);
    }

    public static float ResolveEnvironmentalDamageMultiplier(Item receiver)
    {
        return IsPlayer(receiver)
            ? Mathf.Max(0f, Current.PlayerSurvival.EnvironmentalDamageMultiplier)
            : 1f;
    }

    public static float ResolveHealingMultiplier(Item receiver)
    {
        return IsPlayer(receiver)
            ? Mathf.Max(0f, Current.PlayerSurvival.HealingMultiplier)
            : 1f;
    }

    public static float ResolveStaminaDeltaMultiplier(Item owner, float delta)
    {
        if (!IsPlayer(owner))
            return 1f;

        return delta < 0f
            ? Mathf.Max(0f, Current.PlayerSurvival.StaminaConsumptionMultiplier)
            : Mathf.Max(0f, Current.PlayerSurvival.StaminaRecoveryMultiplier);
    }

    private static void InvalidateCache()
    {
        cachedSaveData = null;
        cachedDefinition = null;
    }

    #endregion
}
