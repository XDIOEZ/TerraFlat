using UnityEngine;

public partial class ChunkMgr
{
    #region 单机太空往返的有限区块表现池
    public const int SceneTransitionChunkViewCapacity = 9;
    public bool SceneTransitionChunkViewCachingEnabled { get; set; } = true;
    private Transform sceneTransitionChunkPoolRoot;
    public int RuntimeChunkViewCreatedCount { get; private set; }
    public int RuntimeChunkViewTransitionHits { get; private set; }
    public int RetainedSceneTransitionChunkViewCount
    {
        get
        {
            int count = 0;
            foreach (PooledChunkViewEntry entry in chunkViewPool)
                if (entry.RetainedForTransition && entry.View != null) count++;
            return count;
        }
    }

    private void RetainSceneTransitionChunkViews()
    {
        // 太空交接会批量取出、丢弃和迁移池条目，回地表时重新核对维护期限。
        nextChunkViewPoolMaintenanceAt = 0f;
        int available = chunkViewPool.Count;
        int retained = 0;
        GameRes resources = GameRes.ExistingInstance;
        for (int index = 0; index < available; index++)
        {
            PooledChunkViewEntry entry = chunkViewPool.Dequeue();
            ChunkView view = entry.View;
            if (view == null) continue;
            if (!entry.Completed || view.Model != null || resources == null || !resources.isLoadFinish ||
                retained >= SceneTransitionChunkViewCapacity ||
                (entry.RetainedForTransition && (entry.ResourceOwner != resources ||
                    entry.ResourceRevision != resources.ItemDefinitionRevision)))
            {
                DestroyRuntimeChunkView(entry.TakeView());
                continue;
            }
            // 常驻根只收已经解绑的壳，旧场景卸载不会带走池中组件。
            if (sceneTransitionChunkPoolRoot == null)
            {
                var root = new GameObject("SceneTransitionChunkViewPool");
                root.transform.SetParent(transform, false);
                sceneTransitionChunkPoolRoot = root.transform;
            }
            view.transform.SetParent(sceneTransitionChunkPoolRoot, false);
            view.gameObject.SetActive(false);
            entry.RetainedForTransition = true;
            entry.ResourceOwner = resources;
            entry.ResourceRevision = resources.ItemDefinitionRevision;
            chunkViewPool.Enqueue(entry);
            retained++;
        }
    }

    private void DestroySceneTransitionChunkPoolRoot()
    {
        if (sceneTransitionChunkPoolRoot == null) return;
        if (Application.isPlaying) Destroy(sceneTransitionChunkPoolRoot.gameObject);
        else DestroyImmediate(sceneTransitionChunkPoolRoot.gameObject);
        sceneTransitionChunkPoolRoot = null;
    }
    #endregion
}
