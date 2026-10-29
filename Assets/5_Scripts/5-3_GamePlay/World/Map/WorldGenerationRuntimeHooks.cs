using System;
using System.Threading;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 可选运行时生成配置入口：WorldModel 在纯 Profile 冻结前调用快照 Hook；
/// 自动化、诊断或服务器可临时调整真实生成参数，而不修改 Prefab/SO。
/// </summary>
public static class WorldGenerationRuntimeHooks
{
    #region 生成扩展

    public static event Func<ChunkGenerationProfileSnapshot, ChunkGenerationProfileSnapshot>
        BeforeWorldModelGeneration;
    public static event Action<ChunkGenerationRequest, ChunkTerrainBuffer, CancellationToken>
        WorldModelTerrainDecorator;
    internal static bool HasProfileSubscribers => BeforeWorldModelGeneration != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        BeforeWorldModelGeneration = null;
        WorldModelTerrainDecorator = null;
    }

    // 主线程一次捕获纯数据扩展，后台任务不再读取运行时订阅列表。
    public static Action<ChunkGenerationRequest, ChunkTerrainBuffer, CancellationToken>
        CaptureWorldModelTerrainDecorator(
            Action<ChunkGenerationRequest, ChunkTerrainBuffer, CancellationToken> builtIn = null)
        => builtIn + WorldModelTerrainDecorator;

    /// <summary>在后台区块请求冻结前，按订阅顺序转换纯生成配置快照。</summary>
    public static ChunkGenerationProfileSnapshot ApplyBeforeWorldModelGeneration(
        ChunkGenerationProfileSnapshot profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        Delegate[] handlers = BeforeWorldModelGeneration?.GetInvocationList();
        if (handlers == null)
            return profile;

        for (int i = 0; i < handlers.Length; i++)
        {
            profile = ((Func<ChunkGenerationProfileSnapshot,
                ChunkGenerationProfileSnapshot>)handlers[i])(profile);
            if (profile == null)
                throw new InvalidOperationException("世界生成运行时 Hook 返回了空 Profile。");
        }

        return profile;
    }

    #endregion
}
