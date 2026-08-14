using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ChunkMgr 的联机观察者扩展。
/// 联机模式以所有网络玩家的区块窗口并集为准，避免两个玩家互相卸载对方周围的区块。
/// </summary>
public partial class ChunkMgr
{
    /// <summary>
    /// 按多个观察者的位置统一加载、失活和销毁区块。
    /// 原有单玩家接口保持不变，离线模式不会受影响。
    /// </summary>
    public void RefreshChunksAroundObservers(
        IReadOnlyList<Vector3> observerPositions,
        int loadDistance = 2,
        int inactiveDistance = 3,
        int destroyDistance = 5)
    {
        if (observerPositions == null || observerPositions.Count == 0)
            return;

        loadDistance = Mathf.Max(1, loadDistance);
        inactiveDistance = Mathf.Max(loadDistance, inactiveDistance);
        destroyDistance = Mathf.Max(inactiveDistance, destroyDistance);

        RefreshChunkStepCache();
        if (_cachedChunkSize.x <= 0f || _cachedChunkSize.y <= 0f)
            return;

        HashSet<Vector2Int> loadWindow = BuildObserverWindow(observerPositions, loadDistance);
        HashSet<Vector2Int> keepAliveWindow = BuildObserverWindow(observerPositions, inactiveDistance);

        foreach (Vector2Int keepPos in keepAliveWindow)
            CancelDeferredChunkDeactivation(keepPos);

        ResetChunkLoadQueue();

        if (loadWindow.Count > 0)
        {
            foreach (Vector2Int chunkPos in loadWindow)
                RequestLoadChunk_By_Position(chunkPos);
        }

        List<Vector2Int> deactivateBuffer = new List<Vector2Int>();
        foreach (Vector2Int activePos in Chunk_Dic_Active_ByPos.Keys)
        {
            if (!keepAliveWindow.Contains(activePos))
                deactivateBuffer.Add(activePos);
        }

        for (int i = 0; i < deactivateBuffer.Count; i++)
        {
            if (TryGetActiveChunkByPos(deactivateBuffer[i], out Chunk chunk))
                SetChunkActive(chunk, false);
        }

        List<Vector2Int> destroyBuffer = new List<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, Chunk> pair in Chunk_Dic_UnActive_ByPos)
        {
            if (pair.Value == null || IsWithinAnyObserver(pair.Key, observerPositions, destroyDistance))
                continue;

            destroyBuffer.Add(pair.Key);
        }

        PlanetData activePlanet = SaveDataMgr.Instance?.Active_PlanetData;
        for (int i = 0; i < destroyBuffer.Count; i++)
        {
            Vector2Int chunkPos = destroyBuffer[i];
            if (!TryGetChunkByPos(chunkPos, out Chunk chunk))
                continue;

            chunk.SaveChunk();
            if (activePlanet != null && chunk.MapSave != null)
                activePlanet.MapData_Dict[ChunkNameFromPos(chunkPos)] = chunk.MapSave;

            DestroyChunk(chunk);
        }
    }

    private HashSet<Vector2Int> BuildObserverWindow(IReadOnlyList<Vector3> observerPositions, int distance)
    {
        int radius = Mathf.Max(0, distance - 1);
        HashSet<Vector2Int> result = new HashSet<Vector2Int>();

        for (int observerIndex = 0; observerIndex < observerPositions.Count; observerIndex++)
        {
            Vector3 position = observerPositions[observerIndex];
            int centerIndexX = Mathf.FloorToInt(position.x / _cachedChunkSize.x);
            int centerIndexY = Mathf.FloorToInt(position.y / _cachedChunkSize.y);

            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    result.Add(NormalizeChunkPosition(new Vector2Int(
                        (centerIndexX + offsetX) * _cachedChunkStepX,
                        (centerIndexY + offsetY) * _cachedChunkStepY)));
                }
            }
        }

        return result;
    }

    private bool IsWithinAnyObserver(
        Vector2Int chunkPos,
        IReadOnlyList<Vector3> observerPositions,
        int distance)
    {
        Vector2 chunkCenter = (Vector2)chunkPos + _cachedChunkSize * 0.5f;
        float maxDistanceX = distance * _cachedChunkSize.x;
        float maxDistanceY = distance * _cachedChunkSize.y;

        for (int i = 0; i < observerPositions.Count; i++)
        {
            Vector2Int observerChunkPos = NormalizeChunkPosition(Chunk.GetChunkPosition(observerPositions[i]));
            Vector2 observerChunkCenter = (Vector2)observerChunkPos + _cachedChunkSize * 0.5f;
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(observerChunkCenter, chunkCenter);
            if (Mathf.Abs(delta.x) <= maxDistanceX &&
                Mathf.Abs(delta.y) <= maxDistanceY)
            {
                return true;
            }
        }

        return false;
    }
}
