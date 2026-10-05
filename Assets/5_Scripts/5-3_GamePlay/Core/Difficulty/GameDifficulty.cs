using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

public enum GameDifficultyId
{
    Level0 = 0,
    Level1 = 1,
    Level2 = 2,
    Level3 = 3,
    Level4 = 4,
    Level5 = 5,
    Level6 = 6,
    Level7 = 7,
    Level8 = 8,
    Level9 = 9,
    Level10 = 10,
    Level11 = 11,
    Level12 = 12,
    Level13 = 13,
    Level14 = 14,
    Level15 = 15,
    Level16 = 16,
    Level17 = 17,
    Level18 = 18,
    Level19 = 19,
    Level20 = 20,
    Custom = 100
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
/// 生物战斗规则的统一扩展点；攻击与生命系统统一读取这里的倍率。
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
    public const int MinLevel = 0;
    public const int MaxLevel = 20;

    private static readonly IReadOnlyList<GameDifficultyDefinition> Definitions = BuildDefinitions();

    /// <summary>0 到 20 的正式难度等级列表。</summary>
    public static IReadOnlyList<GameDifficultyDefinition> All => Definitions;

    public static GameDifficultyDefinition Get(GameDifficultyId id)
    {
        if (id == GameDifficultyId.Custom)
            return CreateCustom(ReadCustomRules(SaveDataMgr.Instance?.SaveData));

        int level = Mathf.Clamp((int)id, MinLevel, MaxLevel);
        return Definitions[level];
    }

    public static GameDifficultyId Normalize(GameDifficultyId id)
    {
        if (id == GameDifficultyId.Custom)
            return GameDifficultyId.Custom;

        return (GameDifficultyId)Mathf.Clamp((int)id, MinLevel, MaxLevel);
    }

    public static int GetLevel(GameDifficultyId id)
    {
        return id == GameDifficultyId.Custom
            ? MinLevel
            : Mathf.Clamp((int)id, MinLevel, MaxLevel);
    }

    private static IReadOnlyList<GameDifficultyDefinition> BuildDefinitions()
    {
        var definitions = new GameDifficultyDefinition[MaxLevel - MinLevel + 1];
        for (int level = MinLevel; level <= MaxLevel; level++)
            definitions[level] = CreateLevelDefinition(level);

        return definitions;
    }

    /// <summary>难度等级使用同一条连续曲线生成规则，避免维护 21 份重复配置。</summary>
    private static GameDifficultyDefinition CreateLevelDefinition(int level)
    {
        level = Mathf.Clamp(level, MinLevel, MaxLevel);
        float t = MaxLevel <= 0 ? 0f : level / (float)MaxLevel;

        return new GameDifficultyDefinition(
            (GameDifficultyId)level,
            $"难度 {level}",
            GetLevelDescription(level),
            new PlayerDeathDifficultyRules(dropAllCarriedItems: level >= 10),
            new PlayerSurvivalDifficultyRules(
                Mathf.Lerp(1f, 1.8f, t),
                Mathf.Lerp(1f, 1.7f, t),
                Mathf.Lerp(1f, 0.65f, t),
                Mathf.Lerp(1f, 0.70f, t),
                Mathf.Lerp(1f, 1.8f, t)),
            new CreatureCombatDifficultyRules(
                Mathf.Lerp(1f, 0.65f, t),
                Mathf.Lerp(1f, 2.2f, t),
                Mathf.Lerp(1f, 2.5f, t)),
            new WorldDifficultyRules(
                Mathf.Lerp(1f, 1.25f, t),
                Mathf.Lerp(1f, 1.8f, t),
                Mathf.Lerp(1f, 1.7f, t),
                Mathf.Lerp(1f, 0.65f, t)),
            new ProductionDifficultyRules(
                Mathf.Lerp(1f, 0.70f, t),
                Mathf.Lerp(1f, 0.75f, t),
                Mathf.Lerp(1f, 1.6f, t),
                Mathf.Lerp(1f, 0.80f, t)));
    }

    private static string GetLevelDescription(int level)
    {
        if (level <= 0)
            return "基础规则，适合熟悉世界与系统。";
        if (level <= 4)
            return "轻度挑战，敌人与生存压力小幅提升。";
        if (level <= 9)
            return "标准挑战，战斗、生存与资源压力同步提高。";
        if (level <= 14)
            return "高强度挑战，死亡惩罚与世界威胁明显增加。";
        if (level < MaxLevel)
            return "严酷挑战，资源、生产与战斗都要求更高规划。";

        return "极限挑战，适合已经熟悉全部系统的玩家。";
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
    public const string SettingsProviderId = "difficulty";
    public const string DifficultySettingKey = "difficulty.mode";

    public static event Action<GameDifficultyId> DifficultyChanged;

    private static readonly ISettingsProvider settingsProvider =
        CreateSettingsProvider();

    /// <summary>供游戏内设置使用的难度按钮式切换契约。</summary>
    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    private static GameSaveData cachedSaveData;
    private static GameDifficultyId cachedDifficultyId;
    private static GameDifficultyDefinition cachedDefinition;

    public static GameDifficultyId CurrentId
    {
        get
        {
            GameSaveData saveData = SaveDataMgr.Instance?.SaveData;
            return saveData == null
                ? GameDifficultyId.Level0
                : GameDifficultyCatalog.Normalize(saveData.Difficulty);
        }
    }

    public static GameDifficultyDefinition Current
    {
        get
        {
            GameSaveData saveData = SaveDataMgr.Instance?.SaveData;
            GameDifficultyId difficultyId = saveData == null
                ? GameDifficultyId.Level0
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

    #region 设置提供者

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSettingsProviderOnLoad()
    {
        RegisterSettingsProvider();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        cachedSaveData = null;
        cachedDefinition = null;
        DifficultyChanged = null;
    }

    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    private static ISettingsProvider CreateSettingsProvider()
    {
        return new DifficultySettingsProvider();
    }

    private sealed class DifficultySettingsProvider : ISettingsProvider
    {
        private static readonly IReadOnlyList<SettingOption> Options = BuildOptions();

        private readonly IReadOnlyList<ISettingsSwitch> switches;

        public DifficultySettingsProvider()
        {
            switches = new ISettingsSwitch[]
            {
                new SettingsSwitch(
                    new SettingDescriptor(
                        DifficultySettingKey,
                        "游戏难度",
                        SettingControlType.Switch,
                        "world",
                        order: 0),
                    Options,
                    () => GameDifficultyCatalog.GetLevel(CurrentId),
                    TrySetDifficulty)
            };
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "难度";
        public int Order => 80;
        public IReadOnlyList<ISettingsToggle> ToggleSettings =>
            Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSlider> SliderSettings =>
            Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings =>
            Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => switches;

        public void ResetToDefaults()
        {
            TrySetCurrent(GameDifficultyId.Level0, out _);
        }

        private static string TrySetDifficulty(int index)
        {
            if (index < 0 || index >= Options.Count)
                return "游戏难度选项无效。";

            return TrySetCurrent((GameDifficultyId)index, out string error)
                ? null
                : error;
        }

        private static IReadOnlyList<SettingOption> BuildOptions()
        {
            var options = new SettingOption[GameDifficultyCatalog.All.Count];
            for (int i = 0; i < options.Length; i++)
            {
                GameDifficultyDefinition definition = GameDifficultyCatalog.All[i];
                options[i] = new SettingOption($"level_{i}", definition.DisplayName);
            }

            return options;
        }
    }

    #endregion

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
