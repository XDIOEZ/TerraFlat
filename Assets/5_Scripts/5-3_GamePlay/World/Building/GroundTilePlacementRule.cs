using System;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 地表覆盖的来源约束；配置在 Tile_Block 上，与阻挡墙和动态建筑分开。
/// 平台要求水面，地板要求可行走且非水的普通地面；覆盖只写独立支撑层，不修改原始地形。
/// </summary>
[Serializable]
public sealed class GroundTilePlacementRule
{
    [Tooltip("原地形必须具备的全部标记；水上平台设为 Water。")]
    public TerrainCellFlags RequiredSourceFlags = TerrainCellFlags.Water;

    [Tooltip("原地形不能具备的任意标记；地板使用 Water，避免被铺到水面。")]
    public TerrainCellFlags ForbiddenSourceFlags = TerrainCellFlags.None;

    public string RefundItemId; // 主动拆除返还的覆盖物品。
}
