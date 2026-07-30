using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_Seed
{
#region 权威播种入口

    private void PlantAuthoritativeCrop()
    {
        if (item == null || item.Owner == null)
        {
            Debug.LogWarning("[Mod_Seed] 播种失败：物品未被玩家持有", item);
            return;
        }

        if (Camera.main == null)
        {
            Debug.LogWarning("[Mod_Seed] 播种失败：缺少主摄像机", item);
            return;
        }

        Vector3 mouseWorldPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        if (!TryResolveMapByWorldPos(mouseWorldPosition, out Map targetMap) || targetMap.tileMap == null)
        {
            Debug.LogWarning($"[Mod_Seed] 播种失败：目标位置 {mouseWorldPosition} 不在已加载地图中", item);
            return;
        }

        _cachedMap = targetMap;
        Vector3Int cellPosition = targetMap.tileMap.WorldToCell(mouseWorldPosition);
        Vector2Int tilePos = new Vector2Int(cellPosition.x, cellPosition.y);
        if (!TryValidatePlanting(tilePos, out Chunk chunk, out Vector3 worldCenter))
            return;

        Item crop = TryCreateCultivatedCrop(chunk, tilePos, worldCenter, 0f);
        if (crop == null)
            return;

        ConsumeSeedInHand();
        Debug.Log($"[Mod_Seed] 播种成功：{harvestedCropName}，地块={tilePos}，剩余种子={item.itemData.Stack.Amount}", item);

        if (item.itemData.Stack.Amount <= 0)
            item.DestroySelf();
    }

    private bool TryValidatePlanting(Vector2Int tilePos, out Chunk chunk, out Vector3 worldCenter)
    {
        chunk = null;
        worldCenter = default;

        if (_cachedMap == null)
        {
            Debug.LogWarning("[Mod_Seed] 播种失败：目标地图不可用", item);
            return false;
        }

        TileData tileData = _cachedMap.GetTileAt(tilePos, 0);
        if (tileData is not TileData_Farmland farmlandData)
        {
            Debug.LogWarning($"[Mod_Seed] 地块 {tilePos} 不是耕地", item);
            return false;
        }

        farmlandData.NormalizeValues();
        if (farmlandData.waterValue <= 0f)
        {
            Debug.LogWarning($"[Mod_Seed] 地块 {tilePos} 缺水，无法播种", item);
            return false;
        }

        if (farmlandData.Fertility <= 0f)
        {
            Debug.LogWarning($"[Mod_Seed] 地块 {tilePos} 缺肥，无法播种", item);
            return false;
        }

        worldCenter = _cachedMap.tileMap.GetCellCenterWorld(new Vector3Int(tilePos.x, tilePos.y, 0));
        if (ChunkMgr.Instance == null)
        {
            Debug.LogWarning("[Mod_Seed] 播种失败：ChunkMgr 未初始化", item);
            return false;
        }

        ChunkMgr.Instance.GetChunkBy_ItemPosition(worldCenter, out chunk);
        if (chunk == null)
        {
            Debug.LogWarning($"[Mod_Seed] 播种失败：地块 {tilePos} 所在区块不可用", item);
            return false;
        }

        if (HasCropOccupyingTile(chunk, worldCenter))
        {
            Debug.LogWarning($"[Mod_Seed] 地块 {tilePos} 已有种子或作物，不能重复播种", item);
            return false;
        }

        return true;
    }

    private static bool HasCropOccupyingTile(Chunk chunk, Vector3 worldCenter)
    {
        if (!chunk.TryGetItemsByPosition(worldCenter, out List<Item> items) || items == null)
            return false;

        foreach (Item candidate in items)
        {
            if (candidate == null || candidate.itemMods == null)
                continue;

            if (candidate.itemMods.ContainsKey_ID(ModText.Grow))
                return true;

            if (!candidate.itemMods.ContainsKey_ID(ModText.PlantSeed))
                continue;

            Mod_Seed seed = candidate.itemMods.GetMod_ByID(ModText.PlantSeed) as Mod_Seed;
            if (seed != null && seed.Data != null && seed.Data.isPlanted)
                return true;
        }

        return false;
    }

    private Item TryCreateCultivatedCrop(
        Chunk chunk,
        Vector2Int tilePos,
        Vector3 worldCenter,
        float normalizedProgress)
    {
        if (string.IsNullOrWhiteSpace(harvestedCropName))
        {
            Debug.LogError("[Mod_Seed] 作物 Item ID 为空", item);
            return null;
        }

        try
        {
            Item crop = ItemMgr.Instance.InstantiateItem(
                harvestedCropName,
                worldCenter,
                Quaternion.identity,
                Vector3.one,
                chunk != null ? chunk.gameObject : null);
            crop.Load();
            crop.SetInHand(false);
            crop.itemData.Stack.CanBePickedUp = false;

            Mod_Grow grow = crop.itemMods.ContainsKey_ID(ModText.Grow)
                ? crop.itemMods.GetMod_ByID(ModText.Grow) as Mod_Grow
                : null;
            if (grow == null)
            {
                crop.DestroySelf();
                Debug.LogError($"[Mod_Seed] 作物 {harvestedCropName} 缺少权威 Mod_Grow，已取消播种", item);
                return null;
            }

            grow.InitializeCultivatedCrop(tilePos, normalizedProgress);
            return crop;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Mod_Seed] 创建作物 {harvestedCropName} 失败，未消耗种子。\n{exception}", item);
            return null;
        }
    }

#endregion

#region 旧落地种子迁移

    private void MigrateLegacyPlantedSeed()
    {
        if (!Data.isPlanted || item == null)
            return;

        Vector3 worldCenter = new Vector3(Data.plantedTilePos.x + 0.5f, Data.plantedTilePos.y + 0.5f, 0f);
        if (ChunkMgr.Instance == null)
            return;

        ChunkMgr.Instance.GetChunkBy_ItemPosition(worldCenter, out Chunk chunk);
        if (chunk == null || chunk.Map == null)
            return;

        _cachedMap = chunk.Map;
        TileData tileData = _cachedMap.GetTileAt(Data.plantedTilePos, 0);
        if (tileData is not TileData_Farmland)
        {
            Debug.LogWarning($"[Mod_Seed] 旧落地种子的耕地已不存在，保留实例等待恢复，地块={Data.plantedTilePos}", item);
            return;
        }

        float threshold = Mathf.Max(0.01f, Data.growCompletionThreshold);
        float normalizedProgress = Mathf.Clamp01(Data.growProgress / threshold);
        Item crop = TryCreateCultivatedCrop(chunk, Data.plantedTilePos, worldCenter, normalizedProgress);
        if (crop == null)
            return;

        Data.isPlanted = false;
        Debug.Log($"[Mod_Seed] 旧落地种子已迁移到 Mod_Grow，进度={normalizedProgress:P0}", item);
        item.DestroySelf();
    }

#endregion
}
