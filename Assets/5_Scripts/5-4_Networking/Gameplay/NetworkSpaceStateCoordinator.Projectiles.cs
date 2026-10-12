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
            connection.Send(new NetworkSpaceProjectilePoses { Sequence = ++projectileSequence, Poses = poses.ToArray() });
        }
        private void OnProjectilePoses(NetworkSpaceProjectilePoses message)
        {
            if (NetworkServer.active || message.Sequence <= lastProjectileSequence || message.Poses == null || message.Poses.Length > 128) return;
            lastProjectileSequence = message.Sequence;
            SpaceSession session = SpaceSession.Current;
            NetworkWorldPlayer local = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (session == null || local?.IsInSpace != true || ItemMgr.Instance == null || GameRes.ExistingInstance == null) return;
            foreach (NetworkSpaceProjectilePose pose in message.Poses)
            {
                if (pose.Guid == 0 || string.IsNullOrEmpty(pose.ItemId) || pose.ItemId.Length > 128 || !pose.Position.LocalMeters.IsFinite || !pose.Velocity.IsFinite ||
                    !float.IsFinite(pose.Rotation) || SpaceSectorCoordinates.Difference(pose.Position, local.AuthoritativeSpacePosition).Magnitude > ObserverRangeMeters + 256d) continue;
                if (!projectileViews.TryGetValue(pose.Guid, out ProjectileView view))
                {
                    if (!GameRes.ExistingInstance.TryGetItemDefinition(pose.ItemId, out _)) continue;
                    Item item = ItemMgr.Instance.InstantiateItem(pose.ItemId, session.ProjectPosition(pose.Position), Quaternion.Euler(0f, 0f, pose.Rotation));
                    item.PersistenceOwner = session;
                    item.itemData.Stack.CanBePickedUp = false;
                    item.Load();
                    ItemMgr.Instance.MarkAsRemoteVisualOnly(item);
                    foreach (Module module in item.GetComponentsInChildren<Module>(true)) module.enabled = false;
                    foreach (Collider2D collider in item.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
                    Rigidbody2D body = item.GetComponent<Rigidbody2D>(); if (body != null) body.simulated = false;
                    projectileViews.Add(pose.Guid, view = new ProjectileView { Item = item, Generation = item.RuntimeGeneration });
                }
                view.Pose = pose; view.Received = Time.unscaledTime;
            }
        }
        private void UpdateProjectileViews()
        {
            if (!clientStarted || NetworkServer.active) return;
            SpaceSession session = SpaceSession.Current;
            bool inSpace = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.IsInSpace == true;
            expiredProjectileViews.Clear();
            foreach (var pair in projectileViews)
            {
                ProjectileView view = pair.Value;
                float elapsed = Time.unscaledTime - view.Received;
                if (!inSpace || session == null || view.Item == null || elapsed > .6f) { expiredProjectileViews.Add(pair.Key); continue; }
                SpaceSectorPosition position = SpaceSectorCoordinates.Normalize(view.Pose.Position.Sector,
                    view.Pose.Position.LocalMeters + view.Pose.Velocity * System.Math.Min(elapsed, .25f));
                view.Item.transform.SetPositionAndRotation(session.ProjectPosition(position), Quaternion.Euler(0f, 0f, view.Pose.Rotation));
            }
            foreach (int id in expiredProjectileViews)
            { DespawnProjectileView(projectileViews[id]); projectileViews.Remove(id); }
        }
        private static void DespawnProjectileView(ProjectileView view)
        { if (view.Item != null && !view.Item.DestructionHandled && view.Item.RuntimeGeneration == view.Generation && view.Item.gameObject.activeSelf) ItemMgr.Instance?.DespawnItem(view.Item, saveData: false); }
        private void ClearProjectileViews()
        { foreach (ProjectileView view in projectileViews.Values) DespawnProjectileView(view); projectileViews.Clear(); lastProjectileSequence = 0; }
        #endregion
    }
}
