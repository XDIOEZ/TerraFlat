using System;
using System.Collections.Generic;
using FlatWorld.Networking;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 同一步的船体与星体接触顺序
        private sealed class QueuedSurfaceContact
        {
            public ShipAssemblyState Assembly;
            public SurfaceContact Contact;
            public double AngularAcceleration;
            public bool IsLanding;
        }
        private readonly Dictionary<string, QueuedSurfaceContact> pendingSurfaceContacts = new(StringComparer.Ordinal);

        // 先保留真实触星体时刻，船体接触结束前不切换坐标系。
        public void QueueSurfaceContact(ShipAssemblyState assembly, SurfaceContact contact, double seconds)
        {
            if (assembly == null || assembly.MemberShipIds.Count == 0 || !contact.IsValid || seconds <= 0d) return;
            string id = assembly.MemberShipIds[0];
            double fraction = Math.Clamp(contact.Fraction, 0d, 1d), angularAcceleration = 0d;
            if (shipContactStarts.TryGetValue(id, out ShipContactPose previous) && previous.Realm == "space")
            {
                angularAcceleration = (assembly.AngularVelocityRadiansPerSecond - previous.AngularVelocity) / seconds;
                assembly.AngleRadians = previous.Angle + (assembly.AngleRadians - previous.Angle) * fraction;
                assembly.AngularVelocityRadiansPerSecond = previous.AngularVelocity + angularAcceleration * seconds * fraction;
                ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
            }
            contact.Fraction = fraction;
            pendingSurfaceContacts[id] = new QueuedSurfaceContact
                { Assembly = assembly, Contact = contact, AngularAcceleration = angularAcceleration };
        }

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
                else if (flight.Phase is ShipFlightPhase.Cruising or ShipFlightPhase.Descending)
                {
                    if (flight.RequestedLandingBodyId == candidate.Contact.BodyId)
                        BeginSurfaceDescent(assembly, candidate.Contact);
                    else
                    {
                        SpaceVector2 position = new(assembly.PositionX, assembly.PositionY);
                        SpaceVector2 velocity = new(assembly.VelocityX, assembly.VelocityY);
                        ShipLandingGuidance.ResolveExteriorContact(Universe.GetBody(candidate.Contact.BodyId), AssemblyRadius(assembly),
                            candidate.Contact, ref position, ref velocity);
                        assembly.PositionX = position.X; assembly.PositionY = position.Y;
                        assembly.VelocityX = velocity.X; assembly.VelocityY = velocity.Y;
                        ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
                        foreach (string memberId in assembly.MemberShipIds)
                        {
                            ShipState member = GetShip(memberId); ShipFlightState memberFlight = GetFlight(memberId);
                            memberFlight.Motion.PositionMeters = new SpaceVector2(member.PositionX, member.PositionY);
                            memberFlight.Motion.VelocityMetersPerSecond = new SpaceVector2(member.VelocityX, member.VelocityY);
                            UpdateDescent(memberFlight, member);
                        }
                    }
                }
            }
            // 已接受的降落指令使用本步碰撞后的最终姿态，不因漂出按钮范围或重积轨迹静默丢失。
            foreach (ShipAssemblyState assembly in queryAssemblies.Refresh(State.Ships, State.Docking))
            {
                ShipFlightState flight = GetFlight(assembly.MemberShipIds[0]);
                if (flight == null || flight.Phase is not (ShipFlightPhase.Cruising or ShipFlightPhase.Descending) ||
                    !Universe.TryGetBody(flight.RequestedLandingBodyId, out BodyState body)) continue;
                BeginSurfaceDescent(assembly, ShipLandingGuidance.CreateDescentContact(body,
                    new SpaceVector2(assembly.PositionX, assembly.PositionY),
                    new SpaceVector2(assembly.VelocityX, assembly.VelocityY), Universe.State.SimulationSeconds));
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
            double actualSeconds = remaining;
            if (body.Realm == "space")
            {
                var motion = new SpaceMotionState { PositionMeters = body.Position, VelocityMetersPerSecond = body.PreviousVelocity,
                    RadiusMeters = AssemblyRadius(body.Assembly) };
                double startTime = Math.Max(0d, Universe.State.SimulationSeconds - seconds + fraction * seconds);
                SurfaceContact contact;
                bool touched = remaining > 1e-12d
                    ? Universe.StepMotionAtTime(motion, default, startTime, remaining, out contact)
                    : Universe.TrySweepSurfaceContact(motion.PositionMeters, motion.PositionMeters, motion.VelocityMetersPerSecond,
                        motion.VelocityMetersPerSecond, motion.RadiusMeters, startTime, 0d, out contact);
                body.Assembly.PositionX = motion.PositionMeters.X; body.Assembly.PositionY = motion.PositionMeters.Y;
                body.Assembly.VelocityX = motion.VelocityMetersPerSecond.X; body.Assembly.VelocityY = motion.VelocityMetersPerSecond.Y;
                if (touched)
                {
                    actualSeconds = remaining * contact.Fraction;
                    contact.Fraction = fraction + (1d - fraction) * contact.Fraction;
                    body.EndFraction = contact.Fraction;
                    body.SurfaceContact = contact;
                }
            }
            else
            {
                body.Assembly.PositionX += body.Assembly.VelocityX * remaining;
                body.Assembly.PositionY += body.Assembly.VelocityY * remaining;
            }
            body.Assembly.AngularVelocityRadiansPerSecond = body.PreviousAngularVelocity + body.AngularAcceleration * actualSeconds;
            body.Assembly.AngleRadians = body.PreviousAngle + body.Assembly.AngularVelocityRadiansPerSecond * actualSeconds;
            ShipDockingService.RigidlyCarryMembers(body.Assembly, body.Members);
            if (body.SurfaceContact is SurfaceContact next)
                pendingSurfaceContacts[body.Assembly.MemberShipIds[0]] = new QueuedSurfaceContact
                    { Assembly = body.Assembly, Contact = next, AngularAcceleration = body.AngularAcceleration, IsLanding = landing };
        }

        private void RecheckContactSeparationAgainstSurface(ShipContactBody body, SpaceVector2 from)
        {
            if (body.Realm != "space" || body.SurfaceStopped) return;
            SpaceVector2 velocity = new(body.Assembly.VelocityX, body.Assembly.VelocityY);
            if (!Universe.TrySweepSurfaceContact(from, body.Position, velocity, velocity, AssemblyRadius(body.Assembly),
                Universe.State.SimulationSeconds, 0d, out SurfaceContact contact)) return;
            body.Assembly.PositionX = contact.PositionMeters.X; body.Assembly.PositionY = contact.PositionMeters.Y;
            ShipDockingService.RigidlyCarryMembers(body.Assembly, body.Members);
            contact.Fraction = 1d;
            body.EndFraction = 1d; body.SurfaceContact = contact; body.SurfaceStopped = true;
            pendingSurfaceContacts[body.Assembly.MemberShipIds[0]] = new QueuedSurfaceContact
                { Assembly = body.Assembly, Contact = contact, AngularAcceleration = body.AngularAcceleration };
        }
        #endregion
    }
}
