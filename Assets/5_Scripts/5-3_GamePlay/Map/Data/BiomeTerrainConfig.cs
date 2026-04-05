
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
public class BiomeTerrainConfig
{
    [Tooltip(" 在该生态群系中可能生成的物品及概率")]
    public List<BiomeTileSpawn_NoSo> TileSpawns_NoSO = new();

    [Tooltip("在该生态群系中可能生成的物品及概率")]
    public List<Biome_ItemSpawn_NoSO> ItemSpawn_NoSO = new();

    [Tooltip("该生态群系的地形类型（返回 Tile_Block SO）")]
    public Tile_Block Get_Tile_Block()
    {
        return TileSpawns_NoSO[0].TileBlock;
    }

    public void OnValidate()
    {

        
        // 验证ItemSpawn_NoSO列表
        if (ItemSpawn_NoSO != null)
        {
            for (int i = 0; i < ItemSpawn_NoSO.Count; i++)
            {
                if (ItemSpawn_NoSO[i] != null)
                {
                    ItemSpawn_NoSO[i].OnValidate();
                    // 调用Biome_ItemSpawn_NoSO的OnValidate（如果存在）
                }
            }
        }
    }
}