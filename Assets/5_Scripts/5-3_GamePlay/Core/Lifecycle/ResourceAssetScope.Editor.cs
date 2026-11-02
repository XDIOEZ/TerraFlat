#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine;

internal sealed partial class ResourceAssetScope
{
    #region 编辑器资源调度

    private readonly Dictionary<(Type, IResourceLocation), IResourceLocation> editorLoadLocations = new();

    // 首次 Fast Mode 直接执行同一个正式 Sprite 请求，避免先排一次纹理请求再重复进入 Provider。
    internal bool CanLoadInitialEditorSpriteDirectly(string address, bool initialLoad)
    {
        if (!initialLoad || disposed) return false;
        foreach (var locator in Addressables.ResourceLocators)
            if (locator.Locate(address, typeof(UnityEngine.Sprite), out IList<IResourceLocation> locations))
                foreach (IResourceLocation location in locations)
                {
                    IResourceProvider provider = Addressables.ResourceManager.GetResourceProvider(typeof(UnityEngine.Sprite), location);
                    if (provider != null) return ShouldLoadInitialEditorSpriteDirectly(provider, initialLoad);
                }
        return false;
    }

    internal static bool ShouldLoadInitialEditorSpriteDirectly(IResourceProvider provider, bool initialLoad)
        => initialLoad && provider?.GetType() == typeof(AssetDatabaseProvider);

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
        // 负延迟立即接入已预热资产，避免零延迟仍排到下一次更新；目录队列继续控制主线程预算。
        public FastEditorAssetDatabaseProvider() : base(-1f) { }

        public override void Provide(ProvideHandle handle)
        {
            string path = handle.ResourceManager.TransformInternalId(handle.Location);
            if (handle.Type != typeof(Sprite) || !path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                base.Provide(handle);
                return;
            }
            // 切片解析与真实请求在同一 Provider 内完成一次，失败不再由 SDK 回退成错误主图。
            if (path.EndsWith("[]", StringComparison.Ordinal))
            {
                handle.Complete<Sprite>(null, false, new System.IO.InvalidDataException("Sprite 切片名不能为空：" + path));
                return;
            }
            bool success = ItemDefinitionCatalogLoader.TryLoadEditorSprite(path, out Sprite sprite, out string error);
            handle.Complete(sprite, success, success ? null : new System.IO.InvalidDataException(error));
        }
    }

    #endregion
}
#endif
