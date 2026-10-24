using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 世界碰撞脏批次

    // 独立实例实走正式队列，停用的空组件只作身份，不创建物理几何或访问用户世界。
    private static JObject ServiceCollisionDirtyQueue(JObject args)
    {
        const string firstWorld = "probe:collision-a", secondWorld = "probe:collision-b";
        var queue = new ChunkCollisionRenderer.ObstacleDirtyQueue();
        var batch = new List<ChunkCollisionRenderer>();
        var firstRoot = new GameObject("Collision queue probe A");
        var secondRoot = new GameObject("Collision queue probe B");
        var staleRoot = new GameObject("Collision queue probe destroyed");
        firstRoot.SetActive(false);
        secondRoot.SetActive(false);
        staleRoot.SetActive(false);
        try
        {
            var first = firstRoot.AddComponent<ChunkCollisionRenderer>();
            var second = secondRoot.AddComponent<ChunkCollisionRenderer>();
            var stale = staleRoot.AddComponent<ChunkCollisionRenderer>();
            bool emptyInitially = !queue.HasPending(firstWorld) && !queue.HasPending(secondWorld) && !queue.HasPending(null);
            queue.Add(firstWorld, first);
            queue.Add(firstWorld, first);
            queue.Add(secondWorld, second);
            bool bothPending = queue.HasPending(firstWorld) && queue.HasPending(secondWorld);
            queue.Take(firstWorld, batch);
            bool deduplicated = batch.Count == 1 && batch[0] == first;
            bool isolated = !queue.HasPending(firstWorld) && queue.HasPending(secondWorld);

            queue.Add(firstWorld, first);
            bool requeued = queue.HasPending(firstWorld) && batch.Count == 1 && batch[0] == first;
            queue.Take(firstWorld, batch);
            bool nextBatch = batch.Count == 1 && batch[0] == first && !queue.HasPending(firstWorld);

            queue.Add(firstWorld, first);
            queue.Remove(firstWorld, first);
            queue.Add(secondWorld, first);
            queue.Take(firstWorld, batch);
            bool rebound = batch.Count == 0 && queue.HasPending(secondWorld);
            queue.Take(secondWorld, batch);
            rebound &= batch.Count == 2 && batch.Contains(first) && batch.Contains(second);

            queue.Add(firstWorld, stale);
            UnityEngine.Object.DestroyImmediate(staleRoot);
            queue.Take(firstWorld, batch);
            bool destroyedReleased = batch.Count == 0 && !queue.HasPending(firstWorld);

            queue.Add(firstWorld, first);
            queue.Add(secondWorld, second);
            queue.Clear();
            bool cleared = !queue.HasPending(firstWorld) && !queue.HasPending(secondWorld);
            for (int i = 0; i < 8; i++) { queue.Add(firstWorld, first); queue.Take(firstWorld, batch); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool stable = true;
            for (int i = 0; i < 1024; i++)
            {
                stable &= !queue.HasPending(firstWorld) && !queue.HasPending(secondWorld);
                queue.Add(firstWorld, first);
                queue.Take(firstWorld, batch);
                stable &= batch.Count == 1 && batch[0] == first;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            return new JObject
            {
                ["emptyInitially"] = emptyInitially, ["bothPending"] = bothPending,
                ["deduplicated"] = deduplicated, ["isolated"] = isolated,
                ["requeued"] = requeued, ["nextBatch"] = nextBatch, ["rebound"] = rebound,
                ["destroyedReleased"] = destroyedReleased, ["cleared"] = cleared,
                ["stable"] = stable, ["iterations"] = 1024, ["allocatedBytes"] = allocated
            };
        }
        finally
        {
            queue.Clear();
            if (firstRoot != null) UnityEngine.Object.DestroyImmediate(firstRoot);
            if (secondRoot != null) UnityEngine.Object.DestroyImmediate(secondRoot);
            if (staleRoot != null) UnityEngine.Object.DestroyImmediate(staleRoot);
        }
    }

    #endregion
}
