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
        }
        private readonly Dictionary<string, QueuedSurfaceContact> pendingSurfaceContacts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SpaceVector2> orbitContactAccelerations = new(StringComparer.Ordinal);

        public void RecordOrbitContactAcceleration(ShipAssemblyState assembly, SpaceVector2 acceleration)
        {
            if (assembly == null || !acceleration.IsFinite) return;
            foreach (string id in assembly.MemberShipIds) orbitContactAccelerations[id] = acceleration;
        }

        // 先保留真实触星体时刻，船体接触结束前不切换坐标系。
        public void QueueSurfaceContact(ShipAssemblyState assembly, SurfaceContact contact, double seconds)
        {
            if (assembly == null || assembly.MemberShipIds.Count == 0 || !contact.IsValid || seconds <= 0d) return;
            string id = assembly.MemberShipIds[0];
            double fraction = Math.Clamp(contact.Fraction, 0d, 1d), angularAcceleration = 0d;
            if (shipContactStarts.TryGetValue(id, out ShipContactPose previous) && previous.Realm == "orbit")
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
                if (ship == null || flight == null || flight.Phase is not (ShipFlightPhase.Orbit or ShipFlightPhase.Descending)) continue;
                ShipAssemblyState assembly = GetConnectedAssembly(id);
                if (assembly != null) BeginSurfaceDescent(assembly, candidate.Contact);
            }
        }

        private void ResetSurfaceContactFrame()
        {
            pendingSurfaceContacts.Clear();
            orbitContactAccelerations.Clear();
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

        // 冲量发生后从真实碰撞点重新积分剩余时间，旧落地候选随之失效。
        private void RepredictContactBody(ShipContactBody body, double fraction, double seconds)
        {
            if (!body.Dynamic) return;
            ClearQueuedSurfaceContact(body);
            body.StartFraction = fraction;
            body.PreviousPosition = body.Position;
            body.PreviousVelocity = new SpaceVector2(body.Assembly.VelocityX, body.Assembly.VelocityY);
            body.PreviousAngle = body.Assembly.AngleRadians;
            body.PreviousAngularVelocity = body.Assembly.AngularVelocityRadiansPerSecond;
            foreach (ShipContactShape shape in body.Shapes)
                shape.Previous = new ShipContactPose(new SpaceVector2(shape.Ship.PositionX, shape.Ship.PositionY),
                    shape.Ship.AngleRadians, body.Realm, new SpaceVector2(shape.Ship.VelocityX, shape.Ship.VelocityY), shape.Ship.AngularVelocityRadiansPerSecond);

            double remaining = Math.Max(0d, (1d - fraction) * seconds);
            body.EndFraction = 1d;
            double actualSeconds = remaining;
            if (body.Realm == "orbit")
            {
                ShipFlightState flight = GetFlight(body.Assembly.MemberShipIds[0]);
                var orbit = new OrbitState { PositionMeters = body.Position, VelocityMetersPerSecond = body.PreviousVelocity,
                    ReferenceBodyId = flight?.Orbit.ReferenceBodyId, RadiusMeters = AssemblyRadius(body.Assembly) };
                orbitContactAccelerations.TryGetValue(body.Assembly.MemberShipIds[0], out SpaceVector2 acceleration);
                double startTime = Math.Max(0d, Universe.State.SimulationSeconds - seconds + fraction * seconds);
                SurfaceContact contact;
                bool touched = remaining > 1e-12d
                    ? Universe.StepOrbitAtTime(orbit, acceleration, startTime, remaining, out contact)
                    : Universe.TrySweepSurfaceContact(orbit.PositionMeters, orbit.PositionMeters, orbit.VelocityMetersPerSecond,
                        orbit.VelocityMetersPerSecond, orbit.RadiusMeters, startTime, 0d, out contact);
                body.Assembly.PositionX = orbit.PositionMeters.X; body.Assembly.PositionY = orbit.PositionMeters.Y;
                body.Assembly.VelocityX = orbit.VelocityMetersPerSecond.X; body.Assembly.VelocityY = orbit.VelocityMetersPerSecond.Y;
                foreach (string memberId in body.Assembly.MemberShipIds)
                    if (GetFlight(memberId) is ShipFlightState member)
                    {
                        member.Orbit.ReferenceBodyId = orbit.ReferenceBodyId;
                        member.Orbit.CapturedBodyId = orbit.CapturedBodyId;
                        member.Orbit.RelativeAltitudeMeters = orbit.RelativeAltitudeMeters;
                    }
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
                    { Assembly = body.Assembly, Contact = next, AngularAcceleration = body.AngularAcceleration };
        }

        private void RecheckContactSeparationAgainstSurface(ShipContactBody body, SpaceVector2 from)
        {
            if (body.Realm != "orbit" || body.SurfaceStopped) return;
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
