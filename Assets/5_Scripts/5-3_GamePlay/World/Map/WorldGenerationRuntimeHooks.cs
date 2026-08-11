using System;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 可选运行时生成配置入口：旧 Map 在继承维度设置后调用组件 Hook，
/// 新 WorldModel 在纯 Profile 冻结前调用快照 Hook。正常游戏没有订阅者；
/// 自动化、诊断或服务器可临时调整真实生成参数，而不修改 Prefab/SO。
/// </summary>
public static class WorldGenerationRuntimeHooks
{
    public static event Action<Map> BeforeMapGeneration;
    public static event Func<ChunkGenerationProfileSnapshot, ChunkGenerationProfileSnapshot>
        BeforeWorldModelGeneration;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        BeforeMapGeneration = null;
        BeforeWorldModelGeneration = null;
    }

    public static void ApplyBeforeMapGeneration(Map map)
    {
        if (map == null)
            throw new ArgumentNullException(nameof(map));
        BeforeMapGeneration?.Invoke(map);
    }

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
}
