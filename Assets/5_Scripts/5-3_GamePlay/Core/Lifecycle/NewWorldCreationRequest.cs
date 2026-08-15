using System;
using System.Globalization;

/// <summary>
/// 创建新世界所需的完整输入快照。
/// UI、自动化测试或其他调用方只负责组装请求；GameManager 负责验证并执行生命周期。
/// </summary>
[Serializable]
public sealed class NewWorldCreationRequest
{
    private const uint GeneratedNameMinimum = 10000000;
    private const uint GeneratedNameRange = 90000000;
    private const string GeneratedPlayerNamePrefix = "Player_";
    private const string GeneratedWorldNamePrefix = "World_";

    #region 请求数据

    public string SaveName { get; }
    public string PlayerName { get; }
    public string Seed { get; }
    public PlanetData PlanetData { get; }
    public TimeData TimeData { get; }
    public GameDifficultyId Difficulty { get; }
    public GameDifficultyRuleValues CustomDifficultyRules { get; }

    #endregion

    #region 构造与默认命名

    public NewWorldCreationRequest(
        string saveName,
        string playerName,
        string seed,
        PlanetData planetData,
        TimeData timeData,
        GameDifficultyId difficulty = GameDifficultyId.Simple,
        GameDifficultyRuleValues customDifficultyRules = null,
        ITextLibraryService textLibrary = null)
    {
        textLibrary ??= GameRes.ExistingInstance?.TextLibraries;
        bool requiresGeneratedName = string.IsNullOrWhiteSpace(saveName) ||
                                     string.IsNullOrWhiteSpace(playerName);
        string generatedSuffix = requiresGeneratedName ? CreateRandomNumericSuffix() : string.Empty;
        SaveName = ResolveNameOrDefault(saveName, CreateRandomWorldName(generatedSuffix, textLibrary));
        PlayerName = ResolveNameOrDefault(playerName, CreateRandomPlayerName(generatedSuffix, textLibrary));
        Seed = seed?.Trim() ?? string.Empty;
        PlanetData = planetData == null
            ? null
            : FastCloner.FastCloner.DeepClone(planetData);
        TimeData = timeData == null
            ? new TimeData()
            : timeData.CreateRuntimeCopy();
        Difficulty = GameDifficultyCatalog.Normalize(difficulty);
        CustomDifficultyRules = (customDifficultyRules ?? new GameDifficultyRuleValues()).Clone();
        CustomDifficultyRules.Normalize();
    }

    /// <summary>生成八位随机数字后缀，供默认玩家名和世界名共用。</summary>
    private static string CreateRandomNumericSuffix()
    {
        unchecked
        {
            uint randomBits = (uint)Guid.NewGuid().GetHashCode() ^ (uint)Environment.TickCount;
            uint value = GeneratedNameMinimum + randomBits % GeneratedNameRange;
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>生成默认玩家名；文字库可用时优先使用配置组合。</summary>
    public static string CreateRandomPlayerName(ITextLibraryService textLibrary = null)
    {
        return CreateRandomPlayerName(
            CreateRandomNumericSuffix(),
            textLibrary ?? GameRes.ExistingInstance?.TextLibraries);
    }

    /// <summary>生成默认存档名；文字库可用时优先使用地点名组合。</summary>
    public static string CreateRandomWorldName(ITextLibraryService textLibrary = null)
    {
        return CreateRandomWorldName(
            CreateRandomNumericSuffix(),
            textLibrary ?? GameRes.ExistingInstance?.TextLibraries);
    }

    /// <summary>优先从文字库生成玩家名，失败时保留兼容性的数字名。</summary>
    private static string CreateRandomPlayerName(string suffix, ITextLibraryService textLibrary)
    {
        if (textLibrary != null &&
            textLibrary.TryGenerate(TextLibraryKeys.PlayerName, out string generatedName))
        {
            return generatedName;
        }

        return $"{GeneratedPlayerNamePrefix}{suffix}";
    }

    /// <summary>优先从文字库生成存档名，失败时保留兼容性的数字名。</summary>
    private static string CreateRandomWorldName(string suffix, ITextLibraryService textLibrary)
    {
        if (textLibrary != null &&
            textLibrary.TryGenerate(TextLibraryKeys.SaveName, out string generatedName))
        {
            return generatedName;
        }

        return $"{GeneratedWorldNamePrefix}{suffix}";
    }

    /// <summary>保留玩家输入；空白名称使用带对应前缀的默认名称。</summary>
    private static string ResolveNameOrDefault(string requestedName, string generatedName)
    {
        string value = requestedName?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) ? generatedName : value;
    }

    #endregion

    #region 请求验证

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(PlayerName))
        {
            error = "玩家名称不能为空。";
            return false;
        }

        if (PlanetData == null)
        {
            error = "星球数据不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(PlanetData.Name))
        {
            error = "星球名称不能为空。";
            return false;
        }

        if (PlanetData.Radius <= 0)
        {
            error = "星球半径必须大于 0。";
            return false;
        }

        if (PlanetData.TopologyMode != WorldTopologyMode.Infinite &&
            PlanetData.TopologyMode != WorldTopologyMode.Wrapped)
        {
            error = "Unsupported world topology mode.";
            return false;
        }

        if (PlanetData.TopologyMode == WorldTopologyMode.Wrapped &&
            !WorldTopologyBounds.TryCreate(PlanetData, out _))
        {
            error = "Wrapped worlds require positive chunk dimensions and constructible aligned bounds.";
            return false;
        }

        if (!global::PlanetData.IsValidNoiseScale(PlanetData.NoiseScale))
        {
            error = "世界坐标缩放必须是合法有限值。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    #endregion
}
