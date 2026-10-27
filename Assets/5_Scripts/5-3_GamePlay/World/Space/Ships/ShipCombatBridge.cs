using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Networking;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed class ShipCombatBridge : IGameplayCombatBridge
    {
        #region 船体本地结构的战斗适配
        public static readonly ShipCombatBridge Instance = new();
        private readonly HitOrder hitOrder = new();

        /// <summary>只初始化实际使用的临时名单池，避免第一轮攻击触发泛型池冷启动。</summary>
        internal static void PrewarmQueryPools()
        {
            using var candidates = UnityEngine.Pool.ListPool<(ShipState Ship, ShipPieceState Piece, Vector2 Anchor,
                Mod_DamageReceiver Receiver, SpaceOreAsteroidState Asteroid)>.Get(out _);
            using var hits = UnityEngine.Pool.ListPool<CombatColliderHit2D>.Get(out _);
            using var resolved = UnityEngine.Pool.ListPool<(ShipState Ship, ShipPieceState Piece,
                RuntimeItemDefinition Definition, Mod_DamageReceiver Receiver, SpaceOreAsteroidState Asteroid, CombatColliderHit2D Contact)>.Get(out _);
        }

        private sealed class HitOrder : IComparer<CombatColliderHit2D>
        {
            public Vector2 SweepStart;
            public int Compare(CombatColliderHit2D left, CombatColliderHit2D right)
            {
                int order = left.Fraction.CompareTo(right.Fraction);
                if (order != 0) return order;
                order = (left.Point - SweepStart).sqrMagnitude.CompareTo((right.Point - SweepStart).sqrMagnitude);
                return order != 0 ? order : left.CandidateId.CompareTo(right.CandidateId);
            }
        }

        // 所有查询使用同一稳定命中顺序；测试探针也复用此入口，不复制排序算法。
        internal void SortContacts(List<CombatColliderHit2D> contacts, Vector2 sweepStart)
        {
            hitOrder.SweepStart = sweepStart;
            contacts.Sort(hitOrder);
        }

        public bool TryGetIdentity(Item item, out CombatIdentity identity)
        {
            identity = default;
            if (item?.itemData == null || !SpaceSession.TryGetCombatFrame(item, out _, out _)) return false;
            identity = new CombatIdentity { Backend = CombatBackend.External, Value = unchecked((uint)item.itemData.Guid),
                Generation = item.RuntimeGeneration, World = 0x53504143u };
            return true;
        }
        public void QueryWeaponPulse(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context)
        {
            SpaceSession session = SpaceSession.Current;
            if (!GameNetwork.HasStateAuthority || session?.State == null || weapon == null || weapon.RemainingAttackTargets == 0) return;
            bool spaceCombat = SpaceSession.TryGetCombatFrame(weapon.item, out SpaceSectorPosition canonicalOrigin, out Vector2 presentationOrigin);
            Player attacker = SpaceSession.CombatPlayer(weapon.item);
            SpaceCombatFrame frame = weapon.item != null ? weapon.item.GetComponent<SpaceCombatFrame>() : null;
            bool externalProjectile = frame != null && frame.Active;
            string attackerWorld = GameplayWorldRegistry.GetItemWorldKey(
                externalProjectile || attacker == null ? weapon.item : attacker);
            ShipState attackerShip = attackerWorld?.StartsWith("ship:", StringComparison.Ordinal) == true
                ? session.GetShip(attackerWorld.Substring(5)) : null;
            string surfaceWorld = null;
            bool hasSurfaceWorld = !spaceCombat && SpaceSession.TryGetCombatWorldKey(weapon.item, out surfaceWorld);
            if (!spaceCombat && !hasSurfaceWorld) return;
            SpaceSession.SurfaceCombatProjection surfaceProjection = hasSurfaceWorld
                ? SpaceSession.CaptureSurfaceCombatProjection(weapon.item, surfaceWorld, (Vector2)shape.Center)
                : default;
            // 攻击窗口内的三份临时名单统一租用池，连续挥砍不再反复分配托管数组。
            using var candidatesLease = UnityEngine.Pool.ListPool<(ShipState Ship, ShipPieceState Piece, Vector2 Anchor,
                Mod_DamageReceiver Receiver, SpaceOreAsteroidState Asteroid)>.Get(out var candidates);
            using var hitsLease = UnityEngine.Pool.ListPool<CombatColliderHit2D>.Get(out var hits);
            using var resolvedLease = UnityEngine.Pool.ListPool<(ShipState Ship, ShipPieceState Piece,
                RuntimeItemDefinition Definition, Mod_DamageReceiver Receiver, SpaceOreAsteroidState Asteroid, CombatColliderHit2D Contact)>.Get(out var resolvedHits);
            using (CombatColliderQuery2D physics = CombatColliderQuery2D.Rent())
            {
                foreach (ShipState ship in session.State.Ships)
                {
                    // 同一世界已有正式物品、机器与地块受击查询，不再重复结算本船结构。
                    if (attackerWorld == MachineWorld.GetShipScopeKey(ship.ShipId) ||
                        externalProjectile && frame.DepartingShipId == ship.ShipId) continue;
                    ShipFlightState flight = session.GetFlight(ship.ShipId);
                    if (spaceCombat ? flight?.Phase != ShipFlightPhase.Cruising :
                        !hasSurfaceWorld || flight == null || flight.Phase == ShipFlightPhase.Cruising ||
                        !string.Equals(flight.SurfaceWorldKey, surfaceWorld, StringComparison.Ordinal)) continue;
                    // 船体按实际空地状态筛选投送能力，拒绝的攻击不占用命中名额。
                    if (!ShipSurfaceState.AllowsDamage(flight, context.DeliveryCapabilities)) continue;
                    foreach (ShipPieceState piece in ship.Pieces)
                    {
                        if (!piece.IsAlive) continue;
                        if (piece.Kind == ShipPieceKind.Wire && MachineCatalog.Get(piece.ItemId)?.Electrical?.Hidden == true &&
                            !EngineeringVision.CanSee(weapon.item?.Owner)) continue;
                        ShipGeometry.LocalToWorld(ship, (piece.CellX + .5d) * ship.CellSizeMeters,
                            (piece.CellY + .5d) * ship.CellSizeMeters, out double x, out double y);
                        Vector2 anchor;
                        if (spaceCombat)
                        {
                            Vector2 relative = SpaceSectorCoordinates.Difference(
                                new SpaceSectorPosition(ship.Sector, new SpaceVector2(x, y)), canonicalOrigin).ToVector2();
                            if (attackerShip != null)
                            {
                                ShipGeometry.Rotate(relative.x, relative.y, -attackerShip.AngleRadians, out double rx, out double ry);
                                relative = new Vector2((float)rx, (float)ry);
                            }
                            anchor = presentationOrigin + relative;
                        }
                        else if (attackerShip != null)
                        {
                            Vector2 start = new((float)attackerShip.PositionX, (float)attackerShip.PositionY);
                            Vector2 relative = PressureExplosionQueue.DeltaInWorld(surfaceWorld, start, new Vector2((float)x, (float)y));
                            relative.y += (float)(ShipSurfaceState.VisualRise(flight) -
                                ShipSurfaceState.VisualRise(session.GetFlight(attackerShip.ShipId)));
                            ShipGeometry.WorldToLocal(attackerShip, start.x + relative.x, start.y + relative.y, out double lx, out double ly);
                            anchor = new Vector2((float)lx, (float)ly);
                        }
                        else
                        {
                            anchor = surfaceProjection.Project(new Vector2((float)x, (float)y))
                                + Vector2.up * (float)ShipSurfaceState.VisualRise(flight);
                        }
                        float width = (float)((piece.QuarterTurns % 2 == 0 ? piece.Width : piece.Height) * ship.CellSizeMeters);
                        float height = (float)((piece.QuarterTurns % 2 == 0 ? piece.Height : piece.Width) * ship.CellSizeMeters);
                        float radius = Mathf.Sqrt(width * width + height * height);
                        if (Math.Abs(anchor.x - shape.BoundsCenter.x) > shape.BoundsExtents.x + radius ||
                            Math.Abs(anchor.y - shape.BoundsCenter.y) > shape.BoundsExtents.y + radius) continue;
                        // 物品定义只在实际接触后读取，避免每次挥砍对同一部件重复查表。
                        PerceptionShape2D local = PerceptionShape2D.Aabb(new float2((width - (float)ship.CellSizeMeters) * .5f, (height - (float)ship.CellSizeMeters) * .5f), new float2(width * .5f, height * .5f));
                        double angle = ship.AngleRadians - (attackerShip?.AngleRadians ?? 0d);
                        ShipGeometry.Rotate(local.Center.x, local.Center.y, angle, out double offsetX, out double offsetY);
                        local.Center = new float2(anchor.x + (float)offsetX, anchor.y + (float)offsetY);
                        physics.Add(candidates.Count, local, (float)(angle * 180d / Math.PI));
                        candidates.Add((ship, piece, anchor, null, null));
                    }
                }
                if (spaceCombat)
                    foreach (SpaceOreAsteroidState asteroid in session.State.OreAsteroids)
                    {
                        if (!asteroid.IsAlive) continue;
                        Vector2 anchor = presentationOrigin + SpaceSectorCoordinates.Difference(
                            session.GetOreAsteroidPosition(asteroid), canonicalOrigin).ToVector2();
                        if (attackerShip != null)
                        {
                            Vector2 relative = anchor - presentationOrigin;
                            ShipGeometry.Rotate(relative.x, relative.y, -attackerShip.AngleRadians, out double rx, out double ry);
                            anchor = presentationOrigin + new Vector2((float)rx, (float)ry);
                        }
                        float radius = (float)asteroid.RadiusMeters;
                        if (Math.Abs(anchor.x - shape.BoundsCenter.x) > shape.BoundsExtents.x + radius ||
                            Math.Abs(anchor.y - shape.BoundsCenter.y) > shape.BoundsExtents.y + radius) continue;
                        physics.Add(candidates.Count, PerceptionShape2D.Circle(anchor, radius), 0f);
                        candidates.Add((null, null, anchor, null, asteroid));
                    }
                if (spaceCombat && ItemMgr.Instance != null)
                    foreach (SpacePassengerState passenger in session.State.Passengers)
                    {
                        if (!session.TryGetNetworkObserver(passenger.ProfileId, out SpaceNetworkObserver observer) || !observer.InSpace ||
                            !ItemMgr.Instance.Player_DIC.TryGetValue(passenger.ProfileId, out Player player) || player == null) continue;
                        string targetWorld = GameplayWorldRegistry.GetItemWorldKey(player);
                        if (attackerShip != null && targetWorld == attackerWorld || externalProjectile &&
                            frame.DepartingShipId != null && targetWorld == MachineWorld.GetShipScopeKey(frame.DepartingShipId)) continue;
                        CombatIdentity identity = GameplayCombatBridge.Identity(player);
                        if (identity == context.Credit || identity == context.Attack.Source) continue;
                        Mod_DamageReceiver receiver = player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
                        if (receiver == null || receiver.Hp <= 0f) continue;
                        BoxCollider2D collider = receiver.GetComponent<BoxCollider2D>();
                        if (collider == null) continue;
                        Vector2 relative = SpaceSectorCoordinates.Difference(observer.Position, canonicalOrigin).ToVector2();
                        if (attackerShip != null)
                        {
                            ShipGeometry.Rotate(relative.x, relative.y, -attackerShip.AngleRadians, out double rx, out double ry);
                            relative = new Vector2((float)rx, (float)ry);
                        }
                        Vector2 anchor = presentationOrigin + relative;
                        Vector2 offset = (Vector2)collider.transform.TransformPoint(collider.offset) - (Vector2)player.transform.position;
                        Vector3 axisX = collider.transform.TransformVector(Vector3.right), axisY = collider.transform.TransformVector(Vector3.up);
                        ShipState targetShip = targetWorld?.StartsWith("ship:", StringComparison.Ordinal) == true
                            ? session.GetShip(targetWorld.Substring(5)) : null;
                        double relativeAngle = (targetShip?.AngleRadians ?? 0d) - (attackerShip?.AngleRadians ?? 0d);
                        if (relativeAngle != 0d)
                        {
                            Quaternion turn = Quaternion.Euler(0f, 0f, (float)(relativeAngle * Mathf.Rad2Deg));
                            offset = turn * offset; axisX = turn * axisX; axisY = turn * axisY;
                        }
                        Vector2 extent = new(collider.size.x * axisX.magnitude * .5f, collider.size.y * axisY.magnitude * .5f);
                        if (Math.Abs(anchor.x + offset.x - shape.BoundsCenter.x) > shape.BoundsExtents.x + extent.magnitude ||
                            Math.Abs(anchor.y + offset.y - shape.BoundsCenter.y) > shape.BoundsExtents.y + extent.magnitude) continue;
                        physics.Add(candidates.Count, PerceptionShape2D.Aabb(anchor + offset, extent), Mathf.Atan2(axisX.y, axisX.x) * Mathf.Rad2Deg);
                        candidates.Add((null, null, default, receiver, null));
                    }
                physics.Query(shape, hits);
            }
            // 重叠目标优先离攻击起点最近者，比较器复用，避免排序闭包带来每次挥砍的 GC。
            SortContacts(hits, (Vector2)(shape.Center - shape.SweepDelta));
            foreach (CombatColliderHit2D contact in hits)
            {
                var target = candidates[contact.CandidateId];
                if (target.Asteroid != null)
                {
                    resolvedHits.Add((null, null, null, null, target.Asteroid, contact));
                    continue;
                }
                if (target.Receiver != null)
                {
                    resolvedHits.Add((null, null, null, target.Receiver, null, contact));
                    continue;
                }
                Vector2 delta = contact.Point - target.Anchor;
                ShipGeometry.Rotate(delta.x, delta.y, -target.Ship.AngleRadians + (attackerShip?.AngleRadians ?? 0d),
                    out double dx, out double dy);
                ShipPieceState piece = ShipDamageTargeting.SelectAtContact(target.Ship, target.Piece,
                    (target.Piece.CellX + .5d) * target.Ship.CellSizeMeters + dx,
                    (target.Piece.CellY + .5d) * target.Ship.CellSizeMeters + dy);
                if (piece != null && GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition definition))
                {
                    if (piece.Kind == ShipPieceKind.Wire && MachineCatalog.Get(piece.ItemId)?.Electrical?.Hidden == true &&
                        !EngineeringVision.CanSee(weapon.item?.Owner)) continue;
                    resolvedHits.Add((target.Ship, piece, definition, null, null, contact));
                }
            }
            // 命中名单在扣血前固定，先摧毁设备也不能让同一发补打刚露出的地板。
            foreach (var target in resolvedHits)
            {
                if (weapon.RemainingAttackTargets == 0) break;
                CombatColliderHit2D contact = target.Contact;
                if (target.Asteroid != null)
                {
                    if (!target.Asteroid.IsAlive) continue;
                    ulong stableId = ((ulong)unchecked((uint)UniverseSectorFactory.DeriveSeed(0, target.Asteroid.AsteroidId)) << 32) |
                        unchecked((uint)UniverseSectorFactory.DeriveSeed(1, target.Asteroid.AsteroidId));
                    var asteroidIdentity = new CombatIdentity { Backend = CombatBackend.External, Value = stableId | (1UL << 63),
                        World = context.Attack.Source.World, Dimension = context.Attack.Source.Dimension };
                    if (!weapon.TryReserveExternalTarget(asteroidIdentity)) continue;
                    CombatDamageContext asteroidHit = context; asteroidHit.HitPoint = contact.Point;
                    weapon.ConsumeWeaponDurabilityForHit();
                    float multiplier = GameplayCombatBridge.Tuning().Resolve(context.SourceIsPlayer != 0, false);
                    float asteroidDamage = CombatRules.ResolvePhysical(math.csum(context.Damage), 0f, multiplier, 1f);
                    weapon.PublishExternalDamage(asteroidHit, (float)session.ApplyResolvedOreAsteroidDamage(target.Asteroid, asteroidDamage));
                    continue;
                }
                if (target.Receiver != null)
                {
                    if (!weapon.TryReserveExternalTarget(GameplayCombatBridge.Identity(target.Receiver.item))) continue;
                    CombatDamageContext playerHit = context;
                    Vector2 relative = contact.Point - presentationOrigin;
                    if (attackerShip != null)
                    {
                        ShipGeometry.Rotate(relative.x, relative.y, attackerShip.AngleRadians, out double rx, out double ry);
                        relative = new Vector2((float)rx, (float)ry);
                    }
                    SpaceSectorPosition hitPosition = SpaceSectorCoordinates.Normalize(canonicalOrigin.Sector,
                        canonicalOrigin.LocalMeters + SpaceVector2.FromVector2(relative));
                    string targetWorld = GameplayWorldRegistry.GetItemWorldKey(target.Receiver.item);
                    ShipState targetShip = targetWorld?.StartsWith("ship:", StringComparison.Ordinal) == true
                        ? session.GetShip(targetWorld.Substring(5)) : null;
                    if (targetShip != null)
                    {
                        ShipGeometry.WorldToLocal(targetShip, hitPosition.Sector, hitPosition.LocalMeters.X, hitPosition.LocalMeters.Y,
                            out double hx, out double hy);
                        playerHit.HitPoint = new float2((float)hx, (float)hy);
                    }
                    else if (target.Receiver.item is Player targetPlayer &&
                        session.TryGetNetworkObserver(targetPlayer.ProfileName, out SpaceNetworkObserver targetObserver))
                        playerHit.HitPoint = (float2)((Vector2)targetPlayer.transform.position +
                            SpaceSectorCoordinates.Difference(hitPosition, targetObserver.Position).ToVector2());
                    else playerHit.HitPoint = contact.Point;
                    weapon.ConsumeWeaponDurabilityForHit();
                    float received;
                    using (GameplayWorldRegistry.TryGet(targetWorld, out _)
                        ? GameplayWorldRegistry.UseWorld(targetWorld) : null)
                        received = target.Receiver.Hurt(playerHit);
                    CombatDamageContext sourceHit = context; sourceHit.HitPoint = contact.Point;
                    weapon.PublishExternalDamage(sourceHit, received);
                    continue;
                }
                if (!target.Piece.IsAlive || !int.TryParse(target.Piece.PieceId, out int guid)) continue;
                var identity = new CombatIdentity { Backend = CombatBackend.External, Value = unchecked((uint)guid),
                    World = context.Attack.Source.World, Dimension = context.Attack.Source.Dimension };
                if (!weapon.TryReserveExternalTarget(identity)) continue;
                CombatDamageContext hit = context; hit.HitPoint = contact.Point;
                weapon.ConsumeWeaponDurabilityForHit();
                float attackMultiplier = GameplayCombatBridge.Tuning().Resolve(context.SourceIsPlayer != 0, false);
                float weakness = hit.IsTrueDamage != 0 || target.Definition.Health == null ? 1f :
                    Mod_ResourceHarvest.ResolveAffinityMultiplier(target.Definition.Health.WeakTool, 1,
                        (ResourceToolKind)hit.ResourceToolKind, hit.ResourceToolTier, hit.ResourceToolEfficiency);
                float damage = CombatRules.ResolvePhysical(math.csum(hit.Damage),
                    hit.IsTrueDamage != 0 ? 0f : (float)target.Piece.PhysicalDefense, attackMultiplier, weakness);
                weapon.PublishExternalDamage(hit, (float)session.ApplyResolvedPieceDamage(target.Ship, target.Piece, damage, "攻击"));
            }
        }
        #endregion
    }
}
