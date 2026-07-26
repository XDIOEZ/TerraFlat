using System;
using System.Collections.Generic;

public enum GameDifficultyId
{
    Simple = 0,
    Hard = 1
}

public sealed class PlayerDeathDifficultyRules
{
    public bool DropAllCarriedItems { get; }

    public PlayerDeathDifficultyRules(bool dropAllCarriedItems)
    {
        DropAllCarriedItems = dropAllCarriedItems;
    }
}

/// <summary>
/// 生物战斗规则的统一扩展点。当前难度不改变战斗数值，
/// 后续攻击力、血量等系统应读取这里的倍率，而不是自行判断难度枚举。
/// </summary>
public sealed class CreatureCombatDifficultyRules
{
    public float AttackMultiplier { get; }
    public float MaxHealthMultiplier { get; }

    public CreatureCombatDifficultyRules(float attackMultiplier, float maxHealthMultiplier)
    {
        AttackMultiplier = attackMultiplier;
        MaxHealthMultiplier = maxHealthMultiplier;
    }
}

public sealed class GameDifficultyDefinition
{
    public GameDifficultyId Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public PlayerDeathDifficultyRules PlayerDeath { get; }
    public CreatureCombatDifficultyRules CreatureCombat { get; }

    public GameDifficultyDefinition(
        GameDifficultyId id,
        string displayName,
        string description,
        PlayerDeathDifficultyRules playerDeath,
        CreatureCombatDifficultyRules creatureCombat)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        PlayerDeath = playerDeath;
        CreatureCombat = creatureCombat;
    }
}

public static class GameDifficultyCatalog
{
    private static readonly GameDifficultyDefinition Simple = new GameDifficultyDefinition(
        GameDifficultyId.Simple,
        "简单",
        "保持当前游戏配置。玩家死亡后不会掉落随身物品。",
        new PlayerDeathDifficultyRules(dropAllCarriedItems: false),
        new CreatureCombatDifficultyRules(attackMultiplier: 1f, maxHealthMultiplier: 1f));

    private static readonly GameDifficultyDefinition Hard = new GameDifficultyDefinition(
        GameDifficultyId.Hard,
        "困难",
        "玩家死亡时，背包、快捷栏、装备及随身制作槽中的物品会全部掉落。",
        new PlayerDeathDifficultyRules(dropAllCarriedItems: true),
        new CreatureCombatDifficultyRules(attackMultiplier: 1f, maxHealthMultiplier: 1f));

    private static readonly IReadOnlyList<GameDifficultyDefinition> Definitions =
        new[] { Simple, Hard };

    public static IReadOnlyList<GameDifficultyDefinition> All => Definitions;

    public static GameDifficultyDefinition Get(GameDifficultyId id)
    {
        return id == GameDifficultyId.Hard ? Hard : Simple;
    }

    public static GameDifficultyId Normalize(GameDifficultyId id)
    {
        return id == GameDifficultyId.Hard ? GameDifficultyId.Hard : GameDifficultyId.Simple;
    }
}

/// <summary>
/// 当前世界难度的唯一运行时入口。难度选择属于存档，不使用全局 PlayerPrefs。
/// </summary>
public static class GameDifficultyService
{
    public static event Action<GameDifficultyId> DifficultyChanged;

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

    public static GameDifficultyDefinition Current => GameDifficultyCatalog.Get(CurrentId);

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
        DifficultyChanged?.Invoke(normalized);
        error = null;
        return true;
    }
}
