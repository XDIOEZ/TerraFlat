using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public static class ShipGroundBuildingStore
    {
        #region 普通地面建筑引用
        public sealed class Entry
        {
            public Item Item;
            public MachineEntity Machine;
            public ItemData Data;
            public TileBuildingCell? Tile;
            public string WorldKey;
            public Vector2Int Cell;
            public int QuarterTurns;
            public int Guid => Data.Guid;

            // 收集只保留真实引用，被选中的成员才捕获完整库存与模块快照。
            public ItemData Capture()
            {
                if (Tile.HasValue)
                {
                    CaptureTileBuildingHealth(Data, Tile.Value);
                }
                else if (Machine != null)
                {
                    Data = MachineWorld.CaptureSnapshot(Machine) ??
                        throw new InvalidOperationException("待装配机器已离开地面世界。");
                    Cell = Machine.Cell;
                    QuarterTurns = Machine.RotationQuarterTurns;
                }
                else
                {
                    if (Item == null || Item.DestructionHandled)
                        throw new InvalidOperationException("待装配建筑已离开地面世界。");
                    Item.Save();
                    Data = FastCloner.FastCloner.DeepClone(Item.itemData);
                    Vector2 position = WorldLocalPresentation.ToLogical((Vector2)Item.transform.position);
                    Cell = new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
                    QuarterTurns = Mathf.RoundToInt(Item.transform.eulerAngles.z / 90f) & 3;
                }
                return Data;
            }
        }

        public static List<Entry> Collect(string worldKey)
        {
            if (string.IsNullOrWhiteSpace(worldKey)) throw new ArgumentException("地面世界地址为空。", nameof(worldKey));
            using var worldScope = GameplayWorldRegistry.UseWorld(worldKey);
            var result = new List<Entry>();
            ItemMgr items = ItemMgr.Instance;
            if (items != null)
                foreach (Item item in items.WorldRunTimeItems.Values)
                {
                    if (item == null || item.DestructionHandled || item.itemData == null || item.InHand ||
                        item.gameObject.scene.name != worldKey ||
                        item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.IsInstalled() != true ||
                        MachineWorld.OwnsWorldItem(item.itemData)) continue;
                    Vector2 position = WorldLocalPresentation.ToLogical((Vector2)item.transform.position);
                    result.Add(new Entry { Item = item, Data = item.itemData, WorldKey = worldKey,
                        Cell = new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)),
                        QuarterTurns = Mathf.RoundToInt(item.transform.eulerAngles.z / 90f) & 3 });
                }
            using (MachineWorld.UseScope(worldKey))
            {
                var machines = new List<MachineEntity>();
                MachineWorld.CollectNodes(machines);
                foreach (MachineEntity machine in machines)
                    result.Add(new Entry { Machine = machine, Data = machine.Snapshot, WorldKey = worldKey,
                        Cell = machine.Cell, QuarterTurns = machine.RotationQuarterTurns });
            }
            return result;
        }

        // 普通格墙只捕获冷载荷，装船和落地都不为它生成完整 Item。
        public static List<Entry> CollectGroundTileBuildings(string worldKey, IEnumerable<Vector2Int> supportedCells,
            ISet<int> capturedIds = null)
        {
            using var scope = GameplayWorldRegistry.UseWorld(worldKey);
            var result = new List<Entry>();
            capturedIds ??= new HashSet<int>();
            ChunkMgr chunks = ChunkMgr.ExistingInstance;
            foreach (Vector2Int cell in supportedCells)
            {
                if (chunks == null || !chunks.TryGetRuntimeTerrainTile(new Vector2(cell.x + .5f, cell.y + .5f), out var sample) ||
                    sample.Cell.BlockingTileId == 0 || !GameRes.ExistingInstance.TryGetTileDefinition(sample.Cell.BlockingTileId, out var definition) ||
                    string.IsNullOrWhiteSpace(definition.damageProfile?.DropItemId) ||
                    !chunks.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk)) continue;
                if (TryCreateGroundTileEntry(worldKey, cell, sample, chunk, definition,
                        definition.damageProfile.DropItemId, false, capturedIds, out Entry entry)) result.Add(entry);
            }
            return result;
        }

        internal static bool TryGetGroundSupportTileBuilding(string worldKey, Vector2Int cell,
            ISet<int> capturedIds, out Entry entry)
        {
            entry = null;
            ChunkMgr chunks = ChunkMgr.ExistingInstance;
            if (chunks == null || !chunks.TryGetRuntimeTerrainTile((Vector2)cell + Vector2.one * .5f, out var sample))
                return false;
            int id = TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y);
            if (id == 0 || !GameRes.ExistingInstance.TryGetTileDefinition(id, out var definition) ||
                definition.GroundPlacement == null || string.IsNullOrWhiteSpace(definition.GroundPlacement.RefundItemId) ||
                !chunks.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk)) return false;
            return TryCreateGroundTileEntry(worldKey, cell, sample, chunk, definition,
                definition.GroundPlacement.RefundItemId, true, capturedIds, out entry);
        }

        private static bool TryCreateGroundTileEntry(string worldKey, Vector2Int cell, RuntimeTerrainTileSample sample,
            ChunkRuntime chunk, RuntimeTileDefinition definition, string itemId, bool support, ISet<int> capturedIds, out Entry entry)
        {
            entry = null;
            ItemData data = GameRes.ExistingInstance.CreateItemData(itemId);
            if (!Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building) ||
                building.TileBlockId != definition.Id) return false;
            data.Guid = Guid.NewGuid().GetHashCode() & int.MaxValue;
            while (data.Guid == 0 || ItemMgr.GetExistingInstance()?.GetItemByGuid(data.Guid) != null ||
                   SpaceSession.Current?.Owns(data.Guid) == true || !capturedIds.Add(data.Guid))
                data.Guid = Guid.NewGuid().GetHashCode() & int.MaxValue;
            data.transform.position = new Vector3(cell.x + .5f, cell.y + .5f);
            var tile = new TileBuildingCell(chunk, cell, sample.LocalCell, definition.RuntimeTileId, definition.Id,
                support ? (TerrainCell?)sample.Cell : null);
            CaptureTileBuildingHealth(data, tile);
            entry = new Entry { Data = data, Cell = cell, Tile = tile, WorldKey = worldKey };
            return true;
        }

        private static void CaptureTileBuildingHealth(ItemData data, TileBuildingCell tile)
        {
            if (tile.ReplacedGroundCell.HasValue) return;
            float remaining = TileBuildingSystem.TryGetRuntimeDamageFraction(tile.RuntimeChunk, tile.LocalPosition, out float damage)
                ? 1f - damage : 1f;
            foreach (ModuleData module in data.ModuleDataDic.Values)
                if (module.ID == ModText.Hp && module is Ex_ModData storage)
                {
                    var health = storage.GetData<Mod_DamageReceiver.DamageReceiver_SaveData>();
                    if (health == null) continue;
                    health.Hp = health.MaxHp * remaining;
                    storage.WriteData(health);
                    break;
                }
        }

        private static void RestoreTileBuildingHealth(ItemData data, TileBuildingCell tile)
        {
            if (tile.ReplacedGroundCell.HasValue) return;
            float remaining = 1f;
            foreach (ModuleData module in data.ModuleDataDic.Values)
                if (module.ID == ModText.Hp && module is Ex_ModData storage)
                {
                    var health = storage.GetData<Mod_DamageReceiver.DamageReceiver_SaveData>();
                    if (health?.MaxHp > 0f) remaining = Mathf.Clamp01(health.Hp / health.MaxHp);
                    break;
                }
            float maximum = GameRes.ExistingInstance.GetTileBlock(tile.TileBlockId)?.damageProfile?.MaxHealth ?? 0f;
            tile.RuntimeChunk.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
                tile.LocalPosition.x, tile.LocalPosition.y, maximum * (1f - remaining));
            SaveDataMgr.Instance?.RecordRuntimeTerrainChange(tile);
        }
        #endregion

        #region 普通地面建筑创建与卸载
        public static Func<IReadOnlyList<Entry>, bool> NetworkPublisher;

        public static void PublishCreated(IReadOnlyList<Entry> entries)
        {
            var runtimeEntries = new List<Entry>(entries.Count);
            foreach (Entry entry in entries) if (!entry.Tile.HasValue) runtimeEntries.Add(entry);
            if (GameNetwork.IsOnline && GameNetwork.HasStateAuthority &&
                runtimeEntries.Count > 0 && NetworkPublisher?.Invoke(runtimeEntries) != true)
                throw new InvalidOperationException("地面建筑的联机发布器尚未就绪。");
        }

        public static Entry Place(ItemData data, Vector2Int cell, int quarterTurns, string worldKey)
            => PlaceCore(data, cell, quarterTurns, worldKey, null, true);

        public static Entry PlacePrepared(ItemData data, Vector2Int cell, int quarterTurns, string worldKey,
            ItemMgr.PreparedItemHandle prepared)
            => PlaceCore(data, cell, quarterTurns, worldKey, prepared, false);

        private static Entry PlaceCore(ItemData data, Vector2Int cell, int quarterTurns, string worldKey,
            ItemMgr.PreparedItemHandle prepared, bool activate)
        {
            if (!GameNetwork.HasStateAuthority || data == null || data.Guid == 0 ||
                string.IsNullOrWhiteSpace(worldKey)) throw new InvalidOperationException("地面建筑数据无效或没有世界写入权限。");
            using var worldScope = GameplayWorldRegistry.UseWorld(worldKey);
            Scene targetScene = GameplayWorldRegistry.CurrentScene;
            bool machine = MachineCatalog.Get(data.IDName) != null;
            if (!targetScene.IsValid() || !targetScene.isLoaded)
                throw new InvalidOperationException("普通建筑需要在目标地表场景就绪后恢复。");
            quarterTurns &= 3;
            Vector3 logical = new(cell.x + .5f, cell.y + .5f, 0f);
            Quaternion rotation = Quaternion.Euler(0f, 0f, quarterTurns * 90f);
            data.transform ??= new ItemTransform();
            data.transform.position = logical;
            data.transform.rotation = rotation;
            data.transform.scale = Vector3.one;
            data.inHand = false;
            if (data.Stack != null) { data.Stack.Amount = 1f; data.Stack.CanBePickedUp = false; }
            Mod_Building.SetInstalledDataState(data);
            var entry = new Entry { Data = data, Cell = cell, QuarterTurns = quarterTurns, WorldKey = worldKey };
            try
            {
                using var scope = MachineWorld.UseScope(worldKey);
                if (Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data tileBuilding) &&
                    !string.IsNullOrWhiteSpace(tileBuilding.TileBlockId))
                {
                    if (!TileBuildingSystem.TryPlace(logical, tileBuilding.TileBlockId, out TileBuildingCell tile, out string reason))
                        throw new InvalidOperationException("格墙落地失败：" + reason);
                    entry.Tile = tile;
                    RestoreTileBuildingHealth(data, tile);
                }
                else if (machine)
                {
                    if (MachineWorld.GetById(data.Guid) != null)
                        throw new InvalidOperationException("落地机器身份已经被使用：" + data.Guid);
                    MachineState state = MachineWorld.ReadMachineState(data);
                    state.RotationQuarterTurns = quarterTurns;
                    MachineWorld.WriteMachineState(data, state);
                    entry.Machine = MachineWorld.RestoreMachine(data);
                    entry.Data = entry.Machine.Snapshot;
                }
                else
                {
                    if (ItemMgr.Instance == null) throw new InvalidOperationException("物品管理器尚未就绪。");
                    if (prepared != null)
                    {
                        entry.Item = ItemMgr.Instance.CommitPreparedItem(prepared, data,
                            WorldLocalPresentation.ProjectPosition(logical), rotation, Vector3.one, activate: activate);
                    }
                    else
                    {
                        // 提前准备失败时沿用原创建链，最终显示仍等整组所有权移交成功。
                        entry.Item = ItemMgr.Instance.InstantiateItem(data, WorldLocalPresentation.ProjectPosition(logical), rotation, Vector3.one);
                        if (!activate) entry.Item.gameObject.SetActive(false);
                        entry.Item.transform.SetParent(null, true);
                        if (entry.Item.gameObject.scene != targetScene) SceneManager.MoveGameObjectToScene(entry.Item.gameObject, targetScene);
                        entry.Item.Load();
                    }
                    Mod_Building building = entry.Item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building) ??
                        throw new InvalidOperationException("落地成员缺少普通建筑模块：" + data.IDName);
                    building.SetAsInstalled(initializeHealth: false);
                    entry.Item.transform.rotation = rotation;
                    entry.Item.Save();
                    if (activate) BuildingPlacementLifecycle.NotifyCommitted(entry.Item);
                }
                return entry;
            }
            catch
            {
                RollbackCreated(entry);
                throw;
            }
        }

        public static void RevealPrepared(Entry entry)
        {
            if (entry?.Item == null || entry.Item.DestructionHandled) return;
            entry.Item.gameObject.SetActive(true);
            BuildingPlacementLifecycle.NotifyCommitted(entry.Item);
        }

        public static void RemoveItem(Entry entry)
        {
            if (entry?.Machine != null) throw new InvalidOperationException("机器必须通过作用域迁移移交，不能按建筑卸载。");
            if (entry?.Tile is TileBuildingCell tile)
            {
                using var tileScope = GameplayWorldRegistry.UseWorld(entry.WorldKey);
                if (!TileBuildingSystem.TryRemove(tile, false, out string reason))
                    throw new InvalidOperationException("格墙移交失败：" + reason);
                return;
            }
            if (entry?.Item == null || entry.Item.DestructionHandled) return;
            Item item = entry.Item;
            using var worldScope = GameplayWorldRegistry.UseWorld(item.gameObject.scene.name);
            if (ItemMgr.Instance == null) throw new InvalidOperationException("物品管理器尚未就绪。");
            ItemMgr manager = ItemMgr.Instance;
            int guid = entry.Guid;
            try
            {
                SaveDataMgr.Instance?.RemoveRuntimeBuildingRecord(item);
                item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.ReleasePlacementOccupancy();
                manager.DespawnItem(item, saveData: false);
            }
            catch (Exception error)
            {
                // 注销后的外部通知失败不撤销已完成的所有权移交。
                if (item == null || item.DestructionHandled ||
                    !manager.WorldRunTimeItems.TryGetValue(guid, out Item current) || !ReferenceEquals(current, item))
                { Debug.LogException(error); return; }
                SaveDataMgr.Instance?.RecordRuntimeBuildingChange(item);
                throw;
            }
        }

        public static void RollbackCreated(Entry entry)
        {
            if (entry == null) return;
            if (entry.Machine != null)
            {
                using var scope = MachineWorld.UseNodeScope(entry.Machine);
                if (!MachineWorld.DiscardPlacementCandidate(entry.Machine.Id))
                    throw new InvalidOperationException("落地机器候选未能撤销。");
            }
            else RemoveItem(entry);
        }
        #endregion
    }
}
