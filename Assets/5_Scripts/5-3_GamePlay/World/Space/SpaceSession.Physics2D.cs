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
                body.FreeCircle = false; body.FreezeRotation = true;
                body.Position = new SpaceSectorPosition(assembly.Sector, new SpaceVector2(assembly.PositionX, assembly.PositionY));
                body.Velocity = new SpaceVector2(assembly.VelocityX, assembly.VelocityY);
                body.AngleRadians = body.AngularVelocityRadians = 0d;
                body.MassKg = assembly.MassKg; body.InertiaKgM2 = assembly.InertiaKgM2;
                body.RadiusMeters = AssemblyRadius(assembly);
                ShipState anchorShip = GetShip(assembly.MemberShipIds[0]);
                if (anchorShip?.Anchor != null)
                {
                    body.Kinematic = true;
                    body.KinematicTarget = GetAnchoredShipPosition(anchorShip, State.SimulationSeconds);
                    body.KinematicTargetVelocity = GetUniverse(GetBody(anchorShip.Anchor.BodyId).Sector).GetVelocityAtTime(anchorShip.Anchor.BodyId, State.SimulationSeconds);
                }
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
                if (!item.InSpace || !string.IsNullOrEmpty(item.SupportShipId) || string.IsNullOrWhiteSpace(item.Snapshot)) continue;
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
            body.ShapeShips.Clear();
            for (int memberIndex = 0; memberIndex < assembly.MemberShipIds.Count; memberIndex++)
            {
                string id = assembly.MemberShipIds[memberIndex];
                ShipState ship = GetShip(id);
                if (ship == null) continue;
                body.ShapeShips.Add(ship);
                if (!body.ShipGeometries.TryGetValue(ship, out ShipColliderGeometry geometry))
                    body.ShipGeometries.Add(ship, geometry = new ShipColliderGeometry());
                geometry.Synchronize(ship);
                ShipAssemblyMemberState member = memberIndex < assembly.MemberTransforms.Count &&
                    assembly.MemberTransforms[memberIndex].ShipId == id ? assembly.MemberTransforms[memberIndex] : null;
                for (int rectangleIndex = 0; rectangleIndex < geometry.Rectangles.Count; rectangleIndex++)
                {
                    ShipCollisionRect rectangle = geometry.Rectangles[rectangleIndex];
                    Vector2 local = SpacePhysicsShipOffset(ship, (rectangle.X + rectangle.Width * .5d) * ship.CellSizeMeters,
                        (rectangle.Y + rectangle.Height * .5d) * ship.CellSizeMeters, assembly, member);
                    body.Shapes.Add(new SpacePhysicsShape(local, new Vector2((float)(rectangle.Width * ship.CellSizeMeters),
                        (float)(rectangle.Height * ship.CellSizeMeters)),
                        (float)((member?.RelativeAngleRadians ?? ship.AngleRadians - assembly.AngleRadians) * Mathf.Rad2Deg),
                        ignoreFreeCircles: !rectangle.BlocksPassage));
                }
                foreach (SpacePassengerState passenger in State.Passengers)
                {
                    if (!passenger.Supported || passenger.ShipId != id) continue;
                    // 支撑乘员的质量已计入整船，碰撞圆也归同一个原生刚体，避免重复计重。
                    body.Shapes.Add(new SpacePhysicsShape(SpacePhysicsShipOffset(ship, passenger.LocalX, passenger.LocalY, assembly, member),
                        new Vector2(.3f, .3f), circle: true));
                }
            }
            body.RemovedShapeShips.Clear();
            foreach (ShipState ship in body.ShipGeometries.Keys)
                if (!body.ShapeShips.Contains(ship)) body.RemovedShapeShips.Add(ship);
            foreach (ShipState ship in body.RemovedShapeShips) body.ShipGeometries.Remove(ship);
            body.ShapeShips.Clear(); body.RemovedShapeShips.Clear();
        }

        private static Vector2 SpacePhysicsShipOffset(ShipState ship, double localX, double localY,
            ShipAssemblyState assembly, ShipAssemblyMemberState member)
        {
            if (member != null)
            {
                // 直接用成员相对变换，避免整船平移和旋转造成形状坐标往返换算漂移。
                ShipGeometry.Rotate(localX - ship.CenterOfMassLocalX, localY - ship.CenterOfMassLocalY,
                    member.RelativeAngleRadians, out double offsetX, out double offsetY);
                return new Vector2((float)(member.OffsetX + offsetX), (float)(member.OffsetY + offsetY));
            }
            ShipGeometry.LocalToWorld(ship, localX, localY, out double worldX, out double worldY);
            SpaceVector2 delta = SpaceSectorCoordinates.Relative(ship.Sector, new SpaceVector2(worldX, worldY), assembly.Sector) -
                new SpaceVector2(assembly.PositionX, assembly.PositionY);
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
