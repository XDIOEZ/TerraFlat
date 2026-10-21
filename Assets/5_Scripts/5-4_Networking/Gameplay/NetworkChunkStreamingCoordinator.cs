using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private readonly List<Vector3> observerPositions = new List<Vector3>();
        private readonly Dictionary<string, List<Vector3>> worldObservers = new(System.StringComparer.Ordinal);
        private readonly Dictionary<string, int> worldSignatures = new(System.StringComparer.Ordinal);
        private int lastObserverSignature;
        private Vector2Int lastNavigationAnchorChunk = new Vector2Int(int.MinValue, int.MinValue);
        private int lastNavigationLoadDistance = -1;
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
                instance.lastObserverSignature = int.MinValue;
            }
        }

        public static void Unregister(Transform observer)
        {
            if (instance == null || observer == null)
                return;

            instance.observers.Remove(observer);
            instance.lastObserverSignature = int.MinValue;
            instance.nextRefreshTime = 0f;
            if (instance.observers.Count == 0)
                instance.ReleaseObserverWindows();
        }

        public static void RequestImmediateRefresh()
        {
            if (instance == null)
                return;

            instance.lastObserverSignature = int.MinValue;
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
            DontDestroyOnLoad(coordinatorObject);
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
                Vector3 point = realm.Position.LocalMeters.ToVector2();
                if (!IsValidObserverPosition(point)) continue;
                if (!worldObservers.TryGetValue(realm.WorldKey, out List<Vector3> positions)) worldObservers.Add(realm.WorldKey, positions = new List<Vector3>());
                positions.Add(point);
            }
            // 每个世界只合并属于自己的观察者，主机换维度不会撤销其它世界的需求。
            foreach (var pair in worldObservers)
            {
                if (pair.Value.Count == 0)
                {
                    if (GameplayWorldRegistry.TryGet(pair.Key, out GameplayWorldRuntime oldWorld) && oldWorld.PreparationCount == 0)
                        using (GameplayWorldRegistry.UseWorld(pair.Key)) oldWorld.Chunks.ClearRuntimeObserverWindows();
                    worldSignatures.Remove(pair.Key); continue;
                }
                GameplayWorldRuntime world = GameplayWorldRegistry.GetOrCreate(pair.Key);
                using (GameplayWorldRegistry.UseWorld(pair.Key))
                {
                    Vector3 anchor = localAnchor != null && localAnchor.GetComponent<NetworkWorldPlayer>()?.CorePlayer?.Data?.CurrentSceneName == pair.Key
                        ? ResolveLogicalObserverPosition(localAnchor) : pair.Value[0];
                    int signature = active * 31 + prefetch * 17 + retained;
                    foreach (Vector3 point in pair.Value) unchecked { signature = signature * 31 + world.Chunks.ResolveRuntimeChunkOrigin(point).GetHashCode(); }
                    world.Chunks.UpdateRuntimePresentationPriority(anchor);
                    if (!worldSignatures.TryGetValue(pair.Key, out int previous) || previous != signature)
                    {
                        worldSignatures[pair.Key] = signature;
                        world.Chunks.RefreshChunksAroundObservers(pair.Value, anchor, active, prefetch, retained);
                    }
                }
            }
            if (localAnchor != null)
            {
                string key = localAnchor.GetComponent<NetworkWorldPlayer>()?.CorePlayer?.Data?.CurrentSceneName;
                if (GameplayWorldRegistry.TryGet(key, out _))
                    using (GameplayWorldRegistry.UseWorld(key)) RefreshLocalNavigationAnchor(ResolveLogicalObserverPosition(localAnchor), active);
            }
        }

        /// <summary>退出联机或失去本机观察者时释放本协调器的需求。</summary>
        private void ReleaseObserverWindows()
        {
            foreach (string key in worldSignatures.Keys)
                if (GameplayWorldRegistry.TryGet(key, out GameplayWorldRuntime world) && world.PreparationCount == 0)
                    using (GameplayWorldRegistry.UseWorld(key)) world.Chunks.ClearRuntimeObserverWindows();
            worldSignatures.Clear();
            lastObserverSignature = int.MinValue;
            lastNavigationAnchorChunk = new Vector2Int(int.MinValue, int.MinValue);
            lastNavigationLoadDistance = -1;
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
            Vector2Int anchorChunk = ChunkMgr.Instance.ResolveRuntimeChunkOrigin(logicalPosition);
            if (anchorChunk == lastNavigationAnchorChunk && activeDistance == lastNavigationLoadDistance)
                return;

            lastNavigationAnchorChunk = anchorChunk;
            lastNavigationLoadDistance = activeDistance;
            WorldNavigationManager.Instance?.RefreshLoadedRegion(anchorChunk, activeDistance);
        }

        #endregion

        private static bool IsValidObserverPosition(Vector3 position)
        {
            return !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
                   !float.IsNaN(position.y) && !float.IsInfinity(position.y) &&
                   Mathf.Abs(position.x) <= MaxSupportedWorldCoordinate &&
                   Mathf.Abs(position.y) <= MaxSupportedWorldCoordinate;
        }

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
