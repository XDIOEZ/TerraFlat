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
            if (!NetworkClient.active || Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + refreshInterval;
            observers.RemoveAll(observer => observer == null);
            if (observers.Count == 0 || GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
                return;

            if (ChunkMgr.Instance == null || SaveDataMgr.Instance?.Active_PlanetData == null)
                return;

            int signature = 17;
            observerPositions.Clear();
            for (int i = 0; i < observers.Count; i++)
            {
                Vector3 position = observers[i].position;
                if (!IsValidObserverPosition(position))
                    continue;

                Vector2Int chunkPosition = ChunkMgr.NormalizeChunkPosition(Chunk.GetChunkPosition(position));
                observerPositions.Add(position);
                unchecked
                {
                    signature = signature * 31 + observers[i].GetInstanceID();
                    signature = signature * 31 + chunkPosition.GetHashCode();
                }
            }

            if (observerPositions.Count == 0)
                return;

            int activeDistance = ResolveLocalLoadDistance();
            int prefetchDistance = activeDistance + Mathf.Max(1, inactiveDistance - loadDistance);
            int retainedDistance = prefetchDistance + Mathf.Max(1, destroyDistance - inactiveDistance);
            unchecked
            {
                signature = signature * 31 + activeDistance;
            }

            if (signature == lastObserverSignature)
                return;

            lastObserverSignature = signature;
            ChunkMgr.Instance.RefreshChunksAroundObservers(
                observerPositions,
                activeDistance,
                prefetchDistance,
                retainedDistance);

            RefreshLocalNavigationAnchor(activeDistance);

            Debug.Log($"[联机区块] 已按 {observerPositions.Count} 个玩家刷新区块窗口");
        }

        /// <summary>联机流送窗口读取本机玩家的区块距离，其他观察者复用该客户端的配置。</summary>
        private int ResolveLocalLoadDistance()
        {
            for (int i = 0; i < observers.Count; i++)
            {
                Transform observer = observers[i];
                NetworkIdentity identity = observer != null ? observer.GetComponent<NetworkIdentity>() : null;
                if (identity == null || !identity.isOwned)
                    continue;

                NetworkWorldPlayer networkPlayer = observer.GetComponent<NetworkWorldPlayer>();
                Mod_ChunkLoader loader = networkPlayer?.CorePlayer?.GetComponentInChildren<Mod_ChunkLoader>(true);
                if (loader != null)
                    return loader.CurrentLoadChunkDistance;
            }

            return loadDistance;
        }

        /// <summary>本地导航窗口与联机区块加载距离保持一致。</summary>
        private void RefreshLocalNavigationAnchor(int activeDistance)
        {
            Transform anchor = null;
            for (int i = 0; i < observers.Count; i++)
            {
                Transform observer = observers[i];
                NetworkIdentity identity = observer != null ? observer.GetComponent<NetworkIdentity>() : null;
                if (identity != null && identity.isOwned)
                {
                    anchor = observer;
                    break;
                }
            }

            anchor ??= observers.Count > 0 ? observers[0] : null;
            if (anchor == null || !IsValidObserverPosition(anchor.position))
                return;

            Vector2Int anchorChunk = ChunkMgr.NormalizeChunkPosition(Chunk.GetChunkPosition(anchor.position));
            if (anchorChunk == lastNavigationAnchorChunk && activeDistance == lastNavigationLoadDistance)
                return;

            lastNavigationAnchorChunk = anchorChunk;
            lastNavigationLoadDistance = activeDistance;
            WorldNavigationManager.Instance?.RefreshLoadedRegion(anchorChunk, activeDistance);
        }

        private static bool IsValidObserverPosition(Vector3 position)
        {
            return !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
                   !float.IsNaN(position.y) && !float.IsInfinity(position.y) &&
                   Mathf.Abs(position.x) <= MaxSupportedWorldCoordinate &&
                   Mathf.Abs(position.y) <= MaxSupportedWorldCoordinate;
        }
    }
}
