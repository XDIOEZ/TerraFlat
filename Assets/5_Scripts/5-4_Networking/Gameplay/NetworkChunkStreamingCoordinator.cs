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

                Vector2Int chunkPosition = Chunk.GetChunkPosition(position);
                observerPositions.Add(position);
                unchecked
                {
                    signature = signature * 31 + observers[i].GetInstanceID();
                    signature = signature * 31 + chunkPosition.GetHashCode();
                }
            }

            if (observerPositions.Count == 0)
                return;

            if (signature == lastObserverSignature)
                return;

            lastObserverSignature = signature;
            ChunkMgr.Instance.RefreshChunksAroundObservers(
                observerPositions,
                loadDistance,
                inactiveDistance,
                destroyDistance);

            RefreshLocalNavigationAnchor();

            Debug.Log($"[联机区块] 已按 {observerPositions.Count} 个玩家刷新区块窗口");
        }

        private void RefreshLocalNavigationAnchor()
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

            Vector2Int anchorChunk = Chunk.GetChunkPosition(anchor.position);
            if (anchorChunk == lastNavigationAnchorChunk)
                return;

            lastNavigationAnchorChunk = anchorChunk;
            AstarGameManager.Instance?.RefreshNavMeshAsync(anchorChunk, loadDistance);
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
