using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 太空物理接入与业务提交
        private SpacePhysicsWorld spacePhysics;

        internal void ResetSpacePhysics()
        {
            spacePhysics?.Dispose();
            spacePhysics = null;
        }

        private void StepSpacePhysics(IReadOnlyList<ShipAssemblyState> assemblies, double seconds)
        {
            spacePhysics ??= new SpacePhysicsWorld(GetInstanceID());
            spacePhysics.BeginFrame();
            foreach (ShipAssemblyState assembly in assemblies)
            {
                ShipFlightState flight = GetFlight(assembly.MemberShipIds[0]);
                if (flight?.Phase != ShipFlightPhase.Cruising) continue;
                SpacePhysicsBody body = spacePhysics.Acquire("ship:" + assembly.AssemblyId);
                body.Assembly = assembly; body.Passenger = null; body.LooseItem = null;
                body.FreeCircle = body.FreezeRotation = false;
                body.Position = new SpaceSectorPosition(assembly.Sector, new SpaceVector2(assembly.PositionX, assembly.PositionY));
                body.Velocity = new SpaceVector2(assembly.VelocityX, assembly.VelocityY);
                body.AngleRadians = assembly.AngleRadians; body.AngularVelocityRadians = assembly.AngularVelocityRadiansPerSecond;
                body.MassKg = assembly.MassKg; body.InertiaKgM2 = assembly.InertiaKgM2;
                body.RadiusMeters = AssemblyRadius(assembly);
                AddSpaceShipShapes(body, assembly);
            }
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                if (!passenger.IsInSpace || passenger.Supported || passenger.SurfaceDescending || passenger.LandingResolved) continue;
                SpacePhysicsBody body = spacePhysics.Acquire("player:" + passenger.ProfileId);
                body.Assembly = null; body.Passenger = passenger; body.LooseItem = null;
                body.Position = new SpaceSectorPosition(passenger.FreeMotion.Sector, passenger.FreeMotion.PositionMeters);
                body.Velocity = passenger.FreeMotion.VelocityMetersPerSecond;
                body.AngleRadians = body.AngularVelocityRadians = 0d;
                Player player = FindNetworkPlayer(passenger.ProfileId);
                Data_Player data = player?.Data;
                if (data == null) owner.PlayerData_Dict.TryGetValue(passenger.ProfileId, out data);
                contactMassReader.Reset();
                body.MassKg = Math.Max(.001d, data == null ? 70d : contactMassReader.ReadItem(data, player, 0));
                ConfigureSpaceCircle(body, Math.Max(.01d, passenger.FreeMotion.RadiusMeters), true);
            }
            foreach (SpaceLooseItemState item in State.LooseItems)
            {
                if (!item.InSpace || string.IsNullOrWhiteSpace(item.Snapshot)) continue;
                looseViews.TryGetValue(item.Id, out Item live);
                if (live != null && (live.Owner != null || live.InHand || live.itemData.Stack.Amount <= 0f)) continue;
                SpacePhysicsBody body = spacePhysics.Acquire("item:" + item.Id);
                body.Assembly = null; body.Passenger = null; body.LooseItem = item;
                body.Position = new SpaceSectorPosition(item.Motion.Sector, item.Motion.PositionMeters);
                body.Velocity = item.Motion.VelocityMetersPerSecond;
                body.AngleRadians = item.RotationDegrees * Math.PI / 180d;
                body.AngularVelocityRadians = item.AngularVelocityDegreesPerSecond * Math.PI / 180d;
                if (body.MassSnapshot != item.Snapshot)
                {
                    ItemData data = live != null ? live.itemData : Decode(item.Snapshot);
                    looseData[item.Id] = data;
                    contactMassReader.Reset();
                    body.MassKg = Math.Max(.001d, contactMassReader.ReadItem(data, live, 0));
                    body.MassSnapshot = item.Snapshot;
                }
                ConfigureSpaceCircle(body, Math.Max(.01d, item.Motion.RadiusMeters), false);
            }
            spacePhysics.Step(gravitySources, Math.Max(0d, State.SimulationSeconds - seconds), seconds, SpaceGameplaySettings.Current);
            foreach (SpacePhysicsBody body in spacePhysics.ActiveBodies) CommitSpacePhysicsBody(body);
            // 损伤只读取 Unity 已经解算的接触，不能再向同一批刚体追加自研碰撞冲量。
            foreach (SpacePhysicsImpact impact in spacePhysics.Impacts)
            {
                double budget = impact.EnergyJoules / SpaceGameplaySettings.Current.ImpactJoulesPerDurability * .5d;
                ApplySpacePhysicsImpact(impact.Left.Assembly, budget);
                ApplySpacePhysicsImpact(impact.Right.Assembly, budget);
            }
        }

        private static void ConfigureSpaceCircle(SpacePhysicsBody body, double radius, bool freezeRotation)
        {
            body.FreeCircle = true; body.FreezeRotation = freezeRotation; body.RadiusMeters = radius;
            body.InertiaKgM2 = .5d * body.MassKg * radius * radius;
            body.Shapes.Add(new SpacePhysicsShape(Vector2.zero, new Vector2((float)radius, (float)radius), circle: true));
        }

        private void AddSpaceShipShapes(SpacePhysicsBody body, ShipAssemblyState assembly)
        {
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id);
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive || piece.Kind is ShipPieceKind.Wire or ShipPieceKind.Fiber ||
                        piece.Kind == ShipPieceKind.Door && piece.DoorOpen) continue;
                    int width = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height);
                    int height = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width);
                    ShipGeometry.LocalToWorld(ship, (piece.CellX + width * .5d) * ship.CellSizeMeters,
                        (piece.CellY + height * .5d) * ship.CellSizeMeters, out double x, out double y);
                    Vector2 local = SpacePhysicsShipOffset(ship.Sector, new SpaceVector2(x, y), assembly);
                    body.Shapes.Add(new SpacePhysicsShape(local, new Vector2((float)(width * ship.CellSizeMeters), (float)(height * ship.CellSizeMeters)),
                        (float)((ship.AngleRadians - assembly.AngleRadians) * Mathf.Rad2Deg), ignoreFreeCircles: !piece.BlocksPassage));
                }
                foreach (SpacePassengerState passenger in State.Passengers)
                {
                    if (!passenger.Supported || passenger.ShipId != id) continue;
                    ShipGeometry.LocalToWorld(ship, passenger.LocalX, passenger.LocalY, out double x, out double y);
                    // 支撑乘员的质量已计入整船，碰撞圆也归同一个原生刚体，避免重复计重。
                    body.Shapes.Add(new SpacePhysicsShape(SpacePhysicsShipOffset(ship.Sector, new SpaceVector2(x, y), assembly),
                        new Vector2(.3f, .3f), circle: true));
                }
            }
        }

        private static Vector2 SpacePhysicsShipOffset(SpaceSectorId sector, SpaceVector2 position, ShipAssemblyState assembly)
        {
            SpaceVector2 delta = SpaceSectorCoordinates.Relative(sector, position, assembly.Sector) - new SpaceVector2(assembly.PositionX, assembly.PositionY);
            ShipGeometry.Rotate(delta.X, delta.Y, -assembly.AngleRadians, out double x, out double y);
            return new Vector2((float)x, (float)y);
        }

        private void CommitSpacePhysicsBody(SpacePhysicsBody body)
        {
            if (body.Assembly != null)
            {
                ShipAssemblyState assembly = body.Assembly;
                assembly.Sector = body.Position.Sector; assembly.PositionX = body.Position.LocalMeters.X; assembly.PositionY = body.Position.LocalMeters.Y;
                assembly.VelocityX = body.Velocity.X; assembly.VelocityY = body.Velocity.Y;
                assembly.AngleRadians = body.AngleRadians; assembly.AngularVelocityRadiansPerSecond = body.AngularVelocityRadians;
                ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
                foreach (string id in assembly.MemberShipIds)
                {
                    ShipState ship = GetShip(id); ShipFlightState flight = GetFlight(id);
                    flight.Motion.Sector = ship.Sector; flight.Motion.PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY);
                    flight.Motion.VelocityMetersPerSecond = new SpaceVector2(ship.VelocityX, ship.VelocityY);
                }
            }
            else if (body.Passenger != null)
            {
                SpacePassengerState passenger = body.Passenger;
                passenger.FreeMotion.Sector = body.Position.Sector; passenger.FreeMotion.PositionMeters = body.Position.LocalMeters;
                passenger.FreeMotion.VelocityMetersPerSecond = body.Velocity;
                passenger.FreeWalkVelocity *= body.InputRetention;
                // 只修正主动输入的记账分量，实际碰撞速度已经由 Unity 解算。
                foreach (Vector2 contactNormal in body.ContactNormals)
                {
                    SpaceVector2 normal = SpaceVector2.FromVector2(contactNormal);
                    double inward = SpaceVector2.Dot(passenger.FreeWalkVelocity, normal);
                    if (inward < 0d) passenger.FreeWalkVelocity -= normal * inward;
                }
            }
            else if (body.LooseItem != null)
            {
                SpaceLooseItemState item = body.LooseItem;
                item.Motion.Sector = body.Position.Sector; item.Motion.PositionMeters = body.Position.LocalMeters;
                item.Motion.VelocityMetersPerSecond = body.Velocity;
                item.RotationDegrees = body.AngleRadians * Mathf.Rad2Deg;
                item.AngularVelocityDegreesPerSecond = body.AngularVelocityRadians * Mathf.Rad2Deg;
            }
        }

        private void ApplySpacePhysicsImpact(ShipAssemblyState assembly, double budget)
        {
            if (assembly == null || budget <= 0d) return;
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id);
                if (ship != null) ApplyLineImpact(ship, budget * ship.MassKg / Math.Max(.001d, assembly.MassKg), "船体碰撞");
            }
        }
        #endregion
    }
}
