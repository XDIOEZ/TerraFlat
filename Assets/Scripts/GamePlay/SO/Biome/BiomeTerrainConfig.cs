
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
public class BiomeTerrainConfig
{
    [Tooltip("该生态群系可能包含的地块预制体")]
    [InlineEditor]
    public List<BiomeTileSpawn> TileSpawns;

    [InlineEditor]
    [Tooltip("在该生态群系中可能生成的物品及概率")]
    public List<Biome_ItemSpawn> ItemSpawn = new ();

    [Tooltip("在该生态群系中可能生成的物品及概率")]
    public List<Biome_ItemSpawn_NoSO> ItemSpawn_NoSO     = new ();

    [Tooltip("该生态群系的地形类型（返回 Tile_Block SO）")]
    public Tile_Block GetTilePrefab(EnvironmentFactors env)
    {
        if (TileSpawns == null || TileSpawns.Count == 0)
        {
            Debug.LogError("TileSpawns 列表为空！");
            return null;
        }

        //// 简单版本：使用湿度或降水作为噪声映射来源
        //float noiseValue = Mathf.InverseLerp(0f, 100f, env.Humidity); // 也可以是env.Temperature等

        //// 映射到索引
        //int index = Mathf.FloorToInt(noiseValue * TileData_Prefab.Count);
        //index = Mathf.Clamp(index, 0, TileData_Prefab.Count - 1);

        // TODO: 后续可以根据 env 和 environmentConditionRange 做权重选择
        return TileSpawns[0] != null ? TileSpawns[0].TileBlock : null;
    }
    
    public void OnValidate()
    {
        // 验证TileSpawns列表
        if (TileSpawns != null)
        {
            for (int i = 0; i < TileSpawns.Count; i++)
            {
                if (TileSpawns[i] != null)
                {
                    // 调用BiomeTileSpawn的OnValidate（如果存在）
                }
            }
        }
        
        // 验证ItemSpawn列表
        if (ItemSpawn != null)
        {
            for (int i = 0; i < ItemSpawn.Count; i++)
            {
                if (ItemSpawn[i] != null)
                {
                    // 调用Biome_ItemSpawn的OnValidate（如果存在）
                }
            }
        }
        
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