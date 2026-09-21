using System;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 地表覆盖的来源约束；配置在 Tile_Block 上，与阻挡墙和动态建筑分开。
/// 液体要求直接读取独立 Liquid 层，不能通过 TerrainCellFlags 间接表达。
/// </summary>
[Serializable]
public sealed class GroundTilePlacementRule
{
    [Tooltip("原地形必须具备的全部 Ground 标记。")]
    public TerrainCellFlags RequiredSourceFlags = TerrainCellFlags.None;

    [Tooltip("原地形不能具备的任意 Ground 标记。")]
    public TerrainCellFlags ForbiddenSourceFlags = TerrainCellFlags.None;

    [Tooltip("对独立 Liquid 层的要求；平台使用 Liquid，地板使用 Dry。")]
    public GroundLiquidRequirement LiquidRequirement = GroundLiquidRequirement.Any;

    public string RefundItemId; // 主动拆除返还的覆盖物品。
}

/// <summary>地表覆盖对独立液体层的约束。</summary>
public enum GroundLiquidRequirement : byte
{
    Any = 0,
    Dry = 1,
    Liquid = 2
}
