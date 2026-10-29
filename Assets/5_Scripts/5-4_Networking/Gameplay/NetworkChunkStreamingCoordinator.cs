using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 每个客户端维护一份网络玩家观察者集合，并把区块窗口并集提交给 ChunkMgr。
    /// </summary>
    public sealed class NetworkChunkStreamingCoordinator : MonoBehaviour
    {
        private const float MaxSupportedWorldCoordinate = 100000f;

        private static NetworkChunkStreamingCoordinator instance;

        private readonly List<Transform> observers = new List<Transform>();
        private readonly Dictionary<string, List<Vector3>> worldObservers = new(System.StringComparer.Ordinal);
        private readonly Dictionary<string, int> worldSignatures = new(System.StringComparer.Ordinal);
        private readonly Dictionary<string, GameplayWorldRuntime> signatureWorlds = new(System.StringComparer.Ordinal);
        private readonly List<string> inactiveWorldKeys = new();
        private Vector2Int lastNavigationAnchorChunk = new Vector2Int(int.MinValue, int.MinValue);
        private int lastNavigationLoadDistance = -1;
        private WorldNavigationManager lastNavigationManager;
        private float nextRefreshTime;

        [SerializeField, Min(1)] private int loadDistance = 2;
        [SerializeField, Min(1)] private int inactiveDistance = 3;
        [SerializeField, Min(1)] private int destroyDistance = 5;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.12f;

        public static void Register(Transform observer)
        {
            if (observer == null)
                return;

            EnsureInstance();
            if (!instance.observers.Contains(observer))
            {
                instance.observers.Add(observer);
                instance.worldSignatures.Clear();
            }
        }

        public static void Unregister(Transform observer)
        {
            if (instance == null || observer == null)
                return;

            instance.observers.Remove(observer);
            instance.worldSignatures.Clear();
            instance.nextRefreshTime = 0f;
            if (instance.observers.Count == 0)
                instance.ReleaseObserverWindows();
        }

        public static void RequestImmediateRefresh()
        {
            if (instance == null)
                return;

            instance.worldSignatures.Clear();
            instance.nextRefreshTime = 0f;
        }

        private void OnEnable()
        {
            FlatWorld.Spaceflight.SpaceNetworkBridge.GroundStreamingRefreshRequested += RequestImmediateRefresh;
            WorldTopologyRuntime.LocalPlayerWrapped += RequestImmediateRefresh;
        }

        private void OnDisable()
        {
            FlatWorld.Spaceflight.SpaceNetworkBridge.GroundStreamingRefreshRequested -= RequestImmediateRefresh;
            WorldTopologyRuntime.LocalPlayerWrapped -= RequestImmediateRefresh;
            ReleaseObserverWindows();
        }

        private static void EnsureInstance()
        {
            if (instance != null)
                return;

            GameObject coordinatorObject = new GameObject("NetworkChunkStreamingCoordinator");
            instance = coordinatorObject.AddComponent<NetworkChunkStreamingCoordinator>();
            PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(coordinatorObject, PersistentRuntimeObjectCategory.Networking);
        }

        private void Update()
        {
            if (!NetworkClient.active && !NetworkServer.active)
            { ReleaseObserverWindows(); return; }
            if (Time.unscaledTime < nextRefreshTime) return;
            nextRefreshTime = Time.unscaledTime + refreshInterval;
            observers.RemoveAll(observer => observer == null);
            if (GameManager.Instance?.IsInGameWorld != true || SaveDataMgr.Instance?.SaveData?.PlanetData_Dict == null) return;
            foreach (List<Vector3> positions in worldObservers.Values) positions.Clear();
            inactiveWorldKeys.Clear();
            Transform localAnchor = ResolveLocalObserver();
            Mod_ChunkLoader loader = localAnchor?.GetComponent<NetworkWorldPlayer>()?.CorePlayer?.GetComponentInChildren<Mod_ChunkLoader>(true);
            int active = loader != null ? loader.CurrentLoadChunkDistance : loadDistance;
            int prefetch = loader != null ? loader.CurrentUnActiveChunkDistance : active + Mathf.Max(1, inactiveDistance - loadDistance);
            int retained = loader != null ? loader.CurrentDestroyChunkDistance : prefetch + Mathf.Max(1, destroyDistance - inactiveDistance);
            for (int i = 0; i < observers.Count; i++)
            {
                NetworkWorldPlayer player = observers[i].GetComponent<NetworkWorldPlayer>();
                if (player == null || player.IsInSpace || !NetworkServer.active && !player.SharesLocalRealm() ||
                    !player.TryGetSpaceObserver(out FlatWorld.Spaceflight.SpaceNetworkObserver realm) || string.IsNullOrWhiteSpace(realm.WorldKey)) continue;
                // 本机雾窗和区块需求都跟随同一个预测位置，不能等旧服务器位置跨块才补地形。
                bool ownsLocalSurface = player.isOwned && player.CorePlayer?.Data?.CurrentSceneName == realm.WorldKey;
                Vector3 point = ResolveObserverWindowPosition(ownsLocalSurface,
                    ownsLocalSurface ? player.ObserverLogicalPosition : default, realm.Position.LocalMeters.ToVector2());
                if (!IsValidObserverPosition(point)) continue;
                if (!worldObservers.TryGetValue(realm.WorldKey, out List<Vector3> positions)) worldObservers.Add(realm.WorldKey, positions = new List<Vector3>());
                positions.Add(point);
            }
            // 每个世界只合并属于自己的观察者，主机换维度不会撤销其它世界的需求。
            foreach (var pair in worldObservers)
            {
                if (pair.Value.Count == 0)
                {
                    // 目标维度仍在准备时保留待清理键，避免误撤准备窗口。
                    bool hasWorld = GameplayWorldRegistry.TryGet(pair.Key, out GameplayWorldRuntime oldWorld);
                    if (hasWorld && oldWorld.PreparationCount > 0)
                        continue;
                    if (hasWorld)
                        using (GameplayWorldRegistry.UseWorld(pair.Key)) oldWorld.Chunks.ClearRuntimeObserverWindows();
                    inactiveWorldKeys.Add(pair.Key);
                    worldSignatures.Remove(pair.Key);
                    signatureWorlds.Remove(pair.Key);
                    continue;
                }
                GameplayWorldRuntime world = GameplayWorldRegistry.GetOrCreate(pair.Key);
                using (GameplayWorldRegistry.UseWorld(pair.Key))
                {
                    Vector3 anchor = localAnchor != null && localAnchor.GetComponent<NetworkWorldPlayer>()?.CorePlayer?.Data?.CurrentSceneName == pair.Key
                        ? ResolveLogicalObserverPosition(localAnchor) : pair.Value[0];
                    int signature = active * 31 + prefetch * 17 + retained;
                    foreach (Vector3 point in pair.Value) unchecked { signature = signature * 31 + world.Chunks.ResolveRuntimeChunkOrigin(point).GetHashCode(); }
                    world.Chunks.UpdateRuntimePresentationPriority(anchor);
                    // 同名维度重建后的窗口必须重新提交，不能复用旧实例的签名。
                    if (!signatureWorlds.TryGetValue(pair.Key, out GameplayWorldRuntime cachedWorld) ||
                        !ReferenceEquals(cachedWorld, world) ||
                        !worldSignatures.TryGetValue(pair.Key, out int previous) || previous != signature)
                    {
                        world.Chunks.RefreshChunksAroundObservers(pair.Value, anchor, active, prefetch, retained);
                        worldSignatures[pair.Key] = signature;
                        signatureWorlds[pair.Key] = world;
                    }
                }
            }
            for (int i = 0; i < inactiveWorldKeys.Count; i++)
                worldObservers.Remove(inactiveWorldKeys[i]);
            if (localAnchor != null)
            {
                string key = localAnchor.GetComponent<NetworkWorldPlayer>()?.CorePlayer?.Data?.CurrentSceneName;
                if (GameplayWorldRegistry.TryGet(key, out _))
                {
                    using (GameplayWorldRegistry.UseWorld(key)) RefreshLocalNavigationAnchor(ResolveLogicalObserverPosition(localAnchor), active);
                    return;
                }
            }
            // 离开地表或本地世界不可用时，重进同一格也必须恢复导航窗口。
            lastNavigationManager = null;
        }

        /// <summary>退出联机或失去本机观察者时释放本协调器的需求。</summary>
        private void ReleaseObserverWindows()
        {
            foreach (string key in worldSignatures.Keys)
                if (GameplayWorldRegistry.TryGet(key, out GameplayWorldRuntime world) && world.PreparationCount == 0)
                    using (GameplayWorldRegistry.UseWorld(key)) world.Chunks.ClearRuntimeObserverWindows();
            worldSignatures.Clear();
            signatureWorlds.Clear();
            worldObservers.Clear();
            inactiveWorldKeys.Clear();
            lastNavigationAnchorChunk = new Vector2Int(int.MinValue, int.MinValue);
            lastNavigationLoadDistance = -1;
            lastNavigationManager = null;
        }

        #region 本机观察者

        /// <summary>只有本机拥有的地表角色可以决定本机加载蒙版和导航锚点。</summary>
        private Transform ResolveLocalObserver()
        {
            for (int i = 0; i < observers.Count; i++)
            {
                Transform observer = observers[i];
                NetworkIdentity identity = observer != null ? observer.GetComponent<NetworkIdentity>() : null;
                NetworkWorldPlayer player = observer != null ? observer.GetComponent<NetworkWorldPlayer>() : null;
                if (identity != null && identity.isOwned && player != null &&
                    !player.IsInSpace && player.SharesLocalRealm())
                    return observer;
            }
            return null;
        }

        /// <summary>本地导航窗口仅跟随本机角色。</summary>
        private void RefreshLocalNavigationAnchor(Vector3 logicalPosition, int activeDistance)
        {
            WorldNavigationManager navigation = WorldNavigationManager.Instance;
            if (navigation == null) return;
            Vector2Int anchorChunk = ChunkMgr.Instance.ResolveRuntimeChunkOrigin(logicalPosition);
            if (ReferenceEquals(navigation, lastNavigationManager) &&
                anchorChunk == lastNavigationAnchorChunk && activeDistance == lastNavigationLoadDistance)
                return;

            lastNavigationAnchorChunk = anchorChunk;
            lastNavigationLoadDistance = activeDistance;
            lastNavigationManager = navigation;
            navigation.RefreshLoadedRegion(anchorChunk, activeDistance);
        }

        #endregion

        private static bool IsValidObserverPosition(Vector3 position)
        {
            return !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
                   !float.IsNaN(position.y) && !float.IsInfinity(position.y) &&
                   Mathf.Abs(position.x) <= MaxSupportedWorldCoordinate &&
                   Mathf.Abs(position.y) <= MaxSupportedWorldCoordinate;
        }

        internal static Vector3 ResolveObserverWindowPosition(bool ownsLocalSurface, Vector3 predicted, Vector3 authoritative)
            => ownsLocalSurface && IsValidObserverPosition(predicted) ? predicted : authoritative;

        /// <summary>客户端网络 Transform 可处于局部表现镜像；区块与导航只消费规范逻辑坐标。</summary>
        private static Vector3 ResolveLogicalObserverPosition(Transform observer)
        {
            if (observer == null)
                return default;

            NetworkWorldPlayer networkPlayer = observer.GetComponent<NetworkWorldPlayer>();
            return networkPlayer != null
                ? networkPlayer.ObserverLogicalPosition
                : WorldTopologyRuntime.NormalizePosition(observer.position);
        }
    }
}
