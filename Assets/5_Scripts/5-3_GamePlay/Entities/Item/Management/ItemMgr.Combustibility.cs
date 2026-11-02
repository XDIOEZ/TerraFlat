using System;
using System.Collections.Generic;
using UnityEngine;

public partial class ItemMgr
{
    #region 物品可燃性来源生命周期
    private sealed class CombustibilityItemSource
    {
        internal string World;
        internal Vector2Int Cell;
        internal RuntimeItemDefinition Definition;
        internal uint Generation;
        internal float Amount;
        internal Action<bool> HandChanged;
        internal ItemData Data;
        internal Action<ItemData> MassChanged;
    }
    private readonly Dictionary<Item, CombustibilityItemSource> combustibilityItems = new();

    private void TrackWorldItemCombustibility(Item item)
    {
        if (item != null && item.IsRuntimeTransitioning) return;
        if (item == null || item.itemData == null || !item.IsInitialized || item.IsInPool || item.DestructionHandled ||
            GameRes.ExistingInstance?.TryGetItemDefinition(item.itemData.IDName, out var definition) != true ||
            !definition.FireFuel.IsCombustible)
        { ReleaseWorldItemCombustibility(item); return; }
        string world = GameplayWorldRegistry.GetItemWorldKey(item);
        if (world == null) return;
        Vector2Int cell = Vector2Int.FloorToInt(WorldLocalPresentation.ToLogical(item.transform.position));
        if (!combustibilityItems.TryGetValue(item, out var source))
        {
            source = new CombustibilityItemSource();
            source.HandChanged = _ => TrackWorldItemCombustibility(item);
            source.MassChanged = _ => TrackWorldItemCombustibility(item);
            item.OnInHandChanged += source.HandChanged;
            combustibilityItems.Add(item, source);
        }
        if (!ReferenceEquals(source.Data, item.itemData))
        {
            if (source.Data != null) source.Data.MassChanged -= source.MassChanged;
            source.Data = item.itemData; source.Data.MassChanged += source.MassChanged;
        }
        if (item.Owner != null || item.InHand || !item.gameObject.activeInHierarchy ||
            item.itemMods.GetMod_ByID<Mod_MechanicalNode>(Mod_MechanicalNode.ModuleId) != null)
        {
            if (source.World != null) WorldCombustibilitySystem.RemoveCombustibilitySource(source.World, item);
            source.World = null; source.Definition = null;
            return;
        }
        float amount = item.itemData.Stack?.Amount ?? 1f;
        Mod_Building building = item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building);
        var footprint = building != null ? BuildingOccupancyRegistry.GetRegisteredFootprintCells(building) : null;
        if (source.World != null && source.World != world) WorldCombustibilitySystem.RemoveCombustibilitySource(source.World, item);
        if (source.World == world && source.Cell == cell && ReferenceEquals(source.Definition, definition) &&
            source.Generation == item.RuntimeGeneration && source.Amount == amount && footprint == null) return;
        var cells = footprint ?? new HashSet<Vector2Int>();
        if (footprint == null)
        {
            foreach (var offset in definition.WorldGridOccupancy) cells.Add(cell + new Vector2Int(offset.X, offset.Y));
            if (cells.Count == 0) cells.Add(cell);
        }
        source.World = world; source.Cell = cell; source.Definition = definition; source.Generation = item.RuntimeGeneration; source.Amount = amount;
        WorldCombustibilitySystem.ReplaceCombustibilitySource(world, item, cells,
            definition.FireFuel.Flammability, definition.FireFuel.BurnDurationSeconds * amount);
    }

    private void ReleaseWorldItemCombustibility(Item item)
    {
        if (ReferenceEquals(item, null) || !combustibilityItems.Remove(item, out var source)) return;
        item.OnInHandChanged -= source.HandChanged;
        if (source.Data != null) source.Data.MassChanged -= source.MassChanged;
        if (source.World != null) WorldCombustibilitySystem.RemoveCombustibilitySource(source.World, item);
    }

    private void ClearWorldItemCombustibility()
    {
        foreach (var pair in combustibilityItems)
        {
            pair.Key.OnInHandChanged -= pair.Value.HandChanged;
            if (pair.Value.Data != null) pair.Value.Data.MassChanged -= pair.Value.MassChanged;
            if (pair.Value.World != null) WorldCombustibilitySystem.RemoveCombustibilitySource(pair.Value.World, pair.Key, preserveFuelConsumption: true);
        }
        combustibilityItems.Clear();
    }
    #endregion
}
