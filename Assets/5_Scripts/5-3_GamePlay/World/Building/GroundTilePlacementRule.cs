using System;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 地表铺设的来源约束；配置在 Tile_Block 上，与阻挡墙和动态建筑分开。
/// 默认要求原格为水，铺设后使用目标 Tile 的无阻挡地面与当前世界的普通通行代价。
/// </summary>
[Serializable]
public sealed class GroundTilePlacementRule
{
    [Tooltip("原地形必须具备的全部标记；水上平台设为 Water。")]
    public TerrainCellFlags RequiredSourceFlags = TerrainCellFlags.Water;
}
