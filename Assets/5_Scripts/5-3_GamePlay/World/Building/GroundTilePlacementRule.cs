using System;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>铺设来源的独立液体条件；不再把液体状态编码进地面标记。</summary>
public enum GroundPlacementLiquidRequirement
{
    Any = 0, // 不限制液体。
    Present = 1, // 必须有液体。
    Absent = 2 // 必须没有液体。
}

/// <summary>
/// 地表覆盖的来源约束；配置在 Tile_Block 上，与阻挡墙和动态建筑分开。
/// 平台要求水面，地板要求可行走且非水的普通地面；覆盖只写独立支撑层，不修改原始地形。
/// </summary>
[Serializable]
public sealed class GroundTilePlacementRule
{
    #region 来源约束
    [Tooltip("原地形必须具备的全部标记；地板要求 Walkable。")]
    public TerrainCellFlags RequiredSourceFlags = TerrainCellFlags.None;

    [Tooltip("原地形不能具备的任意标记。")]
    public TerrainCellFlags ForbiddenSourceFlags = TerrainCellFlags.None;

    [Tooltip("根据独立 LiquidDepth 判断：平台要求 Present，地板要求 Absent。")]
    public GroundPlacementLiquidRequirement LiquidRequirement = GroundPlacementLiquidRequirement.Any;

    public string RefundItemId; // 主动拆除返还的覆盖物品。
    #endregion
}
