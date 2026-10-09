using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 结构伤害、库存移交与唯一爆炸
        public void SynchronizePieceHealth(ShipState ship, ShipPieceState piece)
        {
            if (int.TryParse(piece.PieceId, out int id) && MachineCatalog.Get(piece.ItemId) != null)
            using (MachineWorld.UseScope("ship:" + ship.ShipId))
            {
                MachineEntity node = MachineWorld.GetById(id);
                if (node?.State != null) { node.State.Hp = (float)piece.Health; MachineWorld.StateChanged(node); }
            }
        }
        public bool ApplyPieceDamage(int guid, float damage, string reason)
        {
            if (!FindPiece(guid.ToString(System.Globalization.CultureInfo.InvariantCulture), out ShipState ship, out ShipPieceState piece)) return false;
            ShipStructureService.ApplyDamage(ship, piece.PieceId, damage, reason, DestroyPiece);
            SynchronizePieceHealth(ship, piece); return true;
        }
        public void RecoverUnsupportedPiece(ShipState ship, ShipPieceState piece, string reason)
        {
            if (contents.TryGetValue(piece.PieceId, out ItemData data))
            {
                using (MachineWorld.UseScope("ship:" + ship.ShipId))
                {
                    if (int.TryParse(piece.PieceId, out int id) && MachineWorld.GetById(id) is MachineEntity node)
                        data = MachineWorld.RemoveShipPieceForRecovery(node);
                }
                CaptureRecoveredPieceHealth(data, piece);
                QueueDrop(Mod_Building.CreateRecoveredCarrier(data, Mod_Building.GetSummonerPrefabId(piece.ItemId)), ship, piece);
                contents.Remove(piece.PieceId);
            }
            State.Devices.RemoveAll(value => value.PieceId == piece.PieceId);
            RemovePieceView(piece.PieceId);
        }
        private void HandleDestroyedInventory(ShipState ship, ShipPieceState piece, string reason)
        {
            ItemData snapshot = GetPieceData(piece);
            using (MachineWorld.UseScope("ship:" + ship.ShipId))
            {
                if (int.TryParse(piece.PieceId, out int id) && MachineWorld.GetById(id) is MachineEntity node)
                {
                    MachineWorld.WakeForInteraction(node);
                    if (node.Logic != null)
                        foreach (Inventory inventory in node.Logic.Inventories)
                        foreach (ItemSlot slot in inventory.Data.itemSlots)
                        {
                            if (slot?.itemData == null || slot.itemData.Stack?.Amount <= 0f) continue;
                            QueueDrop(slot.itemData, ship, piece); slot.itemData = null;
                        }
                    node.State.Hp = 0f;
                    if (MachineWorld.HandleFluidTankZeroHp(node))
                        snapshot = FastCloner.FastCloner.DeepClone(node.Snapshot);
                    else
                    {
                        DrainDestroyedFluidChambers(ship, piece, node);
                        snapshot = MachineWorld.Remove(id);
                    }
                }
            }
            ShipDeviceState device = GetDevice(piece.PieceId);
            if (snapshot != null) CaptureRecoveredPieceHealth(snapshot, piece);
            if (snapshot != null && !string.IsNullOrWhiteSpace(device?.Configuration.RecoveryItemId))
                QueueDrop(Mod_Building.CreateRecoveredCarrier(snapshot, device.Configuration.RecoveryItemId), ship, piece);
            else QueueMaterialSalvage(ship, piece);
            contents.Remove(piece.PieceId);
            State.Devices.RemoveAll(value => value.PieceId == piece.PieceId);
        }
        private static void CaptureRecoveredPieceHealth(ItemData snapshot, ShipPieceState piece)
        {
            if (snapshot?.ModuleDataDic == null) return;
            foreach (ModuleData module in snapshot.ModuleDataDic.Values)
            {
                if (module.ID != ModText.Hp || module is not Ex_ModData storage) continue;
                Mod_DamageReceiver.DamageReceiver_SaveData health = storage.GetData<Mod_DamageReceiver.DamageReceiver_SaveData>();
                if (health == null) return;
                health.Hp = (float)Math.Max(0d, piece.Health); health.MaxHp = (float)piece.MaxHealth;
                storage.WriteData(health); return;
            }
        }
        private void DrainDestroyedFluidChambers(ShipState ship, ShipPieceState piece, MachineEntity node)
        {
            FluidMachineState state = MachineWorld.GetFluidState(node);
            if (state == null || state.Ruptured) return;
            state.Ruptured = true;
            var materials = new List<FluidBatch>();
            var drained = new HashSet<FluidInventoryState>();
            double pressureEnergy = 0d;
            foreach (string chamber in new List<string>(state.Chambers.Keys))
            {
                FluidInventory inventory = MachineWorld.GetFluidInventory(node, chamber);
                if (inventory == null || !drained.Add(inventory.State)) continue;
                double volume = MachineWorld.GetFluidVolumeLiters(node), minimum = MachineWorld.GetFluidMinimumGasSpaceLiters(node);
                if (inventory.GasMoles > 0m)
                    pressureEnergy += Math.Max(0d, inventory.GetPressureKPa(volume, minimum) - MachineWorld.GetFluidAmbientPressureKPa(node)) *
                        Math.Max(minimum, volume - inventory.GetLiquidLiters());
                materials.AddRange(inventory.Drain());
            }
            if (materials.Count > 0)
                PressureExplosionQueue.Enqueue("ship:" + ship.ShipId,
                    node.Snapshot.transform.position, pressureEnergy,
                    ship.ShipId + ":" + piece.PieceId + ":rupture:" + Guid.NewGuid().ToString("N"), materials, null);
            MachineWorld.CaptureFluidState(node);
        }
        private void QueueMaterialSalvage(ShipState ship, ShipPieceState piece)
        {
            string carrier = Mod_Building.GetSummonerPrefabId(piece.ItemId);
            if (string.IsNullOrWhiteSpace(carrier)) carrier = piece.ItemId;
            foreach (RuntimeRecipe recipe in GameRes.ExistingInstance.recipeById.Values)
            {
                if (recipe?.inputs?.recipeType != RecipeType.Crafting || recipe.outputs?.results == null) continue;
                bool found = recipe.outputs.results.Exists(value => value.ItemName == carrier && value.amount == 1);
                if (!found) continue;
                foreach (var ingredient in recipe.inputs.RowItems_List)
                {
                    if (ingredient.matchMode != MatchMode.ExactItem) continue;
                    int amount = 0;
                    for (int i = 0; i < ingredient.amount; i++) if (UnityEngine.Random.value < BuildingMaterialSalvage.DefaultRecoveryChance) amount++;
                    if (amount <= 0) continue;
                    ItemData data = GameRes.ExistingInstance.CreateItemData(ingredient.ItemName); data.Stack.Amount = amount;
                    QueueDrop(data, ship, piece);
                }
                return;
            }
        }
        // 爆炸保留生成时的世界位置和姿态，源船移走或分裂后仍能独立结算。
        private void CaptureShipExplosionContext(PressureExplosionEvent entry)
        {
            if (!entry.WorldKey.StartsWith("ship:", StringComparison.Ordinal)) return;
            ShipState source = GetShip(entry.WorldKey.Substring(5));
            if (source == null) throw new InvalidOperationException("船体爆炸入队时缺少源船姿态。");
            ShipGeometry.LocalToWorld(source, entry.Center.x, entry.Center.y, out double x, out double y);
            ShipFlightState flight = GetFlight(source.ShipId);
            bool space = flight?.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending;
            var snapshot = new ShipExplosionSpatialContext
            {
                SourceShipId = source.ShipId, InSpace = space,
                WorldKey = space ? "SpaceScene" : flight?.SurfaceWorldKey ?? source.WorldAddress,
                CenterX = x, CenterY = y, HeightMeters = space ? 0d : Math.Max(0d, flight?.HeightMeters ?? 0d),
                SourcePositionX = source.PositionX, SourcePositionY = source.PositionY, SourceAngleRadians = source.AngleRadians,
                SourceCenterOfMassLocalX = source.CenterOfMassLocalX, SourceCenterOfMassLocalY = source.CenterOfMassLocalY
            };
            if (string.IsNullOrWhiteSpace(snapshot.WorldKey)) throw new InvalidOperationException("船体爆炸缺少真实世界地址。");
            entry.SpatialContextJson = JsonConvert.SerializeObject(snapshot);
            entry.PresentationWorldKey = snapshot.WorldKey;
        }
        private bool HandleShipExplosion(PressureExplosionEvent entry, float radius, float peak)
        {
            if (!entry.WorldKey.StartsWith("ship:", StringComparison.Ordinal)) return false;
            ShipExplosionSpatialContext explosion = ReadExplosionContext(entry);
            foreach (ShipState target in State.Ships.ToArray())
            {
                ShipFlightState flight = GetFlight(target.ShipId);
                if (flight == null || explosion.InSpace != (flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending) ||
                    !explosion.InSpace && explosion.WorldKey != flight.SurfaceWorldKey) continue;
                double heightDifference = explosion.InSpace ? 0d : Math.Max(0d, flight.HeightMeters) - explosion.HeightMeters;
                if (Math.Abs(heightDifference) >= radius) continue;
                foreach (ShipPieceState piece in target.Pieces.ToArray())
                {
                    if (!piece.IsAlive) continue;
                    double planarDistance = DistanceToPiece(explosion, target, piece);
                    double distance = Math.Sqrt(planarDistance * planarDistance + heightDifference * heightDifference);
                    if (distance >= radius) continue;
                    float damage = CombatRules.ResolvePhysical(peak * (1f - (float)distance / radius), GetPiecePhysicalDefense(piece));
                    ShipStructureService.ApplyDamage(target, piece.PieceId, damage, "燃料与压力爆炸", DestroyPiece);
                    SynchronizePieceHealth(target, piece);
                }
            }
            ApplyExplosionActorDamage(entry, explosion, radius, peak);
            RetainExplosionGasInCabin(entry, explosion);
            if (!explosion.InSpace)
            {
                Vector2 center = PressureExplosionQueue.NormalizeInWorld(explosion.WorldKey, new Vector2((float)explosion.CenterX, (float)explosion.CenterY));
                PressureExplosionQueue.ReleaseMaterials(entry, explosion.WorldKey, center);
                if (explosion.HeightMeters < radius)
                {
                    float groundRadius = (float)Math.Sqrt(radius * radius - explosion.HeightMeters * explosion.HeightMeters);
                    float groundPeak = peak * (1f - (float)explosion.HeightMeters / radius);
                    SpaceSurfaceQuery.ApplyStructureDamageInRadius(explosion.WorldKey, center, groundRadius, groundPeak, true, entry.Id);
                    if (SceneManager.GetActiveScene().name == explosion.WorldKey)
                        using (MachineWorld.UseScope(explosion.WorldKey))
                            FlatWorld.NaturalEntities.NaturalEntityEcsService.ApplyPressureExplosion(explosion.WorldKey, entry.Id, center, groundRadius, groundPeak);
                }
            }
            return true;
        }
        private float GetPiecePhysicalDefense(ShipPieceState piece)
        {
            ItemData data = GetPieceData(piece);
            if (Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building) &&
                !string.IsNullOrWhiteSpace(building.TileBlockId))
            {
                RuntimeTileDefinition tile = GameRes.ExistingInstance.GetTileBlock(building.TileBlockId);
                if (tile?.DamageProfile != null) return tile.DamageProfile.ResolveDefense().Physical;
            }
            if (data?.ModuleDataDic != null)
                foreach (ModuleData module in data.ModuleDataDic.Values)
                    if (module.ID == ModText.Hp && module is Ex_ModData storage)
                        return storage.GetData<Mod_DamageReceiver.DamageReceiver_SaveData>()?.DefenseValues?.Physical ?? 0f;
            return GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition definition)
                ? definition.Health?.Defense?.Physical ?? 0f : 0f;
        }
        private void RetainExplosionGasInCabin(PressureExplosionEvent entry, ShipExplosionSpatialContext explosion)
        {
            var candidates = new List<ShipState>(State.Ships);
            ShipState source = GetShip(explosion.SourceShipId);
            if (source != null) { candidates.Remove(source); candidates.Insert(0, source); }
            foreach (ShipState ship in candidates)
            {
                ShipFlightState flight = GetFlight(ship.ShipId);
                if (flight == null || explosion.InSpace != (flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending) ||
                    !explosion.InSpace && (flight.SurfaceWorldKey != explosion.WorldKey || Math.Abs(flight.HeightMeters - explosion.HeightMeters) > .01d)) continue;
                double x = explosion.CenterX, y = explosion.CenterY;
                if (!explosion.InSpace)
                {
                    Vector2 delta = PressureExplosionQueue.DeltaInWorld(explosion.WorldKey,
                        new Vector2((float)ship.PositionX, (float)ship.PositionY), new Vector2((float)x, (float)y));
                    x = ship.PositionX + delta.x; y = ship.PositionY + delta.y;
                }
                ShipGeometry.WorldToLocal(ship, x, y, out double localX, out double localY);
                int cellX = (int)Math.Floor(localX / ship.CellSizeMeters), cellY = (int)Math.Floor(localY / ship.CellSizeMeters);
                if (!ShipAtmosphereService.HasFloor(ship, cellX, cellY)) continue;
                // 先重算爆炸后的真实边界，只有仍密闭的舱室接收罐中剩余气体。
                ShipAtmosphereService.RebuildCompartments(ship, GetOutside(ship));
                ShipCompartmentState room = ShipAtmosphereService.FindCompartment(ship, cellX, cellY);
                if (room == null) continue;
                foreach (PressureExplosionMaterial material in entry.Materials)
                {
                    decimal amount = Math.Min(material.GasMoles, Math.Max(0m, FluidInventory.MaximumMoles - ShipAtmosphereService.TotalMoles(room.Gas)));
                    if (amount <= 0m) continue;
                    FluidDefinition fluid = FluidCatalog.Default.Find(material.FluidId);
                    double capacity = (double)material.GasMoles * fluid.GasHeatCapacityJPerMolKelvin +
                        (double)material.LiquidMoles * fluid.LiquidHeatCapacityJPerMolKelvin;
                    double temperature = Math.Max(.001d, (material.InternalEnergyJoules -
                        (double)material.GasMoles * fluid.GasEnergyOffsetJPerMol) / Math.Max(1e-12d, capacity));
                    FluidBatch batch = FluidInventory.CreateBatch(fluid, amount, 0m, temperature);
                    if (!ShipAtmosphereService.TryAddGas(ship, cellX, cellY, batch)) continue;
                    material.GasMoles -= amount; material.InternalEnergyJoules = Math.Max(0d, material.InternalEnergyJoules - batch.InternalEnergyJoules);
                }
                return;
            }
        }
        private double DistanceToPiece(ShipExplosionSpatialContext explosion, ShipState ship, ShipPieceState piece)
        {
            double x = explosion.CenterX, y = explosion.CenterY;
            if (!explosion.InSpace)
            {
                Vector2 delta = PressureExplosionQueue.DeltaInWorld(explosion.WorldKey,
                    new Vector2((float)ship.PositionX, (float)ship.PositionY), new Vector2((float)x, (float)y));
                x = ship.PositionX + delta.x; y = ship.PositionY + delta.y;
            }
            ShipGeometry.WorldToLocal(ship, x, y, out double localX, out double localY);
            int width = Math.Max(1, piece.Width), height = Math.Max(1, piece.Height);
            if ((piece.QuarterTurns & 1) != 0) { int swap = width; width = height; height = swap; }
            double dx = localX - Math.Clamp(localX, piece.CellX * ship.CellSizeMeters, (piece.CellX + width) * ship.CellSizeMeters);
            double dy = localY - Math.Clamp(localY, piece.CellY * ship.CellSizeMeters, (piece.CellY + height) * ship.CellSizeMeters);
            return Math.Sqrt(dx * dx + dy * dy);
        }
        private void ApplyExplosionActorDamage(PressureExplosionEvent entry, ShipExplosionSpatialContext explosion, float radius, float peak)
        {
            if (radius <= 0f || peak <= 0f || ItemMgr.Instance == null) return;
            var targets = new HashSet<Item>(ItemMgr.Instance.WorldRunTimeItems.Values);
            foreach (Player player in ItemMgr.Instance.Player_DIC.Values) if (player != null) targets.Add(player);
            foreach (Item target in targets)
            {
                if (target == null || target.Owner != null || target.InHand || Owns(target.itemData.Guid) ||
                    Mod_Building.TryReadBuildingData(target.itemData, out _, out var building) && building.Role == BuildingRole.PlacedBuilding) continue;
                bool space = target.gameObject.scene.name == "SpaceScene";
                string world = target.gameObject.scene.name;
                double x, y, height = 0d;
                Vector2 position = target.gameObject.scene.name == SceneManager.GetActiveScene().name
                    ? WorldLocalPresentation.ToLogical(target.transform.position) : (Vector2)target.itemData.transform.position;
                x = position.x; y = position.y;
                if (space) { SpaceVector2 absolute = UnprojectPosition(target.transform.position); x = absolute.X; y = absolute.Y; }
                SpacePassengerState passenger = target is Player actor ? State.Passengers.Find(value => value.ProfileId == actor.ProfileName) : null;
                ShipState carrier = passenger != null && passenger.Supported ? GetShip(passenger.ShipId) : null;
                if (carrier != null)
                {
                    ShipFlightState flight = GetFlight(carrier.ShipId);
                    if (flight == null) continue;
                    space = flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending;
                    world = space ? "SpaceScene" : flight.SurfaceWorldKey; height = space ? 0d : Math.Max(0d, flight.HeightMeters);
                    ShipGeometry.LocalToWorld(carrier, passenger.LocalX, passenger.LocalY, out x, out y);
                }
                else if (passenger?.SurfaceDescending == true)
                { space = false; world = passenger.LandingWorldKey; x = passenger.LandingX; y = passenger.LandingY; height = Math.Max(0d, passenger.SurfaceHeight); }
                else if (passenger?.IsInSpace == true && space)
                { x = passenger.FreeMotion.PositionMeters.X; y = passenger.FreeMotion.PositionMeters.Y; }
                if (space != explosion.InSpace || world != explosion.WorldKey) continue;
                double dx = x - explosion.CenterX, dy = y - explosion.CenterY;
                if (!space)
                {
                    Vector2 delta = PressureExplosionQueue.DeltaInWorld(world, new Vector2((float)explosion.CenterX, (float)explosion.CenterY), new Vector2((float)x, (float)y));
                    dx = delta.x; dy = delta.y;
                }
                double dz = height - explosion.HeightMeters, distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distance >= radius) continue;
                Vector2 origin = space ? ProjectPosition(new SpaceVector2(explosion.CenterX, explosion.CenterY))
                    : new Vector2((float)explosion.CenterX, (float)explosion.CenterY);
                target.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.HurtPressureExplosion(entry.Id, origin, peak * (1f - (float)distance / radius));
            }
        }
        private void StepExplosions()
        {
            var worlds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShipState ship in State.Ships) worlds.Add("ship:" + ship.ShipId);
            MachineArchive archive = owner?.Mechanical;
            if (archive?.PressureExplosions != null)
                foreach (PressureExplosionEvent pending in archive.PressureExplosions)
                    worlds.Add(pending.WorldKey);
            if (archive?.PendingPressureLiquidSpills != null)
                foreach (PressureLiquidSpillState spill in archive.PendingPressureLiquidSpills)
                    if (PressureExplosionQueue.CanAdvanceLiquidSpill(spill)) worlds.Add(spill.WorldKey);
            foreach (string world in worlds)
            {
                using (MachineWorld.UseScope(world)) PressureExplosionQueue.Tick(0f);
                if (world.StartsWith("ship:", StringComparison.Ordinal) && GetShip(world.Substring(5)) == null &&
                    archive?.PressureExplosions.Exists(value => value.WorldKey == world) != true)
                    MachineWorld.ReleaseShipScope(world.Substring(5));
            }
            // 船撞毁地表气罐时产生的新世界连锁也领取同一帧预算，不等待玩家切场景。
            while (PressureExplosionQueue.HasFrameBudget && archive?.PressureExplosions.Count > 0)
            {
                PressureExplosionEvent pending = archive.PressureExplosions[0];
                using (MachineWorld.UseScope(pending.WorldKey)) PressureExplosionQueue.Tick(0f);
                if (archive.PressureExplosions.Exists(value => value.Id == pending.Id)) break;
                if (pending.WorldKey.StartsWith("ship:", StringComparison.Ordinal) && GetShip(pending.WorldKey.Substring(5)) == null &&
                    !archive.PressureExplosions.Exists(value => value.WorldKey == pending.WorldKey))
                    MachineWorld.ReleaseShipScope(pending.WorldKey.Substring(5));
            }
            if (destroyed.Count > 0) ProcessStructureChanges();
        }
        private bool PresentShipExplosion(PressureExplosionEvent entry, float radius)
        {
            if (!entry.WorldKey.StartsWith("ship:", StringComparison.Ordinal)) return false;
            ShipExplosionSpatialContext explosion = ReadExplosionContext(entry);
            if (SceneManager.GetActiveScene().name != explosion.WorldKey) return true;
            Vector2 center = explosion.InSpace ? ProjectPosition(new SpaceVector2(explosion.CenterX, explosion.CenterY))
                : WorldLocalPresentation.ProjectPosition(new Vector2((float)explosion.CenterX, (float)explosion.CenterY));
            PressureExplosionQueue.PresentAt(center, radius);
            return true;
        }
        private static ShipExplosionSpatialContext ReadExplosionContext(PressureExplosionEvent entry)
        {
            if (string.IsNullOrWhiteSpace(entry.SpatialContextJson)) throw new InvalidOperationException("爆炸事件缺少生成时的世界快照。");
            return JsonConvert.DeserializeObject<ShipExplosionSpatialContext>(entry.SpatialContextJson)
                ?? throw new InvalidOperationException("爆炸事件的世界快照无效。");
        }
        private void ApplyTerrainImpact(ShipState ship, double speed, double impactMassKg = 0d, Vector2? center = null, float? radius = null)
        {
            if (ship == null || speed <= 0d || string.IsNullOrWhiteSpace(ship.WorldAddress)) return;
            Vector2 point = center ?? new Vector2((float)ship.PositionX, (float)ship.PositionY);
            double extent = 1d;
            foreach (ShipPieceState piece in ship.Pieces)
                foreach (ShipCell cell in ShipGeometry.Footprint(piece))
                {
                    ShipGeometry.LocalToWorld(ship, (cell.X + .5d) * ship.CellSizeMeters, (cell.Y + .5d) * ship.CellSizeMeters,
                        out double x, out double y);
                    Vector2 delta = PressureExplosionQueue.DeltaInWorld(ship.WorldAddress, point, new Vector2((float)x, (float)y));
                    extent = Math.Max(extent, delta.magnitude + ship.CellSizeMeters * Math.Sqrt(.5d));
                }
            double mass = impactMassKg > 0d ? impactMassKg : ship.MassKg;
            float damage = (float)Math.Min(float.MaxValue, .5d * mass * speed * speed / SpaceGameplaySettings.Current.ImpactJoulesPerDurability);
            SpaceSurfaceQuery.ApplyStructureDamageInRadius(ship.WorldAddress, point, radius ?? (float)extent, damage);
        }
        #endregion
    }
    [Serializable, JsonObject(MemberSerialization.Fields)]
    internal sealed class ShipExplosionSpatialContext
    {
        #region 爆炸生成时的世界与姿态
        public string SourceShipId, WorldKey;
        public bool InSpace;
        public double CenterX, CenterY, HeightMeters;
        public double SourcePositionX, SourcePositionY, SourceAngleRadians, SourceCenterOfMassLocalX, SourceCenterOfMassLocalY;
        #endregion
    }
}
