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
            if (source != null && source.gameObject.scene.name == "SpaceScene") return true;
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
            if (ReferenceEquals(context.SourceItem?.itemData, source)) context.SourceItem.ModuleSave();
            ItemData payload = FastCloner.FastCloner.DeepClone(source);
            payload.Guid = AllocateSessionDropGuid(source.Guid);
            payload.inHand = false; payload.Stack.CanBePickedUp = true;
            DroppedItemService.RemoveLegacyDropData(payload);
            SpaceLooseItemState state = CreateSessionDropMotion(context, rotation, scale);
            state.Snapshot = Encode(payload);
            Item live = null;
            try
            {
                Transform root = ItemMgr.Instance.GetRuntimeEntityRoot(SceneManager.GetActiveScene());
                live = ItemMgr.Instance.InstantiateItem(payload, context.Start, Quaternion.Euler(0f, 0f, rotation), scale, root != null ? root.gameObject : null);
                live.PersistenceOwner = this;
                // 真实物品只负责模块与拾取，轨迹由唯一太空模拟时钟推进。
                Mod_Droping oldFlight = live.itemMods.GetMod_ByID<Mod_Droping>(ModText.Drop);
                if (oldFlight != null) Module.REMOVEModFROMItem(live, oldFlight._Data);
                State.LooseItems.Add(state); looseViews.Add(state.Id, live); looseData[state.Id] = live.itemData;
                live.Load(); live.SetInHand(false);
                if (live.DestructionHandled || live.IsInPool || ItemMgr.Instance.GetItemByGuid(payload.Guid) != live)
                    throw new InvalidOperationException("太空掉落物在生成时提前结束了生命周期。");
                Rigidbody2D body = live.GetComponent<Rigidbody2D>();
                if (body != null)
                {
                    body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0f;
                    body.velocity = Vector2.zero; body.angularVelocity = 0f;
                    body.position = context.Start; body.rotation = rotation;
                }
                live.itemData.Stack.CanBePickedUp = true;
                live.Save(); state.Snapshot = Encode(live.itemData);
                ItemMgr.Instance.NotifyRuntimeItemMoved(live);
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
                exists = id == 0 || id == sourceGuid || ItemMgr.Instance.GetItemByGuid(id) != null || Owns(id);
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

        #region 初速度与原参考系继承
        private SpaceLooseItemState CreateSessionDropMotion(in DroppedItemSpawnContext context, float rotation, Vector3 scale)
        {
            var state = new SpaceLooseItemState { InSpace = IsSpaceView, WorldKey = SceneManager.GetActiveScene().name,
                RotationDegrees = rotation, Scale = scale,
                Motion = new OrbitState { PositionMeters = IsSpaceView ? UnprojectPosition(context.Start)
                    : SpaceVector2.FromVector2(WorldLocalPresentation.ToLogical(context.Start)), RadiusMeters = .15d } };
            bool inherited = false;
            Item source = context.SourceItem;
            if (source != null && ReferenceEquals(source.PersistenceOwner, this))
                foreach (var view in looseViews)
                    if (ReferenceEquals(view.Value, source) && State.LooseItems.Find(value => value.Id == view.Key) is SpaceLooseItemState original)
                    {
                        state.InSpace = original.InSpace; state.WorldKey = original.WorldKey;
                        state.Motion.VelocityMetersPerSecond = original.Motion.VelocityMetersPerSecond;
                        state.Motion.ReferenceBodyId = original.Motion.ReferenceBodyId;
                        state.Motion.CapturedBodyId = original.Motion.CapturedBodyId;
                        state.SurfaceHeight = original.SurfaceHeight; state.SurfaceVerticalSpeed = original.SurfaceVerticalSpeed;
                        inherited = true; break;
                    }
            Player player = FindDroppedItemSourcePlayer(source, context.Start);
            SpacePassengerState passenger = player == null ? null : GetPassenger(player);
            ShipState carrier = passenger?.Supported == true ? GetShip(passenger.ShipId) : null;
            if (carrier == null && passenger == null && TryGetSupport(context.Start, out ShipState floorShip, out _, out _)) carrier = floorShip;
            if (!inherited && carrier != null)
            {
                ShipFlightState flight = GetFlight(carrier.ShipId);
                state.InSpace = flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending;
                state.WorldKey = flight.SurfaceWorldKey;
                ShipGeometry.PointVelocity(carrier, state.Motion.PositionMeters.X, state.Motion.PositionMeters.Y, out double vx, out double vy);
                state.Motion.VelocityMetersPerSecond = new SpaceVector2(vx, vy);
                state.Motion.ReferenceBodyId = flight.Orbit.ReferenceBodyId; state.Motion.CapturedBodyId = flight.Orbit.CapturedBodyId;
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
                    state.Motion.ReferenceBodyId = passenger.FreeMotion.ReferenceBodyId;
                    state.Motion.CapturedBodyId = passenger.FreeMotion.CapturedBodyId;
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
