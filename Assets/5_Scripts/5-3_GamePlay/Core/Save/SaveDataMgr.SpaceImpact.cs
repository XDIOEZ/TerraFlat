using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Combat;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeAddress = FlatWorld.WorldModel.WorldAddress;

public struct SpaceSurfaceImpactResult
{
    #region 结构冲击结果
    public int DamagedBuildings, DestroyedBuildings, DamagedTiles, DestroyedTiles;
    public bool HasChanges => DamagedBuildings > 0 || DamagedTiles > 0;
    #endregion
}

public partial class SaveDataMgr
{
    #region 跨场景地表结构冲击
    // 当前实体、纯机器和冷差量共用一次伤害，结果进入正式存档而不加载星球场景。
    public SpaceSurfaceImpactResult ApplySpaceSurfaceImpact(global::WorldAddress address, Vector2 center,
        float radius, float structuralDamage, bool pressureExplosion = false, ulong eventId = 0)
    {
        var result = new SpaceSurfaceImpactResult();
        if (!GameNetwork.HasStateAuthority || SaveData == null || structuralDamage == 0f) return result;
        if (!address.IsValid || !address.IsSurface) throw new ArgumentException("结构冲击需要真实星球地表地址", nameof(address));
        if (!float.IsFinite(radius) || radius <= 0f || !float.IsFinite(structuralDamage) || structuralDamage < 0f ||
            !float.IsFinite(center.x) || !float.IsFinite(center.y)) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!SaveData.PlanetData_Dict.TryGetValue(address.WorldKey, out PlanetData planet))
            throw new KeyNotFoundException("结构冲击目标星球尚未创建：" + address.PlanetId);
        WorldTopologyDomain domain = WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds) ? bounds.ToDomain() : default;
        float2 canonicalCenter = domain.Normalize(new float2(center.x, center.y));
        center = new Vector2(canonicalCenter.x, canonicalCenter.y);
        bool visible = SceneManager.GetActiveScene().name == address.WorldKey;
        var processed = new HashSet<int>();
        var records = new HashSet<ChunkSaveRecord>();

        using (MachineWorld.UseScope(address.WorldKey))
        {
            var machines = new List<MachineEntity>(); MachineWorld.CollectNodes(machines);
            foreach (MachineEntity node in machines)
            {
                if (node?.Snapshot == null || SpaceSession.Current?.Owns(node.Id) == true) continue;
                float damage = ImpactDamage(node.Snapshot, center, radius, structuralDamage, domain);
                if (damage <= 0f || !processed.Add(node.Id)) continue;
                bool destroyed = false;
                if (!MachineWorld.ApplySurfaceStructuralDamage(node, damage, target =>
                {
                    DestroyImpactMachine(target, address.WorldKey, visible);
                    destroyed = true;
                }, pressureExplosion)) continue;
                result.DamagedBuildings++;
                if (destroyed) result.DestroyedBuildings++;
            }
            MachineWorld.CaptureSurfaceImpactWorld();
        }

        if (visible && ItemMgr.Instance != null)
        {
            foreach (Item item in new List<Item>(ItemMgr.Instance.WorldRunTimeItems.Values))
            {
                if (item == null || item is Player || item.InHand || item.Owner != null || item.PersistenceOwner != null ||
                    item.gameObject.scene.name != address.WorldKey || item.itemData == null ||
                    SpaceSession.Current?.Owns(item.itemData.Guid) == true || processed.Contains(item.itemData.Guid)) continue;
                Mod_Building building = item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
                if (building?.Data?.Role != BuildingRole.PlacedBuilding ||
                    building.CurrentState is not (BuildingState.Installed or BuildingState.Damaged)) continue;
                Vector3 logical = WorldLocalPresentation.ToLogical(item.transform.position);
                float damage = ImpactDamage(item.itemData, center, radius, structuralDamage, domain, logical);
                if (damage <= 0f) continue;
                Mod_DamageReceiver health = item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
                if (health == null || health.Hp <= 0f) continue;
                int guid = item.itemData.Guid; processed.Add(guid);
                bool destroyed = false;
                void DropFatalInventories(Mod_DamageReceiver receiver)
                {
                    destroyed = true;
                    item.Save();
                    DropImpactBuildingInventories(item.itemData, address.WorldKey, logical, true);
                }
                float actual;
                health.DeathStarted += DropFatalInventories;
                try { actual = pressureExplosion ? health.HurtPressureExplosion(eventId, center, damage) : health.ApplyStructuralDamage(damage); }
                finally { health.DeathStarted -= DropFatalInventories; }
                if (actual <= 0f) continue;
                destroyed |= health.Hp <= 0f;
                result.DamagedBuildings++;
                if (destroyed) result.DestroyedBuildings++;
                ChunkSaveRecord record = GetImpactRecord(planet, logical);
                records.Add(record);
                record.ChangedItems.RemoveAll(value => value?.Guid == guid);
                if (destroyed)
                {
                    if (!record.RemovedItemGuids.Contains(guid)) record.RemovedItemGuids.Add(guid);
                }
                else
                {
                    item.Save();
                    ItemData snapshot = CloneItemData(item.itemData);
                    snapshot.transform.position = logical;
                    record.RemovedItemGuids.Remove(guid); record.ChangedItems.Add(snapshot);
                }
            }
        }

        foreach (ChunkSaveRecord record in chunkDeltas.Values.Where(value => value?.PlanetName == address.WorldKey).ToArray())
        {
            foreach (ItemData saved in (record.ChangedItems ?? new List<ItemData>()).ToArray())
            {
                if (saved == null || processed.Contains(saved.Guid) || SpaceSession.Current?.Owns(saved.Guid) == true ||
                    record.RemovedItemGuids?.Contains(saved.Guid) == true || MachineWorld.OwnsWorldItem(saved) ||
                    !TryGetImpactBuilding(saved, out _, out _)) continue;
                float damage = ImpactDamage(saved, center, radius, structuralDamage, domain);
                if (damage <= 0f) continue;
                ItemData cold = CloneAndRebaseItemData(saved);
                if (!TryGetImpactHealth(cold, out Ex_ModData storage, out Mod_DamageReceiver.DamageReceiver_SaveData health)) continue;
                processed.Add(cold.Guid);
                if (health.Hp <= 0f) continue;
                if (pressureExplosion) damage = CombatRules.ResolvePhysical(damage, health.DefenseValues?.Physical ?? 0f);
                if (damage <= 0f) continue;
                health.Hp = Mathf.Max(0f, health.Hp - damage); storage.WriteData(health);
                record.RemovedItemGuids ??= new List<int>();
                records.Add(record); result.DamagedBuildings++;
                if (health.Hp > 0f)
                {
                    Mod_Building.SetMechanicalDamageState(cold, health.Hp < health.MaxHp * .5f);
                    record.ChangedItems.RemoveAll(value => value?.Guid == cold.Guid);
                    record.ChangedItems.Add(cold); record.RemovedItemGuids.Remove(cold.Guid);
                }
                else
                {
                    DestroyColdImpactBuilding(cold, address.WorldKey, visible);
                    record.ChangedItems.RemoveAll(value => value?.Guid == cold.Guid);
                    if (!record.RemovedItemGuids.Contains(cold.Guid)) record.RemovedItemGuids.Add(cold.Guid);
                    result.DestroyedBuildings++;
                }
            }
        }

        ApplyImpactTiles(address.WorldKey, center, radius, structuralDamage, domain, visible, pressureExplosion, records, ref result);
        foreach (ChunkSaveRecord record in records)
        {
            record.ChangedItems?.Sort((left, right) => (left?.Guid ?? 0).CompareTo(right?.Guid ?? 0));
            record.RemovedItemGuids?.Sort(); record.RuntimeTileDeltas?.Sort(CompareRuntimeTileDelta);
        }
        return result;
    }

    public void RecordSpaceLiquidChange(string worldKey, Vector2Int cell, string liquidId, float depth,
        string generatedLiquidId, float generatedDepth)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        if (!SaveData.PlanetData_Dict.TryGetValue(worldKey, out PlanetData planet)) throw new KeyNotFoundException(worldKey);
        if (!float.IsFinite(depth) || depth < 0f || depth > 1f || (depth == 0f) != string.IsNullOrEmpty(liquidId))
            throw new ArgumentOutOfRangeException(nameof(depth));
        ChunkSaveRecord record = GetImpactRecord(planet, new Vector3(cell.x, cell.y, 0f));
        var local = cell - record.ChunkPosition;
        record.LiquidCells ??= new List<LiquidCellSaveData>();
        record.LiquidCells.RemoveAll(value => value.LocalPosition == local);
        if (depth != generatedDepth || !string.Equals(liquidId, generatedLiquidId, StringComparison.Ordinal))
            record.LiquidCells.Add(new LiquidCellSaveData { LocalPosition = local, LiquidId = liquidId, LiquidDepth = depth });
        record.LiquidCells.Sort(CompareLiquidCell);
    }
    #endregion

    #region 建筑生命与离散占地
    private static bool TryGetImpactBuilding(ItemData data, out Ex_ModData storage, out Mod_Building.Building_Data building)
        => Mod_Building.TryReadBuildingData(data, out storage, out building) && building?.Role == BuildingRole.PlacedBuilding &&
           building.State is BuildingState.Installed or BuildingState.Damaged;

    private static bool TryGetImpactHealth(ItemData data, out Ex_ModData storage, out Mod_DamageReceiver.DamageReceiver_SaveData health)
    {
        storage = null; health = null;
        if (data?.ModuleDataDic == null) return false;
        foreach (ModuleData module in data.ModuleDataDic.Values)
        {
            if (module is not Ex_ModData candidate || module.ID != ModText.Hp) continue;
            storage = candidate; health = candidate.GetData<Mod_DamageReceiver.DamageReceiver_SaveData>();
            if (health == null) return false;
            if (!float.IsFinite(health.Hp) || !float.IsFinite(health.MaxHp) || health.MaxHp <= 0f)
                throw new InvalidOperationException("建筑生命快照无效：" + data.IDName);
            return true;
        }
        return false;
    }

    private static float ImpactDamage(ItemData data, Vector2 center, float radius, float peak, WorldTopologyDomain domain, Vector3? position = null)
    {
        if (data?.transform == null) return 0f;
        Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building);
        Vector3 point = position ?? data.transform.position;
        int x0 = Mathf.FloorToInt(point.x), y0 = Mathf.FloorToInt(point.y);
        int width = Mathf.Clamp(building?.FootprintWidth ?? 1, 1, 8), height = Mathf.Clamp(building?.FootprintHeight ?? 1, 1, 8);
        float nearest = float.PositiveInfinity;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            nearest = Mathf.Min(nearest, ImpactCellDistance(center, new Vector2Int(x0 + x, y0 + y), domain));
        return peak * Mathf.Clamp01(1f - nearest / radius);
    }

    private static bool ImpactHitsCell(Vector2 center, Vector2Int cell, float radius, WorldTopologyDomain domain)
        => ImpactCellDistance(center, cell, domain) < radius;

    private static float ImpactCellDistance(Vector2 center, Vector2Int cell, WorldTopologyDomain domain)
    {
        float2 difference = domain.ShortestDelta(new float2(center.x, center.y), new float2(cell.x + .5f, cell.y + .5f));
        float dx = Mathf.Max(0f, Mathf.Abs(difference.x) - .5f), dy = Mathf.Max(0f, Mathf.Abs(difference.y) - .5f);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private ChunkSaveRecord GetImpactRecord(PlanetData planet, Vector3 position)
    {
        Vector2Int size = PlanetData.NormalizeChunkSize(planet.ChunkSize);
        int x = Mathf.FloorToInt(position.x / size.x) * size.x, y = Mathf.FloorToInt(position.y / size.y) * size.y;
        var origin = new Vector2Int(x, y);
        if (WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds)) origin = bounds.NormalizeChunkOrigin(origin);
        string key = BuildChunkKey(planet.Name, origin.ToString());
        if (!chunkDeltas.TryGetValue(key, out ChunkSaveRecord record) || record == null)
            chunkDeltas[key] = record = CreateRuntimeChunkDelta(planet.Name, new RuntimeAddress(global::WorldAddress.SurfaceDimensionId, new Int2(origin.x, origin.y)));
        record.ChangedItems ??= new List<ItemData>(); record.RemovedItemGuids ??= new List<int>();
        return record;
    }
    #endregion

    #region 归零与真实库存掉落
    private void DestroyImpactMachine(MachineEntity node, string worldKey, bool visible)
    {
        Vector2 position = node.Position;
        var inventories = new HashSet<Inventory>();
        if (node.Logic != null) foreach (Inventory inventory in node.Logic.Inventories) inventories.Add(inventory);
        if (node.Processor != null) { inventories.Add(node.Processor.Input); inventories.Add(node.Processor.Output); }
        foreach (Inventory inventory in inventories)
            DropImpactInventory(inventory?.Data, worldKey, position, visible);
        bool ruptured = MachineWorld.HandleFluidTankZeroHp(node);
        if (!ruptured)
        {
            AddImpactMaterials(node.Snapshot, worldKey, position, visible);
            MachineWorld.Remove(node.Id);
        }
        if (visible && node.View?.item != null && !node.View.item.DestructionHandled)
            ItemMgr.Instance.DespawnItem(node.View.item, false);
    }

    private void DestroyColdImpactBuilding(ItemData data, string worldKey, bool visible)
    {
        DropImpactBuildingInventories(data, worldKey, data.transform.position, visible);
        AddImpactMaterials(data, worldKey, data.transform.position, visible);
    }

    private void DropImpactBuildingInventories(ItemData data, string worldKey, Vector2 position, bool visible)
    {
        var visited = new HashSet<Inventory_Data>();
        if (data.ModuleDataDic == null) return;
        foreach (Inventory_ModuleData inventories in data.ModuleDataDic.Values.OfType<Inventory_ModuleData>())
            foreach (Inventory_Data inventory in inventories.Data.Values)
                if (inventory != null && visited.Add(inventory)) DropImpactInventory(inventory, worldKey, position, visible);
    }

    private void DropImpactInventory(Inventory_Data inventory, string worldKey, Vector2 position, bool visible)
    {
        if (inventory?.itemSlots == null) return;
        foreach (ItemSlot slot in inventory.itemSlots)
        {
            if (slot?.itemData?.Stack == null || slot.itemData.Stack.Amount <= 0f) continue;
            AddPersistentImpactDrop(slot.itemData, worldKey, position, visible); slot.itemData = null;
        }
    }

    private void AddImpactMaterials(ItemData placed, string worldKey, Vector2 position, bool visible)
    {
        Mod_Building.TryReadBuildingData(placed, out _, out Mod_Building.Building_Data building);
        string summoner = !string.IsNullOrWhiteSpace(building?.SummonerPrefabId) ? building.SummonerPrefabId : Mod_Building.GetSummonerPrefabId(placed.IDName);
        if (!BuildingMaterialSalvage.TryCreateRandomMaterialSalvage(summoner, out List<ItemData> materials, out string reason))
        { Debug.LogWarning("[地表结构冲击] " + reason); return; }
        foreach (ItemData material in materials) AddPersistentImpactDrop(material, worldKey, position, visible);
    }

    private void AddPersistentImpactDrop(ItemData data, string worldKey, Vector2 position, bool visible)
    {
        if (visible) { DroppedItemService.Spawn(data, position); return; }
        ItemData payload = CloneAndRebaseItemData(data);
        SaveData.DroppedItems ??= new DroppedItemArchive();
        SaveData.DroppedItems.Worlds ??= new Dictionary<string, List<DroppedItemSaveRecord>>();
        if (!SaveData.DroppedItems.Worlds.TryGetValue(worldKey, out List<DroppedItemSaveRecord> drops))
            SaveData.DroppedItems.Worlds[worldKey] = drops = new List<DroppedItemSaveRecord>();
        int guid;
        do { guid = Guid.NewGuid().GetHashCode() & int.MaxValue; }
        while (guid == 0 || ItemMgr.Instance?.GetItemByGuid(guid) != null ||
            SaveData.DroppedItems.Worlds.Values.Any(value => value?.Any(drop => drop?.Data?.Guid == guid) == true));
        payload.Guid = guid; payload.inHand = false; payload.Stack.CanBePickedUp = true;
        payload.transform ??= new ItemTransform(); payload.transform.position = position;
        Vector3 scale = DroppedItemService.ResolveDefaultWorldDropScale(payload); payload.transform.scale = scale;
        drops.Add(new DroppedItemSaveRecord { Data = payload, Position = position, Scale = new Vector2(scale.x, scale.y), Rotation = payload.transform.rotation.eulerAngles.z });
    }
    #endregion

    #region 已加载与冷地块墙体伤害
    private void ApplyImpactTiles(string worldKey, Vector2 center, float radius, float damage, WorldTopologyDomain domain,
        bool visible, bool pressureExplosion, HashSet<ChunkSaveRecord> changedRecords, ref SpaceSurfaceImpactResult result)
    {
        var visited = new HashSet<Vector2Int>();
        ChunkMgr manager = visible ? ChunkMgr.ExistingInstance : null;
        if (manager?.WorldRuntime != null)
        {
            for (int y = Mathf.FloorToInt(center.y - radius); y <= Mathf.FloorToInt(center.y + radius); y++)
            for (int x = Mathf.FloorToInt(center.x - radius); x <= Mathf.FloorToInt(center.x + radius); x++)
            {
                int2 canonical = domain.Normalize(new int2(x, y)); var cell = new Vector2Int(canonical.x, canonical.y);
                if (visited.Contains(cell) || !ImpactHitsCell(center, cell, radius, domain) ||
                    !manager.TryGetRuntimeTerrainTile(new Vector2(cell.x + .5f, cell.y + .5f), out var sample)) continue;
                visited.Add(cell);
                if (!TryGetImpactTileProfile(sample.Cell.BlockingTileId, out RuntimeTileDefinition definition)) continue;
                TileBuildingDamageProfile profile = definition.damageProfile;
                float current = ReadRuntimeBuildingDamage(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y);
                float actual = damage * Mathf.Clamp01(1f - ImpactCellDistance(center, cell, domain) / radius);
                if (pressureExplosion) actual = CombatRules.ResolvePhysical(actual, profile.ResolveDefense().Physical);
                if (actual <= 0f) continue;
                float accumulated = Mathf.Min(profile.MaxHealth, current + actual);
                bool destroyed = accumulated >= profile.MaxHealth;
                if (destroyed)
                {
                    if (!sample.Terrain.TryRemoveBlockingTile(sample.LocalCell.x, sample.LocalCell.y, sample.Cell.BlockingTileId)) continue;
                    WriteRuntimeBuildingDamage(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y, 0f);
                    AddImpactTileDrop(profile, worldKey, cell, true);
                }
                else WriteRuntimeBuildingDamage(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y, accumulated);
                if (manager.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
                    RecordRuntimeTerrainChange(new TileBuildingCell(chunk, cell, sample.LocalCell, sample.Cell.BlockingTileId, definition.Id));
                result.DamagedTiles++; if (destroyed) result.DestroyedTiles++;
            }
        }

        foreach (ChunkSaveRecord record in chunkDeltas.Values.Where(value => value?.PlanetName == worldKey).ToArray())
        foreach (RuntimeTileCellSaveDelta saved in (record.RuntimeTileDeltas ?? new List<RuntimeTileCellSaveDelta>()).ToArray())
        {
            int2 canonical = domain.Normalize(new int2(record.ChunkPosition.x + saved.LocalPosition.x, record.ChunkPosition.y + saved.LocalPosition.y));
            var cell = new Vector2Int(canonical.x, canonical.y);
            if (!visited.Add(cell) || !ImpactHitsCell(center, cell, radius, domain) ||
                !TryGetImpactTileProfile(saved.BlockingTileId, out RuntimeTileDefinition definition)) continue;
            TileBuildingDamageProfile profile = definition.damageProfile;
            float actual = damage * Mathf.Clamp01(1f - ImpactCellDistance(center, cell, domain) / radius);
            if (pressureExplosion) actual = CombatRules.ResolvePhysical(actual, profile.ResolveDefense().Physical);
            if (actual <= 0f) continue;
            saved.AccumulatedDamage = Mathf.Min(profile.MaxHealth, saved.AccumulatedDamage + actual);
            bool destroyed = saved.AccumulatedDamage >= profile.MaxHealth;
            if (destroyed)
            {
                saved.BlockingTileId = 0; saved.Flags &= ~TerrainCellFlags.Blocking; saved.AccumulatedDamage = 0f;
                AddImpactTileDrop(profile, worldKey, cell, visible);
            }
            changedRecords.Add(record); result.DamagedTiles++; if (destroyed) result.DestroyedTiles++;
        }
    }

    private static bool TryGetImpactTileProfile(int tileId, out RuntimeTileDefinition definition)
    {
        definition = null;
        return tileId > 0 && GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetTileDefinition(tileId, out definition) &&
            definition.damageProfile?.Damageable == true && definition.damageProfile.MaxHealth > 0f;
    }

    private void AddImpactTileDrop(TileBuildingDamageProfile profile, string worldKey, Vector2Int cell, bool visible)
    {
        if (string.IsNullOrWhiteSpace(profile.DropItemId) || profile.DropAmount <= 0) return;
        var position = new Vector2(cell.x + .5f, cell.y + .5f);
        if (BuildingMaterialSalvage.IsBuildingSummoner(profile.DropItemId))
        {
            for (int i = 0; i < profile.DropAmount; i++)
            {
                if (!BuildingMaterialSalvage.TryCreateRandomMaterialSalvage(profile.DropItemId, out List<ItemData> materials, out string reason))
                { Debug.LogWarning("[地表墙体冲击] " + reason); break; }
                foreach (ItemData material in materials) AddPersistentImpactDrop(material, worldKey, position, visible);
            }
        }
        else
        {
            ItemData item = GameRes.ExistingInstance.CreateItemData(profile.DropItemId);
            item.Stack.Amount = profile.DropAmount; AddPersistentImpactDrop(item, worldKey, position, visible);
        }
    }
    #endregion
}
