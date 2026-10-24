#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

internal sealed partial class ResourceAssetScope
{
    #region 编辑器资源调度

    private readonly Dictionary<(Type, IResourceLocation), IResourceLocation> editorLoadLocations = new();

    private IResourceLocation GetEditorLoadLocation(IResourceLocation source, Type type)
    {
        var key = (type, source);
        if (!editorLoadLocations.TryGetValue(key, out IResourceLocation location))
        {
            location = RemapEditorLoadLocation(Addressables.ResourceManager, source, type);
            editorLoadLocations.Add(key, location);
        }
        return location;
    }

    /// <summary>保留原位置和依赖，只为默认 AssetDatabase Provider 去掉每次请求的模拟延迟。</summary>
    internal static IResourceLocation RemapEditorLoadLocation(ResourceManager manager, IResourceLocation source, Type type)
    {
        if (manager.GetResourceProvider(type, source)?.GetType() != typeof(AssetDatabaseProvider)) return source;
        string providerId = typeof(FastEditorAssetDatabaseProvider).FullName;
        bool registered = false;
        foreach (IResourceProvider provider in manager.ResourceProviders)
            if (provider.ProviderId == providerId) { registered = true; break; }
        if (!registered) manager.ResourceProviders.Add(new FastEditorAssetDatabaseProvider());

        int dependencyCount = source.Dependencies?.Count ?? 0;
        var dependencies = dependencyCount == 0 ? Array.Empty<IResourceLocation>() : new IResourceLocation[dependencyCount];
        source.Dependencies?.CopyTo(dependencies, 0);
        return new ResourceLocationBase(source.PrimaryKey, source.InternalId, providerId, source.ResourceType, dependencies)
        {
            Data = source.Data
        };
    }

    private sealed class FastEditorAssetDatabaseProvider : AssetDatabaseProvider
    {
        // 仍由 Addressables 异步完成和释放，四槽队列与主线程预算继续限制接入工作。
        public FastEditorAssetDatabaseProvider() : base(0f) { }
    }

    #endregion
}
#endif
