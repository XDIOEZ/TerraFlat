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
            WorldTopologyRuntime.LocalPlayerWrapped += RequestImmediateRefresh;
        }

        private void OnDisable()
        {
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
            if (!NetworkClient.active)
            {
                ReleaseObserverWindows();
                return;
            }
            if (Time.unscaledTime < nextRefreshTime) return;

            nextRefreshTime = Time.unscaledTime + refreshInterval;
            observers.RemoveAll(observer => observer == null);
            if (observers.Count == 0 || GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
            {
                ReleaseObserverWindows();
                return;
            }

            if (ChunkMgr.Instance == null || SaveDataMgr.Instance?.Active_PlanetData == null)
                return;

            Transform localAnchor = ResolveLocalObserver();
            if (localAnchor == null)
            {
                ReleaseObserverWindows();
                return;
            }
            Vector3 localPosition = ResolveLogicalObserverPosition(localAnchor);
            if (!IsValidObserverPosition(localPosition))
            {
                ReleaseObserverWindows();
                return;
            }
            Mod_ChunkLoader localLoader = localAnchor.GetComponent<NetworkWorldPlayer>()?.CorePlayer?.GetComponentInChildren<Mod_ChunkLoader>(true);
            int signature = SceneManager.GetActiveScene().handle;
            observerPositions.Clear();
            for (int i = 0; i < observers.Count; i++)
            {
                NetworkWorldPlayer realmPlayer = observers[i].GetComponent<NetworkWorldPlayer>();
                if (realmPlayer != null && (realmPlayer.IsInSpace || !realmPlayer.SharesLocalRealm())) continue;
                Vector3 position = ResolveLogicalObserverPosition(observers[i]);
                if (!IsValidObserverPosition(position))
                    continue;

                Vector2Int chunkPosition = ChunkMgr.Instance.ResolveRuntimeChunkOrigin(position);
                observerPositions.Add(position);
                unchecked
                {
                    signature = signature * 31 + observers[i].GetInstanceID();
                    signature = signature * 31 + chunkPosition.GetHashCode();
                }
            }

            if (observerPositions.Count == 0)
            {
                ReleaseObserverWindows();
                return;
            }

            int activeDistance = localLoader != null ? localLoader.CurrentLoadChunkDistance : loadDistance;
            int prefetchDistance = localLoader != null ? localLoader.CurrentUnActiveChunkDistance
                : activeDistance + Mathf.Max(1, inactiveDistance - loadDistance);
            int retainedDistance = localLoader != null ? localLoader.CurrentDestroyChunkDistance
                : prefetchDistance + Mathf.Max(1, destroyDistance - inactiveDistance);
            unchecked
            {
                signature = signature * 31 + activeDistance;
                signature = signature * 31 + prefetchDistance;
                signature = signature * 31 + retainedDistance;
            }

            // 单一区块内移动只改变本机画面优先级，不重新构造所有观察者窗口。
            ChunkMgr.Instance.UpdateRuntimePresentationPriority(localPosition);
            if (signature == lastObserverSignature) return;
            lastObserverSignature = signature;
            ChunkMgr.Instance.RefreshChunksAroundObservers(
                observerPositions,
                localPosition,
                activeDistance,
                prefetchDistance,
                retainedDistance);

            RefreshLocalNavigationAnchor(localPosition, activeDistance);

            Debug.Log($"[联机区块] 已按 {observerPositions.Count} 个玩家刷新区块窗口");
        }

        /// <summary>退出联机或失去本机观察者时释放本协调器的需求。</summary>
        private void ReleaseObserverWindows()
        {
            ChunkMgr.ExistingInstance?.ClearRuntimeObserverWindows();
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
