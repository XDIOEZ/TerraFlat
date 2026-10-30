using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine.Pool;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 船体碰撞与手动降落的提交顺序
        private sealed class QueuedSurfaceContact
        {
            public ShipAssemblyState Assembly;
            public SurfaceContact Contact;
            public double AngularAcceleration;
            public bool IsLanding;
            public bool ApplyImpact = true;
        }
        private readonly Dictionary<string, QueuedSurfaceContact> pendingSurfaceContacts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, QueuedSurfaceContact> waitingSurfaceContacts = new(StringComparer.Ordinal);

        private bool IsWaitingForSurfaceContact(string shipId)
        {
            foreach (QueuedSurfaceContact contact in waitingSurfaceContacts.Values)
                if (contact.Assembly.MemberShipIds.Contains(shipId)) return true;
            return false;
        }

        // 地表下降保留真实触地时刻，先解算本步横移接触再提交落地。
        public void QueueSurfaceLanding(ShipAssemblyState assembly, SurfaceContact contact, bool applyImpact = true)
        {
            if (assembly == null || assembly.MemberShipIds.Count == 0 || !contact.IsValid || !SpaceVector2.Finite(contact.Fraction)) return;
            contact.Fraction = Math.Clamp(contact.Fraction, 0d, 1d);
            pendingSurfaceContacts[assembly.MemberShipIds[0]] = new QueuedSurfaceContact
                { Assembly = assembly, Contact = contact, IsLanding = true, ApplyImpact = applyImpact };
        }

        public void CommitSurfaceContacts(double seconds)
        {
            if (!GameNetwork.HasStateAuthority || State == null || Universe == null || seconds <= 0d) return;
            if (pendingSurfaceContacts.Count > 0 || waitingSurfaceContacts.Count > 0)
            {
                // 仅有真实触地事务时租用快照，允许落地回调继续登记下一批接触。
                using var lease = ListPool<QueuedSurfaceContact>.Get(out var contacts);
                contacts.AddRange(waitingSurfaceContacts.Values);
                contacts.AddRange(pendingSurfaceContacts.Values);
                contacts.Sort((left, right) => left.Contact.Fraction.CompareTo(right.Contact.Fraction));
                pendingSurfaceContacts.Clear();
                waitingSurfaceContacts.Clear();
                foreach (QueuedSurfaceContact candidate in contacts)
                {
                    string id = candidate.Assembly.MemberShipIds[0];
                    ShipState ship = GetShip(id);
                    ShipFlightState flight = GetFlight(id);
                    if (ship == null || flight == null) continue;
                    ShipAssemblyState assembly = GetConnectedAssembly(id);
                    if (assembly == null) continue;
                    if (candidate.IsLanding)
                    {
                        if (flight.LandingResolved || flight.Phase is not (ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)) continue;
                        SurfaceContact contact = candidate.Contact;
                        contact.RelativeVelocityMetersPerSecond = new SpaceVector2(
                            Math.Sqrt(assembly.VelocityX * assembly.VelocityX + assembly.VelocityY * assembly.VelocityY),
                            contact.RelativeVelocityMetersPerSecond.Y);
                        if (!LandAssembly(assembly, contact, candidate.ApplyImpact)) waitingSurfaceContacts[id] = candidate;
                    }
                }
            }
            // 已接受的降落指令使用本步碰撞后的最终姿态，不因漂出按钮范围或重积轨迹静默丢失。
            foreach (ShipAssemblyState assembly in queryAssemblies.Refresh(State.Ships, State.Docking))
            {
                ShipFlightState flight = GetFlight(assembly.MemberShipIds[0]);
                if (flight == null || flight.Phase is not (ShipFlightPhase.Cruising) ||
                    !TryGetBody(flight.RequestedLandingBodyId, out BodyState body)) continue;
                bool poweredLanding = CanBeginPoweredLanding(assembly, body.SurfaceGravity);
                SpaceVector2 position = SpaceSectorCoordinates.Relative(assembly.Sector,
                    new SpaceVector2(assembly.PositionX, assembly.PositionY), body.Sector);
                SurfaceContact contact = ShipLandingGuidance.CreateDescentContact(body, position,
                    new SpaceVector2(assembly.VelocityX, assembly.VelocityY), GetUniverse(body.Sector).State.SimulationSeconds);
                contact.Sector = body.Sector;
                BeginSurfaceDescent(assembly, contact, poweredLanding);
            }
        }

        private void ResetSurfaceContactFrame()
        {
            pendingSurfaceContacts.Clear();
        }

        private QueuedSurfaceContact FindQueuedSurfaceContact(ShipAssemblyState assembly)
        {
            foreach (string id in assembly.MemberShipIds)
                if (pendingSurfaceContacts.TryGetValue(id, out QueuedSurfaceContact candidate)) return candidate;
            return null;
        }

        private void ClearQueuedSurfaceContact(ShipContactBody body)
        {
            foreach (string id in body.Assembly.MemberShipIds) pendingSurfaceContacts.Remove(id);
            body.SurfaceContact = null;
        }

        // 冲量发生后重积剩余轨迹，平面碰撞保留原垂直触地时刻。
        private void RepredictContactBody(ShipContactBody body, double fraction, double seconds)
        {
            if (!body.Dynamic) return;
            QueuedSurfaceContact queued = FindQueuedSurfaceContact(body.Assembly);
            bool landing = queued?.IsLanding == true;
            ClearQueuedSurfaceContact(body);
            if (landing) body.SurfaceContact = queued.Contact;
            body.StartFraction = fraction;
            body.PreviousPosition = body.Position;
            body.PreviousVelocity = new SpaceVector2(body.Assembly.VelocityX, body.Assembly.VelocityY);
            body.PreviousAngle = body.Assembly.AngleRadians;
            body.PreviousAngularVelocity = body.Assembly.AngularVelocityRadiansPerSecond;
            foreach (ShipContactShape shape in body.Shapes)
                shape.Previous = new ShipContactPose(new SpaceVector2(shape.Ship.PositionX, shape.Ship.PositionY),
                    shape.Ship.AngleRadians, body.Realm, new SpaceVector2(shape.Ship.VelocityX, shape.Ship.VelocityY),
                    shape.Ship.AngularVelocityRadiansPerSecond, shape.Ship.Sector);

            body.EndFraction = landing ? queued.Contact.Fraction : 1d;
            double remaining = Math.Max(0d, (body.EndFraction - fraction) * seconds);
            body.Assembly.PositionX += body.Assembly.VelocityX * remaining;
            body.Assembly.PositionY += body.Assembly.VelocityY * remaining;
            body.Assembly.AngularVelocityRadiansPerSecond = body.PreviousAngularVelocity + body.AngularAcceleration * remaining;
            body.Assembly.AngleRadians = body.PreviousAngle + body.Assembly.AngularVelocityRadiansPerSecond * remaining;
            ShipDockingService.RigidlyCarryMembers(body.Assembly, body.Members);
            if (body.SurfaceContact is SurfaceContact next)
                pendingSurfaceContacts[body.Assembly.MemberShipIds[0]] = new QueuedSurfaceContact
                    { Assembly = body.Assembly, Contact = next, AngularAcceleration = body.AngularAcceleration,
                      IsLanding = landing, ApplyImpact = queued?.ApplyImpact ?? true };
        }

        #endregion
    }
}
