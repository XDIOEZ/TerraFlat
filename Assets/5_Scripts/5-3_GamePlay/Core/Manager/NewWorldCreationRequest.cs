using System;

/// <summary>
/// 创建新世界所需的完整输入快照。
/// UI、自动化测试或其他调用方只负责组装请求；GameManager 负责验证并执行生命周期。
/// </summary>
[Serializable]
public sealed class NewWorldCreationRequest
{
    public string SaveName { get; }
    public string PlayerName { get; }
    public string Seed { get; }
    public PlanetData PlanetData { get; }
    public TimeData TimeData { get; }
    public GameDifficultyId Difficulty { get; }
    public GameDifficultyRuleValues CustomDifficultyRules { get; }

    public NewWorldCreationRequest(
        string saveName,
        string playerName,
        string seed,
        PlanetData planetData,
        TimeData timeData,
        GameDifficultyId difficulty = GameDifficultyId.Simple,
        GameDifficultyRuleValues customDifficultyRules = null)
    {
        SaveName = saveName?.Trim() ?? string.Empty;
        PlayerName = playerName?.Trim() ?? string.Empty;
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
}
