using System;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Gameplay.Building;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 船内普通物品生命周期
        private readonly HashSet<int> releasingShipInteriorItems = new();
        private bool shipInteriorItemEventsBound;
        // 真实物品由全局账本持有，船件只按身份查找，不另存一套运行实例。
        public bool TryGetShipInteriorItem(string pieceId, out Item item)
        {
            item = null;
            if (!int.TryParse(pieceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ||
                !FindPiece(pieceId, out ShipState ship, out _) || ItemMgr.GetExistingInstance() is not ItemMgr manager ||
                !manager.WorldRunTimeItems.TryGetValue(id, out Item found) || found == null || found.DestructionHandled ||
                GameplayWorldRegistry.GetItemWorldKey(found) != MachineWorld.GetShipScopeKey(ship.ShipId)) return false;
            item = found;
            return true;
        }

        internal Item EnsureShipInteriorItem(ShipState ship, ShipPieceState piece, ItemData data)
        {
            if (ship == null || piece == null || data == null) throw new ArgumentNullException();
            if (MachineWorld.OwnsWorldItem(data) ||
                Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data buildingData) &&
                !string.IsNullOrWhiteSpace(buildingData.TileBlockId)) return null;
            if (TryGetShipInteriorItem(piece.PieceId, out Item existing)) return existing;
            ItemMgr manager = ItemMgr.GetExistingInstance() ?? throw new InvalidOperationException("物品管理器尚未就绪。");
            if (manager.WorldRunTimeItems.ContainsKey(data.Guid))
                throw new InvalidOperationException("船内物品身份已被其他世界使用：" + data.Guid);
            var world = EnsureShipInteriorWorld(ship);
            Vector3 position = ShipInteriorPiecePosition(ship, piece);
            Quaternion rotation = Quaternion.Euler(0f, 0f, piece.QuarterTurns * 90f);
            using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
            Item created = null;
            try
            {
                data.transform ??= new ItemTransform();
                data.transform.position = position;
                data.transform.rotation = rotation;
                created = manager.InstantiateItem(data, position, rotation, Vector3.one);
                created.PersistenceOwner = this;
                created.Load();
                Mod_Building building = created.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)
                    ?? throw new InvalidOperationException("船内物品缺少普通建筑模块：" + data.IDName);
                building.SetAsInstalled(initializeHealth: false);
                BuildingPlacementLifecycle.NotifyCommitted(created);
                created.Save();
                contents[piece.PieceId] = created.itemData;
                BindShipInteriorItemCallbacks(created, ship, piece);
                return created;
            }
            catch
            {
                if (created != null && !created.DestructionHandled) manager.DespawnItem(created, false);
                throw;
            }
        }

        internal void TransferExistingItemToShipInterior(ShipState ship, ShipPieceState piece, Item item)
        {
            var world = EnsureShipInteriorWorld(ship);
            TransferInstalledItemToWorld(item, world.WorldKey, ShipInteriorPiecePosition(ship, piece),
                piece.QuarterTurns, this);
            contents[piece.PieceId] = item.itemData;
            BindShipInteriorItemCallbacks(item, ship, piece);
        }

        internal Item TransferShipInteriorItemToWorld(ShipState ship, ShipPieceState piece, string worldKey,
            Vector2Int cell, int quarterTurns)
        {
            if (!TryGetShipInteriorItem(piece.PieceId, out Item item)) return null;
            TransferInstalledItemToWorld(item, worldKey, new Vector3(cell.x + .5f, cell.y + .5f), quarterTurns, null);
            UnbindShipInteriorItemCallbacks(item);
            contents[piece.PieceId] = item.itemData;
            return item;
        }

        internal void TransferShipInteriorItemsToFragment(ShipState source, ShipState fragment)
        {
            EnsureShipInteriorWorld(fragment);
            foreach (ShipPieceState piece in fragment.Pieces)
            {
                if (!int.TryParse(piece.PieceId, out int id) || ItemMgr.GetExistingInstance() is not ItemMgr manager ||
                    !manager.WorldRunTimeItems.TryGetValue(id, out Item item) || item == null ||
                    GameplayWorldRegistry.GetItemWorldKey(item) != MachineWorld.GetShipScopeKey(source.ShipId)) continue;
                TransferExistingItemToShipInterior(fragment, piece, item);
            }
        }

        internal void ReleaseShipInteriorItem(ShipPieceState piece, bool capture)
        {
            if (piece == null || !int.TryParse(piece.PieceId, out int id) ||
                ItemMgr.GetExistingInstance() is not ItemMgr manager ||
                !manager.WorldRunTimeItems.TryGetValue(id, out Item item) || item == null ||
                !ReferenceEquals(item.PersistenceOwner, this)) return;
            string world = GameplayWorldRegistry.GetItemWorldKey(item);
            using var scope = GameplayWorldRegistry.TryGet(world, out _) ? GameplayWorldRegistry.UseWorld(world) : default;
            if (capture)
            {
                item.Save();
                contents[piece.PieceId] = item.itemData;
            }
            releasingShipInteriorItems.Add(id);
            try { UnbindShipInteriorItemCallbacks(item); manager.DespawnItem(item, false); }
            finally { releasingShipInteriorItems.Remove(id); }
        }

        internal void RestoreAllShipInteriorItems()
        {
            if (State == null) return;
            foreach (ShipState ship in State.Ships)
            {
                if (ship.IsWreck) continue;
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece.IsAlive && contents.TryGetValue(piece.PieceId, out ItemData data))
                        EnsureShipInteriorItem(ship, piece, data);
            }
        }

        internal void ReleaseAllShipInteriorItems()
        {
            bool capture = owner != null && SaveDataMgr.Instance?.SaveData == owner;
            foreach (ShipInteriorWorldRuntime world in shipInteriorWorlds.Values)
                ReleaseShipInteriorWorldItems(world.WorldKey, capture);
            if (shipInteriorItemEventsBound) ItemMgr.RuntimeItemDespawning -= OnShipInteriorItemDespawning;
            shipInteriorItemEventsBound = false;
        }

        // 先让世界居民和建筑进入普通存档，再按完整世界账本卸载实例。
        internal void ReleaseShipInteriorWorldItems(string worldKey, bool capture)
        {
            if (!GameplayWorldRegistry.TryGet(worldKey, out _)) return;
            ItemMgr manager = ItemMgr.GetExistingInstance();
            if (manager == null)
            {
                if (GameplayWorldRegistry.GetWorldItemCount(worldKey) != 0)
                    throw new InvalidOperationException("世界仍有物品但物品管理器已卸载：" + worldKey);
                return;
            }
            var items = new List<Item>();
            using var scope = GameplayWorldRegistry.UseWorld(worldKey);
            GameplayWorldRegistry.CopyWorldItems(worldKey, items);
            foreach (Item item in items)
            {
                if (item == null || item.DestructionHandled) continue;
                if (item is Player)
                    throw new InvalidOperationException("乘员尚未离开待卸载的船内世界：" + worldKey);
                if (!capture || !FlatWorld.Networking.GameNetwork.HasStateAuthority || item.PersistenceOwner != null ||
                    item.Owner != null || item.InHand || item.itemData?.Stack?.CanBePickedUp != true ||
                    manager.IsRuntimePersistentWorldEntity(item)) continue;
                manager.SetRuntimeWorldResidentPersistence(item, true);
            }
            if (capture && FlatWorld.Networking.GameNetwork.HasStateAuthority &&
                SaveDataMgr.Instance is SaveDataMgr save && save.SaveData == owner)
            {
                save.CaptureRuntimeLocalWorldState(worldKey);
                if (!save.PrepareRuntimeWorldForSceneUnbinding(worldKey))
                    throw new InvalidOperationException("船内居民保存失败：" + worldKey);
            }
            foreach (ShipInteriorWorldRuntime world in shipInteriorWorlds.Values)
                if (world.WorldKey == worldKey) { world.UnbindAllChunkViews(); break; }
            GameplayWorldRegistry.CopyWorldItems(worldKey, items);
            foreach (Item item in items)
            {
                if (item == null || item.DestructionHandled) continue;
                if (capture && item.PersistenceOwner != null && !ReferenceEquals(item.PersistenceOwner, this))
                    throw new InvalidOperationException("船内物品仍由其他宿主持有：" + item.name);
                int id = item.itemData.Guid;
                releasingShipInteriorItems.Add(id);
                try { UnbindShipInteriorItemCallbacks(item); manager.DespawnItem(item, false); }
                finally { releasingShipInteriorItems.Remove(id); }
            }
        }

        private void BindShipInteriorItemCallbacks(Item item, ShipState ship, ShipPieceState piece)
        {
            if (!shipInteriorItemEventsBound)
            {
                ItemMgr.RuntimeItemDespawning += OnShipInteriorItemDespawning;
                shipInteriorItemEventsBound = true;
            }
            Mod_DamageReceiver health = item.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            if (health != null)
            {
                health.OnDamageReceived -= OnShipInteriorItemDamaged;
                health.OnDamageReceived += OnShipInteriorItemDamaged;
                piece.Health = health.Hp;
                piece.MaxHealth = health.MaxHp;
            }
            Mod_Door door = item.GetComponentInChildren<Mod_Door>(true);
            if (door == null) return;
            door.StateChanged -= OnShipInteriorDoorStateChanged;
            door.StateChanged += OnShipInteriorDoorStateChanged;
            piece.DoorOpen = door.Data.IsOpen;
            RefreshShipPassageGeometry(ship);
        }

        private void UnbindShipInteriorItemCallbacks(Item item)
        {
            Mod_DamageReceiver health = item?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            if (health != null) health.OnDamageReceived -= OnShipInteriorItemDamaged;
            Mod_Door door = item != null ? item.GetComponentInChildren<Mod_Door>(true) : null;
            if (door != null) door.StateChanged -= OnShipInteriorDoorStateChanged;
        }

        private void OnShipInteriorItemDamaged(DamageReceiverDamageInfo damage)
        {
            Item item = damage?.ReceiverItem;
            if (item?.itemData == null || !FindPiece(item.itemData.Guid.ToString(CultureInfo.InvariantCulture),
                    out _, out ShipPieceState piece)) return;
            piece.Health = damage.HpAfter;
            piece.MaxHealth = damage.Receiver.MaxHp;
            if (piece.MaxDisableHealth > 0d) piece.DisableHealth -= damage.DamageValue;
        }

        private double? TryApplyShipInteriorItemDamage(ShipState ship, ShipPieceState piece, double damage, string reason)
        {
            if (!TryGetShipInteriorItem(piece.PieceId, out Item item)) return null;
            Mod_DamageReceiver health = item.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)
                ?? throw new InvalidOperationException("船内建筑缺少正式生命模块：" + piece.ItemId);
            using var scope = GameplayWorldRegistry.UseWorld(GameplayWorldRegistry.GetItemWorldKey(item));
            float loss = health.ApplyStructuralDamage((float)Math.Min(float.MaxValue, damage));
            if (loss > 0f && reason != "攻击") QueueShipDamageFeedback(ship, piece, loss, reason);
            return Math.Max(0f, loss);
        }

        private void OnShipInteriorDoorStateChanged(Mod_Door door, bool open)
        {
            Item item = door?.item;
            if (item?.itemData == null || !FindPiece(item.itemData.Guid.ToString(CultureInfo.InvariantCulture),
                    out ShipState ship, out ShipPieceState piece)) return;
            piece.DoorOpen = open;
            RefreshShipPassageGeometry(ship);
        }

        private void OnShipInteriorItemDespawning(Item item)
        {
            if (item?.itemData == null || !ReferenceEquals(item.PersistenceOwner, this) || releasingShipInteriorItems.Contains(item.itemData.Guid)) return;
            string pieceId = item.itemData.Guid.ToString(CultureInfo.InvariantCulture);
            // 死亡回调时生命已归零，按实际世界查结构，不能使用只查询活部件的索引。
            string worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
            ShipState ship = worldKey != null && worldKey.StartsWith("ship:", StringComparison.Ordinal)
                ? GetShip(worldKey.Substring(5)) : null;
            ShipPieceState piece = ship?.Pieces.Find(candidate => candidate.PieceId == pieceId);
            if (piece == null) return;
            UnbindShipInteriorItemCallbacks(item);
            piece.Destroyed = true;
            piece.Health = 0d;
            ship.NotifyStructureChanged();
            if (piece.Kind == ShipPieceKind.Floor || piece.SealsAtmosphere) ship.AtmosphereStructureDirty = true;
            contents.Remove(pieceId);
            State.Devices.RemoveAll(value => value.PieceId == pieceId);
            if (!destroyed.Contains(pieceId)) destroyed.Add(pieceId);
            pieceIndexDirty = viewBindingsDirty = true;
            RemovePieceView(pieceId);
        }

        private static Vector3 ShipInteriorPiecePosition(ShipState ship, ShipPieceState piece) =>
            new((float)((piece.CellX + .5d) * ship.CellSizeMeters),
                (float)((piece.CellY + .5d) * ship.CellSizeMeters));

        // 整个交接保留同一实例，失败时恢复原世界、位置和占地。
        private static void TransferInstalledItemToWorld(Item item, string targetWorld, Vector3 position,
            int quarterTurns, object targetPersistenceOwner)
        {
            if (item == null || item.DestructionHandled || item.itemData == null)
                throw new InvalidOperationException("待移交建筑已失效。");
            string sourceWorld = GameplayWorldRegistry.GetItemWorldKey(item);
            if (string.IsNullOrWhiteSpace(sourceWorld) || !GameplayWorldRegistry.TryGet(targetWorld, out _))
                throw new InvalidOperationException("建筑移交的源或目标世界尚未就绪。");
            Mod_Building building = item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)
                ?? throw new InvalidOperationException("待移交物品缺少普通建筑模块。");
            Vector3 previousPosition = item.transform.position;
            Quaternion previousRotation = item.transform.rotation;
            object previousOwner = item.PersistenceOwner;
            using (GameplayWorldRegistry.UseWorld(sourceWorld))
            {
                item.Save();
                if (previousOwner == null) SaveDataMgr.Instance?.RemoveRuntimeBuildingRecord(item);
                building.ReleasePlacementOccupancy();
            }
            try
            {
                item.PersistenceOwner = targetPersistenceOwner;
                GameplayWorldRegistry.AssignItemToWorld(item, targetWorld);
                using (GameplayWorldRegistry.UseWorld(targetWorld))
                    PlaceTransferredInstalledItem(item, building, position, quarterTurns);
            }
            catch (Exception error)
            {
                try
                {
                    building.ReleasePlacementOccupancy();
                    GameplayWorldRegistry.AssignItemToWorld(item, sourceWorld);
                    item.PersistenceOwner = previousOwner;
                    using (GameplayWorldRegistry.UseWorld(sourceWorld))
                        PlaceTransferredInstalledItem(item, building, previousPosition,
                            Mathf.RoundToInt(previousRotation.eulerAngles.z / 90f));
                }
                catch (Exception rollback) { throw new AggregateException("建筑世界移交及回滚均失败。", error, rollback); }
                throw;
            }
        }

        private static void PlaceTransferredInstalledItem(Item item, Mod_Building building, Vector3 position, int quarterTurns)
        {
            Quaternion rotation = Quaternion.Euler(0f, 0f, (quarterTurns & 3) * 90f);
            item.transform.SetPositionAndRotation(position, rotation);
            item.itemData.transform.position = position;
            item.itemData.transform.rotation = rotation;
            building.SetAsInstalled(initializeHealth: false);
            BuildingPlacementLifecycle.NotifyCommitted(item);
            item.Save();
            ItemMgr.GetExistingInstance()?.NotifyRuntimeItemMoved(item);
            if (item.PersistenceOwner == null) SaveDataMgr.Instance?.RecordRuntimeBuildingChange(item);
        }
        #endregion
    }
}
