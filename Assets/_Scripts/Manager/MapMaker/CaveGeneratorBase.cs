// CaveGeneratorBase.cs
using Force.DeepCloner;
using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class CaveGeneratorBase : ScriptableObject
{
    [Header("基础配置")]
    [Tooltip("每帧生成的最大瓦片数 (0=立即生成)")]
    public int tilesPerFrame = 10;
    
    [Header("边界连接")]
    public bool generateEdges = true;

    // 抽象方法，子类必须实现
    public abstract void GenerateCave(Map map, System.Random rng);
    
    // 可选的虚方法，子类可以重写
    public virtual void OnGenerationComplete(Map map)
    {
        map.tileMap?.RefreshAllTiles();
        map.Data.TileLoaded = true;
        map.BackTilePenalty_Async();
    }
    
    // 可选的虚方法，子类可以重写
    public virtual void ClearMap(Map map)
    {
        map.tileMap?.ClearAllTiles();
        map.Data.TileData?.Clear();
    }
    
    // 工具方法，可在子类中使用
    protected void PlaceTile(Map map, Dictionary<string, TileData> tileDataCache, Vector2Int position, string tileName)
    {
        // 缓存TileData避免重复加载
        if (!tileDataCache.ContainsKey(tileName))
        {
            var prefab = GameRes.Instance.GetPrefab(tileName);
            if (prefab == null)
            {
                Debug.LogError($"无法获取预制体: {tileName}");
                return;
            }

            var blockTile = prefab.GetComponent<IBlockTile>();
            if (blockTile == null)
            {
                Debug.LogError($"预制体 {tileName} 缺少 IBlockTile 组件");
                return;
            }

            tileDataCache[tileName] = blockTile.TileData;
        }

        // 克隆并添加Tile到地图
        var tile = tileDataCache[tileName].DeepClone();
        tile.position = new Vector3Int(position.x, position.y, 0);
        map.ADDTile(position, tile);
    }
    
    protected virtual Item SpawnItem(string itemName, Vector2Int position, GameObject parentObject)
    {
        Vector2 spawnPos = new Vector2(position.x + 0.5f, position.y + 0.5f);
        
        // 实例化资源物品
       return ItemMgr.Instance.InstantiateItem(
            itemName,
            spawnPos,
            default,
            default,
            parentObject
        );
    }
    
    protected void SpawnLootItems(Map map, System.Random rng, List<LootEntry> lootEntries, Vector2Int position)
    {
        foreach (var loot in lootEntries)
        {
            // 根据掉落概率决定是否生成
            if (rng.NextDouble() < loot.DropChance)
            {
                // 随机生成数量
                int amount = rng.Next(loot.MinAmount, loot.MaxAmount + 1);
                
                for (int i = 0; i < amount; i++)
                {
                    SpawnItem(loot.LootPrefabName, position, map.ParentObject);
                }
            }
        }
    }
}