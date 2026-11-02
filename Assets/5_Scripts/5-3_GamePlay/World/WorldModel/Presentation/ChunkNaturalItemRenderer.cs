using System;
using System.Collections;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>只借用世界生态的绘制状态，解绑不保存、不销毁任何逻辑实体。</summary>
public sealed class ChunkNaturalItemRenderer : MonoBehaviour, IIncrementalChunkViewRenderer, IWorldAwareChunkViewRenderer
{
    #region 表现绑定
    private WorldRuntime world;
    private ChunkNaturalItemRuntime runtime;
    private ChunkTilemapRenderer terrainOwner;
    private int bindingVersion;

    public int SpawnedItemCount => runtime?.SpawnedItemCount ?? 0;
    public int EcsEntityCount => runtime?.EcsEntityCount ?? 0;
    public int EcsOnlyEntityCount => EcsEntityCount;
    public void SetWorld(WorldRuntime value) => world = value;

    private WorldNaturalItemRuntimeManager ResolveRuntimeManager()
    {
        if (world == null || !GameplayWorldRegistry.TryGet(world.WorldId, out GameplayWorldRuntime context) ||
            context.Chunks == null || !ReferenceEquals(context.Chunks.WorldRuntime, world))
            throw new InvalidOperationException("自然物表现缺少明确的所属世界。");
        return context.Chunks.NaturalItemRuntimes;
    }

    public void Bind(ChunkRuntime chunk)
    {
        Unbind();
        WorldNaturalItemRuntimeManager manager = ResolveRuntimeManager();
        manager.CompleteInitialization(chunk);
        Attach(manager.Ensure(chunk));
    }

    public IEnumerator BindIncremental(ChunkRuntime chunk)
    {
        Unbind();
        int version = bindingVersion;
        WorldNaturalItemRuntimeManager manager = ResolveRuntimeManager();
        ChunkNaturalItemRuntime state = manager.Ensure(chunk);
        while (version == bindingVersion && !state.IsDisposed && state.Chunk == null) yield return null;
        if (version != bindingVersion || state.IsDisposed) yield break;
        Attach(state);
        while (version == bindingVersion && !state.IsDisposed && !manager.IsInitialized(chunk)) yield return null;
    }

    private void Attach(ChunkNaturalItemRuntime state)
    {
        terrainOwner = GetComponentInParent<ChunkView>().GetComponentInChildren<ChunkTilemapRenderer>(true);
        if (terrainOwner == null) throw new InvalidOperationException("自然物表现缺少地形绘制宿主。");
        runtime = state;
        runtime.AttachPresentation(terrainOwner);
    }

    public void Unbind()
    {
        bindingVersion++;
        runtime?.DetachPresentation(terrainOwner);
        runtime = null;
        terrainOwner = null;
    }

    private void OnDisable() => Unbind();
    private void OnDestroy() => Unbind();
    #endregion
}
