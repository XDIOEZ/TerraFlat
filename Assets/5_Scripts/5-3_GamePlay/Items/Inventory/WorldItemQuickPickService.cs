using System;
using UnityEngine;

public readonly struct WorldItemQuickPickTarget
{
    #region 世界目标身份
    public readonly string ItemId;
    public readonly string TileBlockId;
    public WorldItemQuickPickTarget(string itemId, string tileBlockId = null)
    { ItemId = itemId; TileBlockId = tileBlockId; }
    public bool IsValid => !string.IsNullOrEmpty(ItemId) || !string.IsNullOrEmpty(TileBlockId);
    #endregion
}

public static class WorldItemQuickPickService
{
    #region 世界格查询预留接口
    public static bool TryResolveWorldCellTarget(Vector2 pointerWorld, Item actor, out WorldItemQuickPickTarget target)
    {
        target = default;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || actor == null || !float.IsFinite(pointerWorld.x) || !float.IsFinite(pointerWorld.y)) return false;
        Vector2 normalized = (Vector2)WorldTopologyRuntime.GetActiveDomain().Normalize(new Unity.Mathematics.float2(pointerWorld.x, pointerWorld.y));
        manager.GetChunkBy_ItemPosition(normalized, out Chunk chunk);
        Item chosen = null;
        if (chunk != null && chunk.TryGetItemsByPosition(normalized, out var items))
        {
            // 没有可选光标时只查鼠标所在格，同格按稳定身份决定回退目标。
            foreach (Item candidate in items)
                if (candidate != null && candidate != actor && !candidate.InHand && candidate.itemData != null &&
                    candidate.gameObject.activeInHierarchy && (chosen == null || candidate.itemData.Guid < chosen.itemData.Guid))
                    chosen = candidate;
        }
        if (chosen != null)
        { target = new WorldItemQuickPickTarget(chosen.itemData.IDName); return true; }
        var cell = new Vector2Int(Mathf.FloorToInt(normalized.x), Mathf.FloorToInt(normalized.y));
        if (FlatWorld.NaturalEntities.NaturalEntityEcsService.TryGetWorldItemDefinitionAtCell(cell, out string naturalId))
        { target = new WorldItemQuickPickTarget(naturalId); return true; }
        if (!manager.TryGetRuntimeTerrainTile(normalized, out RuntimeTerrainTileSample sample)) return false;
        int tileId = sample.Cell.BlockingTileId != 0 ? sample.Cell.BlockingTileId : sample.TopTileId;
        if (GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetTileDefinition(tileId, out var tile)) return false;
        target = new WorldItemQuickPickTarget(null, tile.Id);
        return true;
    }
    #endregion

    #region 库存匹配与正式搬运
    public static bool MatchesWorldTarget(ItemData candidate, WorldItemQuickPickTarget target)
    {
        if (candidate?.Stack == null || candidate.Stack.Amount <= 0f || !target.IsValid) return false;
        if (!string.IsNullOrEmpty(target.ItemId) && string.Equals(candidate.IDName, target.ItemId, StringComparison.Ordinal)) return true;
        if (!Mod_Building.TryReadBuildingData(candidate, out _, out var building) || building.Role != BuildingRole.Summoner) return false;
        return !string.IsNullOrEmpty(target.ItemId) && string.Equals(building.BuildingPrefabId, target.ItemId, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(target.TileBlockId) && string.Equals(building.TileBlockId, target.TileBlockId, StringComparison.Ordinal);
    }

    public static bool TryMoveMatchingBagItemToHotbar(WorldItemQuickPickTarget target, Inventory bag,
        Inventory hotbar, int currentIndex, int usableSlots, out int selectedIndex, out int expectedGuid)
    {
        selectedIndex = -1;
        expectedGuid = 0;
        if (!target.IsValid || hotbar?.Data?.itemSlots == null || !hotbar.CanAccessRuntime || ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction()) return false;
        int count = Mathf.Min(usableSlots, hotbar.Data.itemSlots.Count);
        if (currentIndex < 0 || currentIndex >= count) return false;
        if (!hotbar.IsSlotBeingDragged(currentIndex) && MatchesWorldTarget(hotbar.Data.itemSlots[currentIndex]?.itemData, target))
        { selectedIndex = currentIndex; expectedGuid = hotbar.Data.itemSlots[currentIndex].itemData.Guid; return true; }
        for (int i = 0; i < count; i++)
            if (!hotbar.IsSlotBeingDragged(i) && MatchesWorldTarget(hotbar.Data.itemSlots[i]?.itemData, target))
            { selectedIndex = i; expectedGuid = hotbar.Data.itemSlots[i].itemData.Guid; return true; }
        if (bag?.Data?.itemSlots == null || ReferenceEquals(bag.Data, hotbar.Data)) return false;
        int destination = currentIndex;
        for (int i = 0; i < count; i++)
            if (hotbar.Data.itemSlots[i]?.itemData == null) { destination = i; break; }
        for (int i = 0; i < bag.Data.itemSlots.Count; i++)
        {
            ItemData source = bag.Data.itemSlots[i]?.itemData;
            if (!MatchesWorldTarget(source, target) || bag.IsSlotBeingDragged(i) || hotbar.IsSlotBeingDragged(destination)) continue;
            int guid = source.Guid;
            if (!bag.TryMoveSlotTo(i, hotbar, destination)) continue;
            selectedIndex = destination;
            expectedGuid = MatchesWorldTarget(hotbar.Data.itemSlots[destination]?.itemData, target)
                ? hotbar.Data.itemSlots[destination].itemData.Guid : guid;
            return true;
        }
        return false;
    }
    #endregion
}
