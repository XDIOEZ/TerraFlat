using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>位置清单只含字符串，真实 Sprite 按用途首次请求后接入，句柄归调用方资源会话。</summary>
internal sealed partial class ResourceAssetScope
{
    #region 按用途接入 Sprite

    private Dictionary<string, Sprite> startupSprites = new(StringComparer.Ordinal);
    private Dictionary<string, string> spritePurposeLocations;
    private readonly HashSet<string> boundSpritePurposeCatalogs = new(StringComparer.Ordinal);
    private bool? spriteManifestAvailable;
    internal int LoadedSpritePurposeCatalogCount => boundSpritePurposeCatalogs.Count;
    internal int BoundSpriteAddressCount => startupSprites.Count;

    private bool TryCreatePurposeSpriteRequest(string address, out AsyncOperationHandle<Sprite> handle)
    {
        if (startupSprites.TryGetValue(address, out Sprite sprite))
        {
            FlatWorldSpriteAtlasDiagnostics.RecordBatchedSpriteAddress();
            handle = Addressables.ResourceManager.CreateCompletedOperation(sprite, (string)null);
            return true;
        }
        handle = default;
        if (spritePurposeLocations != null)
        {
            if (!spritePurposeLocations.ContainsKey(address)) return false;
            handle = CreatePurposeSpriteRequest(address);
            return true;
        }
        if (!HasSpritePurposeManifest()) return false;
        bool requested = assets.ContainsKey((typeof(StartupSpriteAtlasCatalog), StartupSpriteAtlasCatalog.Address));
        var manifest = Load<StartupSpriteAtlasCatalog>(StartupSpriteAtlasCatalog.Address);
        if (!requested) FlatWorldSpriteAtlasDiagnostics.RecordManifestRequest();
        handle = Addressables.ResourceManager.CreateChainOperation<Sprite, StartupSpriteAtlasCatalog>(manifest,
            completed => CompleteSpritePurposeRequest(() =>
            {
                if (spritePurposeLocations == null)
                {
                    StartupSpriteAtlasCatalog source = Require(completed, "Sprite 用途位置清单");
                    if (source.Entries.Length != 0) throw new InvalidDataException("Sprite 位置清单不能直接引用图片。");
                    var candidate = new Dictionary<string, string>(source.Locations.Length, StringComparer.Ordinal);
                    foreach (StartupSpriteAtlasLocation location in source.Locations)
                    {
                        if (location == null || string.IsNullOrEmpty(location.Address) || string.IsNullOrEmpty(location.CatalogAddress))
                            throw new InvalidDataException("Sprite 用途位置清单包含空地址。");
                        candidate.Add(location.Address, location.CatalogAddress);
                    }
                    spritePurposeLocations = candidate;
                }
                return CreatePurposeSpriteRequest(address);
            }), false);
        return true;
    }

    private bool HasSpritePurposeManifest()
    {
        if (spriteManifestAvailable.HasValue) return spriteManifestAvailable.Value;
        bool found = false;
        foreach (var locator in Addressables.ResourceLocators)
            if (locator.Locate(StartupSpriteAtlasCatalog.Address, typeof(StartupSpriteAtlasCatalog), out IList<IResourceLocation> locations) && locations.Count > 0)
            { found = true; break; }
        spriteManifestAvailable = found;
#if !UNITY_EDITOR
        if (!found) throw new InvalidDataException("缺少构建期 Sprite 用途位置清单，请重新构建游戏。");
#endif
        return found;
    }

    private AsyncOperationHandle<Sprite> CreatePurposeSpriteRequest(string address)
    {
        if (!spritePurposeLocations.TryGetValue(address, out string catalogAddress))
        {
            FlatWorldSpriteAtlasDiagnostics.RecordSourceSpriteAddressRequest();
            // 链操作接管返回值，不能再由 Scope 重复持有同一份子句柄。
            return LoadSource("address|" + typeof(Sprite).FullName + "|" + address,
                () => LoadAddress<Sprite>(address), ownHandle: false);
        }
        bool requested = assets.ContainsKey((typeof(StartupSpriteAtlasCatalog), catalogAddress));
        var catalog = Load<StartupSpriteAtlasCatalog>(catalogAddress);
        if (!requested) FlatWorldSpriteAtlasDiagnostics.RecordCatalogRequest();
        if (catalog.IsDone) return CompleteSpritePurposeRequest(() => ReadPurposeSprite(catalogAddress, catalog, address));
        return Addressables.ResourceManager.CreateChainOperation<Sprite, StartupSpriteAtlasCatalog>(catalog,
            completed => CompleteSpritePurposeRequest(() => ReadPurposeSprite(catalogAddress, completed, address)), false);
    }

    private AsyncOperationHandle<Sprite> ReadPurposeSprite(string catalogAddress,
        AsyncOperationHandle<StartupSpriteAtlasCatalog> request, string address)
    {
        if (!boundSpritePurposeCatalogs.Contains(catalogAddress))
        {
            BindStartupSpriteCatalog(Require(request, catalogAddress));
            boundSpritePurposeCatalogs.Add(catalogAddress);
        }
        if (!startupSprites.TryGetValue(address, out Sprite sprite))
            throw new InvalidDataException("Sprite 用途目录遗漏地址：" + address);
        FlatWorldSpriteAtlasDiagnostics.RecordBatchedSpriteAddress();
        return Addressables.ResourceManager.CreateCompletedOperation(sprite, (string)null);
    }

    internal AsyncOperationHandle<Sprite> CompleteSpritePurposeRequest(Func<AsyncOperationHandle<Sprite>> complete)
    {
        // Scope 卸载后异步回调只能失败，不能伪装成 Succeeded-null 的有效请求。
        if (disposed) return Addressables.ResourceManager.CreateCompletedOperation<Sprite>(null,
            "Sprite 请求已取消：所属资源 Scope 已释放。");
        try
        {
            return complete();
        }
        catch (Exception error)
        {
            return Addressables.ResourceManager.CreateCompletedOperation<Sprite>(null, error.ToString());
        }
    }

    internal void BindStartupSpriteCatalog(StartupSpriteAtlasCatalog catalog)
    {
        var candidate = new Dictionary<string, Sprite>(startupSprites, StringComparer.Ordinal);
        foreach (StartupSpriteAtlasEntry entry in catalog.Entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Address) || entry.Sprite == null)
                throw new InvalidDataException("Sprite 用途目录包含不完整地址或资源引用。");
            candidate.Add(entry.Address, entry.Sprite);
        }
        startupSprites = candidate;
    }

    #endregion
}

public static class FlatWorldSpriteAtlasDiagnostics
{
    #region Sprite 加载诊断

    public static int SourceSpriteAddressRequests { get; private set; }
    public static int BatchedSpriteAddressRequests { get; private set; }
    public static int CatalogRequests { get; private set; }
    public static int ManifestRequests { get; private set; }
    public static string RuntimeSummary => $"批量目录请求 {CatalogRequests}，批量命中 {BatchedSpriteAddressRequests}，零散请求 {SourceSpriteAddressRequests}，位置清单请求 {ManifestRequests}";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    { SourceSpriteAddressRequests = 0; BatchedSpriteAddressRequests = 0; CatalogRequests = 0; ManifestRequests = 0; }
    internal static void RecordCatalogRequest() => CatalogRequests++;
    internal static void RecordManifestRequest() => ManifestRequests++;
    internal static void RecordBatchedSpriteAddress() => BatchedSpriteAddressRequests++;
    internal static void RecordSourceSpriteAddressRequest() => SourceSpriteAddressRequests++;

    #endregion
}
