using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 太空掉落生成来源
        private sealed class ShipDroppedItemSpawnProvider : IDroppedItemSpawnProvider
        {
            private readonly SpaceSession session;
            public ShipDroppedItemSpawnProvider(SpaceSession value) => session = value;
            public bool CanHandle(in DroppedItemSpawnContext context) => session != null && session.CanOwnDroppedItem(context);
            public DroppedItemHandle SpawnDrop(ItemData source, in DroppedItemSpawnContext context, float rotation, Vector3 scale)
                => session.SpawnSessionDrop(source, context, rotation, scale);
        }
        public IDisposable RegisterDroppedItemProvider()
            => DroppedItemService.RegisterSpawnProvider(new ShipDroppedItemSpawnProvider(this));

        private bool CanOwnDroppedItem(in DroppedItemSpawnContext context)
        {
            if (!running || State == null || Universe == null || !ReferenceEquals(Current, this) ||
                !ReferenceEquals(owner, SaveDataMgr.Instance?.SaveData)) return false;
            Item source = context.SourceItem;
            if (source != null && source.PersistenceOwner != null && !ReferenceEquals(source.PersistenceOwner, this)) return false;
            if (source != null && SpaceMgr.IsSpaceScene(source.gameObject.scene.name)) return true;
            if (source == null && IsSpaceView) return true;
            if (source != null && ReferenceEquals(source.PersistenceOwner, this))
            {
                foreach (var view in looseViews)
                    if (ReferenceEquals(view.Value, source))
                        return State.LooseItems.Find(value => value.Id == view.Key) is { InSpace: true } or { SurfaceHeight: > 0d };
            }
            Player player = FindDroppedItemSourcePlayer(source, context.Start);
            SpacePassengerState passenger = player == null ? null : State.Passengers.Find(value => value.ProfileId == player.ProfileName);
            if (passenger?.SurfaceDescending == true && passenger.SurfaceHeight > 0d) return true;
            if (!TryGetSupport(context.Start, out ShipState ship, out _, out _)) return false;
            ShipFlightState flight = GetFlight(ship.ShipId);
            return flight is { HeightMeters: > 0d } && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending;
        }

        private DroppedItemHandle SpawnSessionDrop(ItemData source, in DroppedItemSpawnContext context, float rotation, Vector3 scale)
        {
            if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || !CanOwnDroppedItem(context))
                throw new InvalidOperationException("当前掉落物不能移交给太空会话。");
            if (!float.IsFinite(context.Start.x) || !float.IsFinite(context.Start.y) ||
                !float.IsFinite(context.End.x) || !float.IsFinite(context.End.y) || !float.IsFinite(rotation))
                throw new ArgumentException("太空掉落的起点、终点和朝向必须是有限数。");
            ItemPhysicsDefinitionDto physics = GameRes.ExistingInstance != null &&
                GameRes.ExistingInstance.TryGetItemDefinition(source.IDName, out RuntimeItemDefinition definition)
                    ? definition.Physics : null;
            if (physics != null && (physics.Mode != WorldPhysicsProxyMode2D.IndependentDynamic || physics.HasRegions))
                throw new InvalidOperationException("太空掉落暂不支持固定模式或自定义物理区域。");
            if (ReferenceEquals(context.SourceItem?.itemData, source)) context.SourceItem.ModuleSave();
            ItemData payload = FastCloner.FastCloner.DeepClone(source);
            payload.Guid = AllocateSessionDropGuid(source.Guid);
            payload.inHand = false; payload.Stack.CanBePickedUp = true;
            DroppedItemService.RemoveLegacyDropData(payload);
            SpaceLooseItemState state = CreateSessionDropMotion(context, rotation, scale);
            if (physics != null && (!state.InSpace || !string.IsNullOrEmpty(state.SupportShipId)))
                throw new InvalidOperationException("配置物理的掉落物暂不支持飞船支撑或地表过渡。");
            if (physics?.FreezeRotation == true) state.AngularVelocityDegreesPerSecond = 0d;
            // 与普通掉落一样，投掷未完成时不能立刻被原地自动捡回。
            state.PickupAfterSimulationSeconds = State.SimulationSeconds + Math.Max(.5d, context.Duration);
            state.Snapshot = Encode(payload);
            Item live = null;
            try
            {
                Transform root = ItemMgr.Instance.GetRuntimeEntityRoot(SceneManager.GetActiveScene());
                GameObject shell = new("SpaceDrop_" + payload.IDName);
                if (root != null) shell.transform.SetParent(root, false);
                shell.transform.SetPositionAndRotation(context.Start, Quaternion.Euler(0f, 0f, rotation));
                shell.transform.localScale = scale;
                SpaceDropView drop = shell.AddComponent<SpaceDropView>();
                live = drop;
                drop.Initialize(payload, this);
                State.LooseItems.Add(state); looseViews.Add(state.Id, live); looseData[state.Id] = live.itemData;
                live.SetInHand(false);
                Rigidbody2D body = live.GetComponent<Rigidbody2D>();
                if (body != null)
                {
                    body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0f;
                    body.velocity = Vector2.zero; body.angularVelocity = 0f;
                    body.position = context.Start; body.rotation = rotation;
                }
                live.itemData.Stack.CanBePickedUp = true;
                state.Snapshot = Encode(live.itemData);
                return new DroppedItemHandle(live.itemData.Guid, 0, live);
            }
            catch
            {
                State?.LooseItems.Remove(state); looseViews.Remove(state.Id); looseData.Remove(state.Id);
                if (live != null && !live.DestructionHandled && !live.IsInPool) DespawnLooseView(live);
                throw;
            }
        }
        private int AllocateSessionDropGuid(int sourceGuid)
        {
            int id;
            bool exists;
            do
            {
                id = Guid.NewGuid().GetHashCode() & int.MaxValue;
                exists = id == 0 || id == sourceGuid || ItemMgr.Instance.GetItemByGuid(id) != null || Owns(id) ||
                    State.Ships.Exists(ship => ship.WreckGuid == id);
                if (!exists)
                    foreach (SpaceLooseItemState loose in State.LooseItems)
                    {
                        if (!looseData.TryGetValue(loose.Id, out ItemData saved)) looseData[loose.Id] = saved = Decode(loose.Snapshot);
                        if (saved.Guid == id) { exists = true; break; }
                    }
            } while (exists);
            return id;
        }
        #endregion

        #region 初速度与来源移动继承
        private SpaceLooseItemState CreateSessionDropMotion(in DroppedItemSpawnContext context, float rotation, Vector3 scale)
        {
            SpaceSectorPosition point = IsSpaceView ? UnprojectSectorPosition(context.Start) : default;
            var state = new SpaceLooseItemState { InSpace = IsSpaceView, WorldKey = SceneManager.GetActiveScene().name,
                RotationDegrees = rotation, Scale = scale,
                Motion = new SpaceMotionState { Sector = point.Sector, PositionMeters = IsSpaceView ? point.LocalMeters
                    : SpaceVector2.FromVector2(WorldLocalPresentation.ToLogical(context.Start)), RadiusMeters = .15d } };
            bool inherited = false;
            Item source = context.SourceItem;
            if (source != null && ReferenceEquals(source.PersistenceOwner, this))
                foreach (var view in looseViews)
                    if (ReferenceEquals(view.Value, source) && State.LooseItems.Find(value => value.Id == view.Key) is SpaceLooseItemState original)
                    {
                        state.InSpace = original.InSpace; state.WorldKey = original.WorldKey;
                        state.Motion.VelocityMetersPerSecond = original.Motion.VelocityMetersPerSecond;
                        state.AngularVelocityDegreesPerSecond = original.AngularVelocityDegreesPerSecond;
                        state.SurfaceHeight = original.SurfaceHeight; state.SurfaceVerticalSpeed = original.SurfaceVerticalSpeed;
                        inherited = true; break;
                    }
            Player player = FindDroppedItemSourcePlayer(source, context.Start);
            SpacePassengerState passenger = player == null ? null : GetPassenger(player);
            ShipState carrier = passenger?.Supported == true ? GetShip(passenger.ShipId) : null;
            if (!IsSpaceView && passenger != null && !inherited && (passenger.Supported || passenger.SurfaceDescending))
            {
                // 手持物已经随玩家根节点升高，掉落运动只保存去掉高度后的地面起点。
                Vector3 ground = ShipSurfacePresentation.GroundPosition(player.gameObject, context.Start);
                state.Motion.PositionMeters = SpaceVector2.FromVector2(WorldLocalPresentation.ToLogical(ground));
            }
            if (IsSpaceView && passenger != null && !inherited)
            {
                // 丢弃起点保留手持物的局部偏移，但映射回当前权威姿态。
                SpaceSectorPosition authority = GetAuthoritativePassengerActionPoint(player, passenger, context.Start);
                state.Motion.Sector = authority.Sector; state.Motion.PositionMeters = authority.LocalMeters;
            }
            if (carrier == null && passenger == null && TryGetSupport(context.Start, out ShipState floorShip, out _, out _)) carrier = floorShip;
            if (!inherited && carrier != null)
            {
                ShipFlightState flight = GetFlight(carrier.ShipId);
                state.InSpace = flight.Phase is ShipFlightPhase.Cruising;
                state.WorldKey = flight.SurfaceWorldKey;
                SpaceVector2 carrierPoint = state.InSpace ? SpaceSectorCoordinates.Relative(state.Motion.Sector,
                    state.Motion.PositionMeters, carrier.Sector) : state.Motion.PositionMeters;
                ShipGeometry.PointVelocity(carrier, carrierPoint.X, carrierPoint.Y, out double vx, out double vy);
                state.Motion.VelocityMetersPerSecond = new SpaceVector2(vx, vy);
                state.AngularVelocityDegreesPerSecond = carrier.AngularVelocityRadiansPerSecond * 180d / Math.PI;
                if (!state.InSpace) { state.SurfaceHeight = Math.Max(0d, flight.HeightMeters); state.SurfaceVerticalSpeed = flight.VerticalSpeed; }
                inherited = true;
            }
            if (!inherited && passenger != null)
            {
                if (passenger.SurfaceDescending)
                {
                    state.InSpace = false; state.WorldKey = passenger.LandingWorldKey;
                    state.SurfaceHeight = passenger.SurfaceHeight; state.SurfaceVerticalSpeed = passenger.SurfaceVerticalSpeed;
                    state.Motion.VelocityMetersPerSecond = new SpaceVector2(passenger.SurfaceHorizontalSpeed, 0d);
                }
                else
                {
                    state.Motion.VelocityMetersPerSecond = passenger.FreeMotion.VelocityMetersPerSecond;
                }
                inherited = true;
            }
            if (!inherited && source?.itemData?.PhysicsState != null)
                state.Motion.VelocityMetersPerSecond = SpaceVector2.FromVector2(source.itemData.PhysicsState.Velocity);
            if (context.Duration > 0f)
            {
                Vector2 displacement = context.End - context.Start;
                if (!state.InSpace) displacement = WorldTopologyRuntime.ShortestDelta(context.Start, context.End);
                state.Motion.VelocityMetersPerSecond += SpaceVector2.FromVector2(displacement) / context.Duration;
            }
            if (!state.Motion.PositionMeters.IsFinite || !state.Motion.VelocityMetersPerSecond.IsFinite)
                throw new InvalidOperationException("太空掉落的初始运动状态无效。");
            if (state.InSpace)
            {
                if (carrier != null)
                {
                    SpaceVector2 localPoint = SpaceSectorCoordinates.Relative(state.Motion.Sector, state.Motion.PositionMeters, carrier.Sector);
                    ShipGeometry.WorldToLocal(carrier, localPoint.X, localPoint.Y, out double lx, out double ly);
                    if (HasArtificialGravity(carrier, lx, ly))
                    {
                        Vector2 displacement = context.End - context.Start;
                        ShipGeometry.Rotate(displacement.x, displacement.y, -carrier.AngleRadians, out double dx, out double dy);
                        state.SupportShipId = carrier.ShipId;
                        state.SupportLocalX = lx; state.SupportLocalY = ly;
                        state.DropRemainingSeconds = Math.Max(.01d, context.Duration);
                        state.DropVelocityX = dx / state.DropRemainingSeconds;
                        state.DropVelocityY = dy / state.DropRemainingSeconds;
                    }
                }
                SpaceSectorCoordinates.Normalize(ref state.Motion.Sector, ref state.Motion.PositionMeters);
                state.WorldKey = SpaceSectorCoordinates.SceneKey(state.Motion.Sector);
            }
            return state;
        }
        private Player FindDroppedItemSourcePlayer(Item source, Vector2 start)
        {
            Item candidate = source;
            for (int i = 0; candidate != null && i < 16; i++)
            {
                if (candidate is Player player) return player;
                if (ReferenceEquals(candidate.Owner, candidate)) break;
                candidate = candidate.Owner;
            }
            Player local = ItemMgr.Instance?.User_Player;
            bool legacyDrop = source == null || source.Owner == null && source.PersistenceOwner == null &&
                GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetItemDefinition(source.itemData.IDName, out RuntimeItemDefinition definition) && !definition.IsActor;
            return legacyDrop && local != null && Vector2.Distance(local.transform.position, start) <= .5f ? local : null;
        }
        #endregion
    }
}
