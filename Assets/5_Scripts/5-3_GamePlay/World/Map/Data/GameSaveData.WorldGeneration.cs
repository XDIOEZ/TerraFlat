using MemoryPack;
using Sirenix.OdinInspector;

/// <summary>
/// 控制存档是否锁定首次使用时的世界生成配置。
/// Frozen 保持既有确定性基线；FollowCurrent 允许存档跟随当前游戏版本的生成规则。
/// 数值 0 必须保持 Frozen，确保缺少该字段的旧存档仍沿用原有冻结行为。
/// </summary>
public enum WorldGenerationConfigurationMode
{
    Frozen = 0,
    FollowCurrent = 1
}

/// <summary>GameSaveData 的世界生成配置策略。</summary>
public partial class GameSaveData
{
    [ShowInInspector]
    public WorldGenerationConfigurationMode WorldGenerationConfigMode =
        WorldGenerationConfigurationMode.Frozen;

    [MemoryPackIgnore]
    public bool UsesFrozenWorldGenerationConfiguration =>
        WorldGenerationConfigMode == WorldGenerationConfigurationMode.Frozen;
}
