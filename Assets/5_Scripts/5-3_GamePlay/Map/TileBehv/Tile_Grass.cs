using System.Collections.Generic;
using MemoryPack;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 草地地块逻辑行为
/// 目前主要提供 TileData_Grass 的初始模板，后续可以在此扩展进入 / 退出 / 持续效果。
/// 作为 TileBlockBehaviour 的具体实现，通过组合到 Tile_Block 中使用。
/// </summary>
[System.Serializable]
public class Tile_Grass : TileBlockBehaviour
{

    [Header("进入草地时附加的 Buff（预留，暂未使用）")]
    public List<string> BuffInfo = new List<string>();

    public override void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        // 之后如果有“踩到草地”的特殊效果（如加速、隐藏等），可以在这里实现
    }

    public override void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
    }

    public override void OnUpdate(Item item, TileData tileData, Map map, TileEffectReceiver receiver, float deltaTime)
    {
    }
}

