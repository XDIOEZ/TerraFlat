using System;

/// <summary>
/// 地表资源的多次采挖配置。工具品质决定每格需要的右键次数；
/// 工作进度保存在权威区块格子差量，后续 MOD 可复用此规则。
/// </summary>
[Serializable]
public sealed class GroundTileHarvestRule
{
    #region 采挖配置
    public ResourceToolKind RequiredTool = ResourceToolKind.Shovel; // 工具类别。
    public int MinimumTier = 1; // 最低工具等级。
    public float Reach = 2f; // 从玩家到目标地格边缘的最大距离。
    public string ItemId; // 产物定义 ID。
    public int Amount = 1; // 一格产物数量。
    public string ReplacementTileId; // 挖尽后保留的地表定义 ID。
    public int BaseUsesPerTile = 4; // 一级铲子需要的使用次数。
    public int MinimumUsesPerTile = 2; // 高品质工具至少需要的使用次数。
    public float UseInterval = 0.2f; // 两次右键之间的最短间隔。
    #endregion
}
