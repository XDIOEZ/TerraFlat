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
            string surfaceWorld = null;
            bool hasSurfaceWorld = !spaceCombat && SpaceSession.TryGetCombatWorldKey(weapon.item, out surfaceWorld);
            var candidates = new List<(ShipState Ship, ShipPieceState Piece, RuntimeItemDefinition Definition, Mod_DamageReceiver Receiver)>();
            var hits = new List<CombatColliderHit2D>();
            using (CombatColliderQuery2D physics = CombatColliderQuery2D.Rent())
            {
                foreach (ShipState ship in session.State.Ships)
                {
                    ShipFlightState flight = session.GetFlight(ship.ShipId);
                    if (spaceCombat ? flight?.Phase != ShipFlightPhase.Cruising :
                        !hasSurfaceWorld || flight == null || flight.Phase == ShipFlightPhase.Cruising ||
                        !string.Equals(flight.SurfaceWorldKey, surfaceWorld, StringComparison.Ordinal)) continue;
                    // 船体按实际空地状态筛选投送能力，拒绝的攻击不占用命中名额。
                    if (!ShipSurfaceState.AllowsDamage(flight, context.DeliveryCapabilities)) continue;
                    foreach (ShipPieceState piece in ship.Pieces)
                    {
                        if (!piece.IsAlive || !GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition definition)) continue;
                        Vector2 anchor;
                        if (spaceCombat)
                        {
                            ShipGeometry.LocalToWorld(ship, (piece.CellX + .5d) * ship.CellSizeMeters,
                                (piece.CellY + .5d) * ship.CellSizeMeters, out double x, out double y);
                            anchor = presentationOrigin + SpaceSectorCoordinates.Difference(
                                new SpaceSectorPosition(ship.Sector, new SpaceVector2(x, y)), canonicalOrigin).ToVector2();
                        }
                        else
                        {
                            ShipGeometry.LocalToWorld(ship, (piece.CellX + .5d) * ship.CellSizeMeters,
                                (piece.CellY + .5d) * ship.CellSizeMeters, out double x, out double y);
                            anchor = SpaceSession.ProjectSurfaceCombatPoint(weapon.item, surfaceWorld,
                                new Vector2((float)x, (float)y), (Vector2)shape.Center)
                                + Vector2.up * (float)ShipSurfaceState.VisualRise(flight);
                        }
                        float width = (float)((piece.QuarterTurns % 2 == 0 ? piece.Width : piece.Height) * ship.CellSizeMeters);
                        float height = (float)((piece.QuarterTurns % 2 == 0 ? piece.Height : piece.Width) * ship.CellSizeMeters);
                        float radius = Mathf.Sqrt(width * width + height * height);
                        if (Math.Abs(anchor.x - shape.BoundsCenter.x) > shape.BoundsExtents.x + radius ||
                            Math.Abs(anchor.y - shape.BoundsCenter.y) > shape.BoundsExtents.y + radius) continue;
                        PerceptionShape2D local = PerceptionShape2D.Aabb(new float2((width - (float)ship.CellSizeMeters) * .5f, (height - (float)ship.CellSizeMeters) * .5f), new float2(width * .5f, height * .5f));
                        double angle = ship.AngleRadians;
                        ShipGeometry.Rotate(local.Center.x, local.Center.y, angle, out double offsetX, out double offsetY);
                        local.Center = new float2(anchor.x + (float)offsetX, anchor.y + (float)offsetY);
                        physics.Add(candidates.Count, local, (float)(angle * 180d / Math.PI));
                        candidates.Add((ship, piece, definition, null));
                    }
                }
                if (spaceCombat && ItemMgr.Instance != null)
                    foreach (SpacePassengerState passenger in session.State.Passengers)
                    {
                        if (!session.TryGetNetworkObserver(passenger.ProfileId, out SpaceNetworkObserver observer) || !observer.InSpace ||
                            !ItemMgr.Instance.Player_DIC.TryGetValue(passenger.ProfileId, out Player player) || player == null) continue;
                        CombatIdentity identity = GameplayCombatBridge.Identity(player);
                        if (identity == context.Credit || identity == context.Attack.Source) continue;
                        Mod_DamageReceiver receiver = player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
                        if (receiver == null || receiver.Hp <= 0f) continue;
                        BoxCollider2D collider = receiver.GetComponent<BoxCollider2D>();
                        if (collider == null) continue;
                        Vector2 relative = SpaceSectorCoordinates.Difference(observer.Position, canonicalOrigin).ToVector2();
                        Vector2 anchor = presentationOrigin + relative;
                        Vector2 offset = (Vector2)collider.transform.TransformPoint(collider.offset) - (Vector2)player.transform.position;
                        Vector3 axisX = collider.transform.TransformVector(Vector3.right), axisY = collider.transform.TransformVector(Vector3.up);
                        Vector2 extent = new(collider.size.x * axisX.magnitude * .5f, collider.size.y * axisY.magnitude * .5f);
                        if (Math.Abs(anchor.x + offset.x - shape.BoundsCenter.x) > shape.BoundsExtents.x + extent.magnitude ||
                            Math.Abs(anchor.y + offset.y - shape.BoundsCenter.y) > shape.BoundsExtents.y + extent.magnitude) continue;
                        physics.Add(candidates.Count, PerceptionShape2D.Aabb(anchor + offset, extent), Mathf.Atan2(axisX.y, axisX.x) * Mathf.Rad2Deg);
                        candidates.Add((null, null, null, receiver));
                    }
                physics.Query(shape, hits);
            }
            hits.Sort((left, right) => left.Fraction.CompareTo(right.Fraction));
            foreach (CombatColliderHit2D contact in hits)
            {
                if (weapon.RemainingAttackTargets == 0) break;
                var target = candidates[contact.CandidateId];
                if (target.Receiver != null)
                {
                    if (!weapon.TryReserveExternalTarget(GameplayCombatBridge.Identity(target.Receiver.item))) continue;
                    CombatDamageContext playerHit = context; playerHit.HitPoint = contact.Point;
                    weapon.PublishExternalDamage(playerHit, target.Receiver.Hurt(playerHit));
                    continue;
                }
                var identity = new CombatIdentity { Backend = CombatBackend.External, Value = unchecked((uint)(target.Ship.IsWreck ? target.Ship.WreckGuid : int.Parse(target.Piece.PieceId))),
                    World = context.Attack.Source.World, Dimension = context.Attack.Source.Dimension };
                if (!weapon.TryReserveExternalTarget(identity)) continue;
                CombatDamageContext hit = context; hit.HitPoint = contact.Point;
                float difficulty = GameplayCombatBridge.Difficulty().Resolve(context.SourceIsPlayer != 0, false);
                float damage = target.Definition.Health != null ? MachineCombatBridge.CalculateDamage(target.Definition.Health, hit, difficulty)
                    : CombatRules.ResolvePhysical(math.csum(hit.Damage), 0f, difficulty, 1f);
                if (target.Ship.IsWreck)
                {
                    weapon.PublishExternalDamage(hit, (float)session.ApplyWreckDamage(target.Ship, damage));
                    continue;
                }
                double before = target.Piece.Health;
                ShipStructureService.ApplyDamage(target.Ship, target.Piece.PieceId, damage, "攻击", session.DestroyPiece);
                session.SynchronizePieceHealth(target.Ship, target.Piece);
                weapon.PublishExternalDamage(hit, (float)(before - target.Piece.Health));
            }
        }
        #endregion
    }
}
