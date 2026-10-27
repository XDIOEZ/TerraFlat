using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkSpaceStateCoordinator
    {
        #region 按真实距离复制服务器投射物
        private sealed class ProjectileView
        {
            public Item Item;
            public NetworkSpaceProjectilePose Pose;
            public float Received;
            public uint Generation;
            public Transform Visual;
            public Vector3 BaseVisualPosition;
        }
        private readonly Dictionary<int, ProjectileView> projectileViews = new();
        private readonly List<int> expiredProjectileViews = new();
        private ulong projectileSequence, lastProjectileSequence;
        private void SendProjectilePoses(NetworkConnectionToClient connection, string profile)
        {
            SpaceSession session = SpaceSession.Current;
            if (!session.TryGetNetworkObserver(profile, out SpaceNetworkObserver observer)) return;
            var poses = new List<NetworkSpaceProjectilePose>(128);
            if (observer.InSpace)
                foreach (SpaceCombatFrame frame in SpaceCombatFrame.ActiveFrames)
                {
                    Item item = frame?.Projectile;
                    if (!frame.Active || item?.itemData == null || !SpaceSession.TryGetCombatPosition(item, item.transform.position, out SpaceSectorPosition position) ||
                        SpaceSectorCoordinates.Difference(position, observer.Position).Magnitude > ObserverRangeMeters) continue;
                    Mod_Projectile projectile = item.GetComponentInChildren<Mod_Projectile>(true);
                    poses.Add(new NetworkSpaceProjectilePose { Guid = item.itemData.Guid, ItemId = item.itemData.IDName,
                        Position = position, Velocity = SpaceVector2.FromVector2(projectile != null ? projectile.SpaceFlightVelocity : Vector2.zero), Rotation = item.transform.eulerAngles.z });
                    if (poses.Count == 128)
                    { connection.Send(new NetworkSpaceProjectilePoses { Sequence = ++projectileSequence, Poses = poses.ToArray() }); poses.Clear(); }
                }
            else
                foreach (Mod_Projectile projectile in Mod_Projectile.NetworkSurfaceProjectiles)
                {
                    Item item = projectile != null ? projectile.item : null;
                    if (item?.itemData == null || projectile.NetworkSurfaceWorldKey != observer.WorldKey) continue;
                    Vector2 logical = projectile.NetworkSurfaceLogicalPosition;
                    if (PressureExplosionQueue.DeltaInWorld(observer.WorldKey, observer.Position.LocalMeters.ToVector2(), logical).magnitude > ObserverRangeMeters) continue;
                    poses.Add(new NetworkSpaceProjectilePose { Guid = item.itemData.Guid, ItemId = item.itemData.IDName,
                        Position = new SpaceSectorPosition(default, SpaceVector2.FromVector2(logical)), SurfaceWorldKey = observer.WorldKey,
                        Velocity = SpaceVector2.FromVector2(projectile.NetworkSurfaceVelocity), Rotation = item.transform.eulerAngles.z,
                        VisualHeight = projectile.NetworkSurfaceVisualHeight, VisualRotation = projectile.NetworkSurfaceVisualRotation });
                    if (poses.Count == 128)
                    { connection.Send(new NetworkSpaceProjectilePoses { Sequence = ++projectileSequence, Poses = poses.ToArray() }); poses.Clear(); }
                }
            foreach (Mod_Projectile projectile in Mod_Projectile.LocalWorldProjectiles)
            {
                Item item = projectile != null ? projectile.item : null;
                SpaceSectorPosition position = default;
                Vector2 velocity = default;
                float rotation = 0f;
                string surfaceWorld = null;
                if (item?.itemData == null || !session.TryGetLocalWorldProjectilePose(projectile, out position,
                    out velocity, out rotation, out surfaceWorld) ||
                    observer.InSpace != string.IsNullOrEmpty(surfaceWorld) ||
                    !observer.InSpace && surfaceWorld != observer.WorldKey) continue;
                if (!observer.InSpace)
                    position = new SpaceSectorPosition(default, SpaceVector2.FromVector2(
                        PressureExplosionQueue.NormalizeInWorld(surfaceWorld, position.LocalMeters.ToVector2())));
                double distance = observer.InSpace ? SpaceSectorCoordinates.Difference(position, observer.Position).Magnitude :
                    PressureExplosionQueue.DeltaInWorld(surfaceWorld, observer.Position.LocalMeters.ToVector2(), position.LocalMeters.ToVector2()).magnitude;
                if (distance > ObserverRangeMeters) continue;
                poses.Add(new NetworkSpaceProjectilePose { Guid = item.itemData.Guid, ItemId = item.itemData.IDName,
                    Position = position, SurfaceWorldKey = surfaceWorld, Velocity = SpaceVector2.FromVector2(velocity),
                    Rotation = rotation, VisualHeight = projectile.NetworkSurfaceVisualHeight,
                    VisualRotation = projectile.NetworkSurfaceVisualRotation });
                if (poses.Count == 128)
                { connection.Send(new NetworkSpaceProjectilePoses { Sequence = ++projectileSequence, Poses = poses.ToArray() }); poses.Clear(); }
            }
            connection.Send(new NetworkSpaceProjectilePoses { Sequence = ++projectileSequence, Poses = poses.ToArray() });
        }
        private void OnProjectilePoses(NetworkSpaceProjectilePoses message)
        {
            if (NetworkServer.active || message.Sequence <= lastProjectileSequence || message.Poses == null || message.Poses.Length > 128) return;
            lastProjectileSequence = message.Sequence;
            SpaceSession session = SpaceSession.Current;
            NetworkWorldPlayer local = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (session == null || local == null || !local.TryGetSpaceObserver(out SpaceNetworkObserver observer) || ItemMgr.Instance == null || GameRes.ExistingInstance == null) return;
            foreach (NetworkSpaceProjectilePose pose in message.Poses)
            {
                if (pose.Guid == 0 || string.IsNullOrEmpty(pose.ItemId) || pose.ItemId.Length > 128 || !pose.Position.LocalMeters.IsFinite || !pose.Velocity.IsFinite ||
                    !float.IsFinite(pose.Rotation) || !float.IsFinite(pose.VisualHeight) || !float.IsFinite(pose.VisualRotation) ||
                    (pose.SurfaceWorldKey?.Length ?? 0) > 128 || !SharesProjectileRealm(pose, observer) || ProjectileDistance(pose, observer) > ObserverRangeMeters + 256d) continue;
                if (!projectileViews.TryGetValue(pose.Guid, out ProjectileView view))
                {
                    if (!GameRes.ExistingInstance.TryGetItemDefinition(pose.ItemId, out _)) continue;
                    Item item = ItemMgr.Instance.InstantiateItem(pose.ItemId, ProjectProjectilePosition(session, pose), Quaternion.Euler(0f, 0f, pose.Rotation));
                    item.PersistenceOwner = session;
                    item.itemData.Stack.CanBePickedUp = false;
                    item.Load();
                    ItemMgr.Instance.MarkAsRemoteVisualOnly(item);
                    foreach (Module module in item.GetComponentsInChildren<Module>(true)) module.enabled = false;
                    foreach (Collider2D collider in item.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
                    Rigidbody2D body = item.GetComponent<Rigidbody2D>(); if (body != null) body.simulated = false;
                    Transform visual = item.Sprite != null ? item.Sprite.transform : item.GetComponentInChildren<SpriteRenderer>(true)?.transform;
                    projectileViews.Add(pose.Guid, view = new ProjectileView { Item = item, Generation = item.RuntimeGeneration,
                        Visual = visual, BaseVisualPosition = visual != null ? visual.localPosition : Vector3.zero });
                }
                view.Pose = pose; view.Received = Time.unscaledTime;
            }
        }
        private void UpdateProjectileViews()
        {
            if (!clientStarted || NetworkServer.active) return;
            SpaceSession session = SpaceSession.Current;
            NetworkWorldPlayer local = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            SpaceNetworkObserver observer = default;
            bool hasObserver = local != null && local.TryGetSpaceObserver(out observer);
            expiredProjectileViews.Clear();
            foreach (var pair in projectileViews)
            {
                ProjectileView view = pair.Value;
                float elapsed = Time.unscaledTime - view.Received;
                if (!hasObserver || session == null || view.Item == null || !SharesProjectileRealm(view.Pose, observer) || elapsed > .6f)
                { expiredProjectileViews.Add(pair.Key); continue; }
                NetworkSpaceProjectilePose predicted = view.Pose;
                predicted.Position = string.IsNullOrEmpty(predicted.SurfaceWorldKey)
                    ? SpaceSectorCoordinates.Normalize(predicted.Position.Sector, predicted.Position.LocalMeters + predicted.Velocity * System.Math.Min(elapsed, .25f))
                    : new SpaceSectorPosition(default, predicted.Position.LocalMeters + predicted.Velocity * System.Math.Min(elapsed, .25f));
                view.Item.transform.SetPositionAndRotation(ProjectProjectilePosition(session, predicted), Quaternion.Euler(0f, 0f, predicted.Rotation));
                if (view.Visual != null && view.Visual != view.Item.transform && !string.IsNullOrEmpty(predicted.SurfaceWorldKey))
                {
                    view.Visual.localPosition = view.BaseVisualPosition + view.Visual.parent.InverseTransformVector(Vector3.up * predicted.VisualHeight);
                    view.Visual.localRotation = Quaternion.Euler(0f, 0f, predicted.VisualRotation);
                }
            }
            foreach (int id in expiredProjectileViews)
            { DespawnProjectileView(projectileViews[id]); projectileViews.Remove(id); }
        }
        private static void DespawnProjectileView(ProjectileView view)
        { if (view.Item != null && !view.Item.DestructionHandled && view.Item.RuntimeGeneration == view.Generation && view.Item.gameObject.activeSelf) ItemMgr.Instance?.DespawnItem(view.Item, saveData: false); }
        private void ClearProjectileViews()
        { foreach (ProjectileView view in projectileViews.Values) DespawnProjectileView(view); projectileViews.Clear(); lastProjectileSequence = 0; }
        private static bool SharesProjectileRealm(NetworkSpaceProjectilePose pose, SpaceNetworkObserver observer)
            => string.IsNullOrEmpty(pose.SurfaceWorldKey) ? observer.InSpace : !observer.InSpace && pose.SurfaceWorldKey == observer.WorldKey;
        private static double ProjectileDistance(NetworkSpaceProjectilePose pose, SpaceNetworkObserver observer)
            => observer.InSpace ? SpaceSectorCoordinates.Difference(pose.Position, observer.Position).Magnitude
                : PressureExplosionQueue.DeltaInWorld(observer.WorldKey, observer.Position.LocalMeters.ToVector2(), pose.Position.LocalMeters.ToVector2()).magnitude;
        private static Vector2 ProjectProjectilePosition(SpaceSession session, NetworkSpaceProjectilePose pose)
            => string.IsNullOrEmpty(pose.SurfaceWorldKey) ? session.ProjectPosition(pose.Position) : WorldLocalPresentation.ProjectPosition(pose.Position.LocalMeters.ToVector2());
        #endregion
    }
}
