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
        public bool TryGetIdentity(Item item, out CombatIdentity identity) { identity = default; return false; }
        public void QueryWeaponPulse(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context)
        {
            SpaceSession session = SpaceSession.Current;
            if (!GameNetwork.HasStateAuthority || session?.State == null || weapon == null || weapon.RemainingAttackTargets == 0) return;
            var candidates = new List<(ShipState Ship, ShipPieceState Piece, RuntimeItemDefinition Definition)>();
            var hits = new List<CombatColliderHit2D>();
            using (CombatColliderQuery2D physics = CombatColliderQuery2D.Rent())
            {
                foreach (ShipState ship in session.State.Ships)
                {
                    if (!session.IsVisible(ship)) continue;
                    foreach (ShipPieceState piece in ship.Pieces)
                    {
                        if (!piece.IsAlive || !GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition definition)) continue;
                        Vector2 anchor = session.DisplayPoint(ship, (piece.CellX + .5d) * ship.CellSizeMeters, (piece.CellY + .5d) * ship.CellSizeMeters);
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
                        candidates.Add((ship, piece, definition));
                    }
                }
                physics.Query(shape, hits);
            }
            hits.Sort((left, right) => left.Fraction.CompareTo(right.Fraction));
            foreach (CombatColliderHit2D contact in hits)
            {
                if (weapon.RemainingAttackTargets == 0) break;
                var target = candidates[contact.CandidateId];
                var identity = new CombatIdentity { Backend = CombatBackend.External, Value = unchecked((uint)int.Parse(target.Piece.PieceId)),
                    World = context.Attack.Source.World, Dimension = context.Attack.Source.Dimension };
                if (!weapon.TryReserveExternalTarget(identity)) continue;
                CombatDamageContext hit = context; hit.HitPoint = contact.Point;
                float difficulty = GameplayCombatBridge.Difficulty().Resolve(context.SourceIsPlayer != 0, false);
                float damage = target.Definition.Health != null ? MachineCombatBridge.CalculateDamage(target.Definition.Health, hit, difficulty)
                    : CombatRules.ResolvePhysical(math.csum(hit.Damage), 0f, difficulty, 1f);
                double before = target.Piece.Health;
                ShipStructureService.ApplyDamage(target.Ship, target.Piece.PieceId, damage, "攻击", session.DestroyPiece);
                session.SynchronizePieceHealth(target.Ship, target.Piece);
                weapon.PublishExternalDamage(hit, (float)(before - target.Piece.Health));
            }
        }
        #endregion
    }
}
