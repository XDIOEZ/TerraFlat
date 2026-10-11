using System;
using System.Collections.Generic;
using FlatWorld.Networking;

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
        }
        private readonly Dictionary<string, QueuedSurfaceContact> pendingSurfaceContacts = new(StringComparer.Ordinal);

        // 地表下降保留真实触地时刻，先解算本步横移接触再提交落地。
        public void QueueSurfaceLanding(ShipAssemblyState assembly, SurfaceContact contact)
        {
            if (assembly == null || assembly.MemberShipIds.Count == 0 || !contact.IsValid || !SpaceVector2.Finite(contact.Fraction)) return;
            contact.Fraction = Math.Clamp(contact.Fraction, 0d, 1d);
            pendingSurfaceContacts[assembly.MemberShipIds[0]] = new QueuedSurfaceContact
                { Assembly = assembly, Contact = contact, IsLanding = true };
        }

        public void CommitSurfaceContacts(double seconds)
        {
            if (!GameNetwork.HasStateAuthority || State == null || Universe == null || seconds <= 0d) return;
            var contacts = new List<QueuedSurfaceContact>(pendingSurfaceContacts.Values);
            contacts.Sort((left, right) => left.Contact.Fraction.CompareTo(right.Contact.Fraction));
            pendingSurfaceContacts.Clear();
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
                    LandAssembly(assembly, contact);
                }
            }
            // 已接受的降落指令使用本步碰撞后的最终姿态，不因漂出按钮范围或重积轨迹静默丢失。
            foreach (ShipAssemblyState assembly in queryAssemblies.Refresh(State.Ships, State.Docking))
            {
                ShipFlightState flight = GetFlight(assembly.MemberShipIds[0]);
                if (flight == null || flight.Phase is not (ShipFlightPhase.Cruising) ||
                    !Universe.TryGetBody(flight.RequestedLandingBodyId, out BodyState body)) continue;
                bool poweredLanding = TryConsumeLandingFuel(assembly, seconds);
                BeginSurfaceDescent(assembly, ShipLandingGuidance.CreateDescentContact(body,
                    new SpaceVector2(assembly.PositionX, assembly.PositionY),
                    new SpaceVector2(assembly.VelocityX, assembly.VelocityY), Universe.State.SimulationSeconds), poweredLanding);
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
                    shape.Ship.AngleRadians, body.Realm, new SpaceVector2(shape.Ship.VelocityX, shape.Ship.VelocityY), shape.Ship.AngularVelocityRadiansPerSecond);

            body.EndFraction = landing ? queued.Contact.Fraction : 1d;
            double remaining = Math.Max(0d, (body.EndFraction - fraction) * seconds);
            if (body.Realm == "space")
            {
                var motion = new SpaceMotionState { PositionMeters = body.Position, VelocityMetersPerSecond = body.PreviousVelocity,
                    RadiusMeters = AssemblyRadius(body.Assembly) };
                Universe.StepFreeMotion(motion, default, remaining);
                body.Assembly.PositionX = motion.PositionMeters.X; body.Assembly.PositionY = motion.PositionMeters.Y;
                body.Assembly.VelocityX = motion.VelocityMetersPerSecond.X; body.Assembly.VelocityY = motion.VelocityMetersPerSecond.Y;
            }
            else
            {
                body.Assembly.PositionX += body.Assembly.VelocityX * remaining;
                body.Assembly.PositionY += body.Assembly.VelocityY * remaining;
            }
            body.Assembly.AngularVelocityRadiansPerSecond = body.PreviousAngularVelocity + body.AngularAcceleration * remaining;
            body.Assembly.AngleRadians = body.PreviousAngle + body.Assembly.AngularVelocityRadiansPerSecond * remaining;
            ShipDockingService.RigidlyCarryMembers(body.Assembly, body.Members);
            if (body.SurfaceContact is SurfaceContact next)
                pendingSurfaceContacts[body.Assembly.MemberShipIds[0]] = new QueuedSurfaceContact
                    { Assembly = body.Assembly, Contact = next, AngularAcceleration = body.AngularAcceleration, IsLanding = landing };
        }

        #endregion
    }
}
