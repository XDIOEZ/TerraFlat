using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ChunkMgr
{
    #region 按世界选择独立管理器
    public new static ChunkMgr Instance => GameplayWorldRegistry.ExecutionChunks ?? SingletonAutoMono<ChunkMgr>.Instance;
    public static ChunkMgr LocalInstance => instance;
    public string OwnedWorldKey { get; private set; }

    public static void ClearLocalNetworkWorld() => instance = null;
    public void ReleaseRegisteredWorld() => ShutdownWorldRuntime();

    public void BindNetworkWorld(string worldKey) => OwnedWorldKey = worldKey;

    public static ChunkMgr CreateNetworkWorldManager(string worldKey)
    {
        ChunkMgr previous = instance;
        try
        {
            // 每个世界独立持有全部窗口、生成器和区块索引，创建阶段不触发重复单例销毁。
            instance = null;
            var root = new GameObject("ChunkMgr / " + worldKey);
            DontDestroyOnLoad(root);
            ChunkMgr manager = root.AddComponent<ChunkMgr>();
            manager.OwnedWorldKey = worldKey;
            return manager;
        }
        finally { instance = previous; }
    }

    public static void SelectLocalNetworkWorld(ChunkMgr manager)
    {
        if (manager == null) throw new ArgumentNullException(nameof(manager));
        instance = manager;
    }
    #endregion
}
