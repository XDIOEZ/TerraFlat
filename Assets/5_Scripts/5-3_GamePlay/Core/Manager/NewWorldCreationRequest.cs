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
        GameDifficultyRuleValues customDifficultyRules = null)
    {
        bool requiresGeneratedName = string.IsNullOrWhiteSpace(saveName) ||
                                     string.IsNullOrWhiteSpace(playerName);
        string generatedName = requiresGeneratedName ? CreateRandomNumericName() : string.Empty;
        SaveName = ResolveNameOrDefault(saveName, generatedName);
        PlayerName = ResolveNameOrDefault(playerName, generatedName);
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

    /// <summary>生成八位纯数字名称，供未命名的新世界和玩家使用。</summary>
    public static string CreateRandomNumericName()
    {
        unchecked
        {
            uint randomBits = (uint)Guid.NewGuid().GetHashCode() ^ (uint)Environment.TickCount;
            uint value = GeneratedNameMinimum + randomBits % GeneratedNameRange;
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>保留玩家输入；空白名称统一使用本次请求的随机数字。</summary>
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
