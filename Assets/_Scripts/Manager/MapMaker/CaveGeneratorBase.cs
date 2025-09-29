// CaveGeneratorBase.cs
using Force.DeepCloner;
using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class CaveGeneratorBase : ScriptableObject
{
    [Header("��������")]
    [Tooltip("ÿ֡���ɵ������Ƭ�� (0=��������)")]
    public int tilesPerFrame = 10;
    
    [Header("�߽�����")]
    public bool generateEdges = true;

    // ���󷽷����������ʵ��
    public abstract void GenerateCave(Map map, System.Random rng);
    
    // ��ѡ���鷽�������������д
    public virtual void OnGenerationComplete(Map map)
    {
        map.tileMap?.RefreshAllTiles();
        map.Data.TileLoaded = true;
        map.BackTilePenalty_Async();
    }
    
    // ��ѡ���鷽�������������д
    public virtual void ClearMap(Map map)
    {
        map.tileMap?.ClearAllTiles();
        map.Data.TileData?.Clear();
    }
    
    // ���߷���������������ʹ��
    protected void PlaceTile(Map map, Dictionary<string, TileData> tileDataCache, Vector2Int position, string tileName)
    {
        // ����TileData�����ظ�����
        if (!tileDataCache.ContainsKey(tileName))
        {
            var prefab = GameRes.Instance.GetPrefab(tileName);
            if (prefab == null)
            {
                Debug.LogError($"�޷���ȡԤ����: {tileName}");
                return;
            }

            var blockTile = prefab.GetComponent<IBlockTile>();
            if (blockTile == null)
            {
                Debug.LogError($"Ԥ���� {tileName} ȱ�� IBlockTile ���");
                return;
            }

            tileDataCache[tileName] = blockTile.TileData;
        }

        // ��¡�����Tile����ͼ
        var tile = tileDataCache[tileName].DeepClone();
        tile.position = new Vector3Int(position.x, position.y, 0);
        map.ADDTile(position, tile);
    }
    
    protected virtual Item SpawnItem(string itemName, Vector2Int position, GameObject parentObject)
    {
        Vector2 spawnPos = new Vector2(position.x + 0.5f, position.y + 0.5f);
        
        // ʵ������Դ��Ʒ
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
            // ���ݵ�����ʾ����Ƿ�����
            if (rng.NextDouble() < loot.DropChance)
            {
                // �����������
                int amount = rng.Next(loot.MinAmount, loot.MaxAmount + 1);
                
                for (int i = 0; i < amount; i++)
                {
                    SpawnItem(loot.LootPrefabName, position, map.ParentObject);
                }
            }
        }
    }
}