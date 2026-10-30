using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ChunkMgr
{
    #region 按世界选择独立管理器
    public new static ChunkMgr Instance => GameplayWorldRegistry.ExecutionChunks ?? SingletonAutoMono<ChunkMgr>.Instance;
    public static ChunkMgr LocalInstance => instance;
    public string OwnedWorldKey { get; private set; }
    private bool isReleasingOwnedWorldContext;

    public static void ClearLocalNetworkWorld() => instance = null;
    public void ReleaseRegisteredWorld() => ShutdownWorldRuntime();

    public void BindNetworkWorld(string worldKey) => OwnedWorldKey = worldKey;

    /// <summary>分帧任务只借用仍归本管理器所有的世界，不补建已经注销的世界。</summary>
    private bool TryUseRuntimeWorldContext(out GameplayWorldRegistry.WorldExecutionScope scope)
    {
        scope = default;
        string worldKey = !string.IsNullOrEmpty(OwnedWorldKey) ? OwnedWorldKey : WorldRuntime?.WorldId;
        if (string.IsNullOrEmpty(worldKey)) return false;
        if (GameplayWorldRegistry.TryGet(worldKey, out GameplayWorldRuntime registered))
        {
            if (!ReferenceEquals(registered.Chunks, this)) return false;
            if (!GameplayWorldRegistry.HasExecutionContext ||
                !ReferenceEquals(GameplayWorldRegistry.ExecutionChunks, this) ||
                !string.Equals(GameplayWorldRegistry.ExecutionWorldKey, worldKey, StringComparison.Ordinal) ||
                GameplayWorldRegistry.CurrentScene != registered.Scene)
                scope = GameplayWorldRegistry.UseWorld(worldKey);
            return true;
        }
        return string.IsNullOrEmpty(OwnedWorldKey) &&
            string.Equals(worldKey, GameplayWorldRegistry.ExecutionWorldKey, StringComparison.Ordinal) &&
            ReferenceEquals(ExistingInstance, this);
    }

    private GameplayWorldRegistry.WorldExecutionScope RequireRuntimeWorldContext()
    {
        // 仅销毁解绑允许复用已核对归属的卸载中上下文，正常流送仍要求世界已加载。
        if (isReleasingOwnedWorldContext && ReferenceEquals(GameplayWorldRegistry.ExecutionChunks, this))
            return default;
        if (!TryUseRuntimeWorldContext(out GameplayWorldRegistry.WorldExecutionScope scope))
            throw new InvalidOperationException("[ChunkMgr] 所属世界已注销或管理器已更换，不能在另一世界解绑区块表现。");
        return scope;
    }

    public static ChunkMgr CreateNetworkWorldManager(string worldKey)
    {
        ChunkMgr previous = instance;
        try
        {
            // 每个世界独立持有全部窗口、生成器和区块索引，创建阶段不触发重复单例销毁。
            instance = null;
            var root = new GameObject("ChunkMgr / " + worldKey);
            PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(root, PersistentRuntimeObjectCategory.World);
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
