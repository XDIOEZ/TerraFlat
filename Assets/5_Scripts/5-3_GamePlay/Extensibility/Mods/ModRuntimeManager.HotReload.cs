using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

/// <summary>
/// MOD 资源候选会话。加载期间隔离定义、Lua 与设置目录，正式世界继续使用原会话。
/// 未改动的 Bundle 借用旧代句柄；正在使用的 Bundle 二进制变更明确拒绝，不能强制卸载活跃资源。
/// </summary>
public sealed partial class ModRuntimeManager
{
    #region 候选目录

    private bool preparingResourceReload;
    private bool reloadSourceWorldMutationAllowed;
    private Dictionary<string, ModPackage> reloadSourcePackages;
    private GameObject resourceTemplateRoot;

    /// <summary>登记资源所有权与目录槽位；游戏事件订阅由同一个管理器持续持有，不重复解绑和绑定。</summary>
    internal void ConfigureResourceReload(ResourceReloadContext context)
    {
        reloadSourcePackages = packagesById;
        reloadSourceWorldMutationAllowed = worldMutationAllowed;
        context.Add(() => preparingResourceReload, value => preparingResourceReload = value, true);
        context.Add(() => worldMutationAllowed, value => worldMutationAllowed = value, false);
        context.Add(() => State, value => State = value, ModLoadState.NotStarted);
        context.Add(() => FailureReason, value => FailureReason = value, (string)null);
        context.Add(() => ModSetHash, value => ModSetHash = value, string.Empty);
        context.Add(() => activeProfile, value => activeProfile = value, (ModProfile)null);
        context.Add(() => safeModeActive, value => safeModeActive = value, false);
        context.Add(() => resourceTemplateRoot, value => resourceTemplateRoot = value, (GameObject)null);
        context.AddList(() => loadedPackages, value => loadedPackages = value);
        context.AddDictionary(() => packagesById, value => packagesById = value);
        context.AddDictionary(() => luaRuntimes, value => luaRuntimes = value);
        context.AddDictionary(() => globalStates, value => globalStates = value);
        context.AddList(() => loadedBundles, value => loadedBundles = value);
        context.AddList(() => clonedAssets, value => clonedAssets = value);
        context.AddSet(() => runtimeTemplates, value => runtimeTemplates = value);
        context.AddList(() => pendingItemDefinitions, value => pendingItemDefinitions = value);
        context.AddList(() => pendingActorDefinitions, value => pendingActorDefinitions = value);
        context.AddList(() => registeredActorIds, value => registeredActorIds = value);
        context.AddList(() => pendingRecipeDefinitions, value => pendingRecipeDefinitions = value);
        context.AddList(() => pendingBuffDefinitions, value => pendingBuffDefinitions = value);
        context.AddList(() => pendingLiquidDefinitions, value => pendingLiquidDefinitions = value);
        context.AddList(() => registeredLiquidIds, value => registeredLiquidIds = value);
        context.AddList(() => pendingContaminationDefinitions, value => pendingContaminationDefinitions = value);
        context.AddList(() => registeredContaminationIds, value => registeredContaminationIds = value);
        context.AddList(() => pendingQuestDefinitions, value => pendingQuestDefinitions = value);
        context.AddList(() => pendingPlayerCreationTemplates, value => pendingPlayerCreationTemplates = value);
        context.AddList(() => pendingPatchDocuments, value => pendingPatchDocuments = value);
        context.AddDictionary(() => definitionInfos, value => definitionInfos = value);
        context.AddList(() => pendingTileDefinitions, value => pendingTileDefinitions = value);
        context.AddList(() => tileDefinitionChanges, value => tileDefinitionChanges = value);
        context.AddList(() => registeredTileAssets, value => registeredTileAssets = value);
        ModLocalizationRegistry.ConfigureResourceReload(context);
        ModSettingsRegistry.ConfigureResourceReload(context);
        FactionRelationService.ConfigureResourceReload(context);
    }

    /// <summary>提交前恢复最新 MOD 全局状态；它来自仍在运行的世界，而不是加载开始时的过期快照。</summary>
    internal void PrepareReloadState(ModSaveMetadata state)
    {
        if (!IsReady) throw new InvalidOperationException("MOD 候选目录尚未就绪。");
        if (!ValidateSaveMetadata(state, out string error)) throw new InvalidDataException(error);
        RestoreSaveMetadata(state);
    }

    /// <summary>离开候选模式；正式事件订阅始终保持一份。</summary>
    internal void FinishResourceReload()
    {
        // 只有已提交的候选需要恢复权限；取消时正式世界可能已退出，不能写回加载开始时的旧权限。
        if (preparingResourceReload && reloadSourcePackages != null)
            worldMutationAllowed = reloadSourceWorldMutationAllowed;
        preparingResourceReload = false;
        reloadSourcePackages = null;
    }

    /// <summary>候选脚本不能提前持久化设置；正式客户端仍可修改自己的 client 设置。</summary>
    internal void EnsureClientSettingsWritable()
    {
        if (preparingResourceReload)
            throw new InvalidOperationException("MOD 候选加载期间不能写入客户端设置，请在 OnContentReady 中执行。");
    }

    #endregion

    #region Bundle 与旧代资源

    /// <summary>资源模板始终在未激活的父节点下实例化，不能在候选阶段触发实体 Awake/OnEnable。</summary>
    private Transform GetResourceTemplateParent()
    {
        if (resourceTemplateRoot == null)
        {
            resourceTemplateRoot = new GameObject("MOD Resource Templates");
            resourceTemplateRoot.SetActive(false);
            resourceTemplateRoot.transform.SetParent(transform, false);
            clonedAssets.Add(resourceTemplateRoot);
        }
        return resourceTemplateRoot.transform;
    }

    /// <summary>复用未变化的在用 Bundle，所有新加载句柄仍只有一个明确所有者。</summary>
    private AssetBundle LoadResourceBundle(ModPackage package, string bundleId, string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        string hash = BitConverter.ToString(sha.ComputeHash(stream));
        package.BundleHashes.Add(bundleId, hash);
        package.BundlePaths.Add(bundleId, path);
        if (preparingResourceReload && reloadSourcePackages != null &&
            reloadSourcePackages.TryGetValue(package.Manifest.Id, out ModPackage previous) &&
            previous.Bundles.TryGetValue(bundleId, out AssetBundle existing))
        {
            if (!previous.BundleHashes.TryGetValue(bundleId, out string oldHash) || oldHash != hash ||
                !previous.BundlePaths.TryGetValue(bundleId, out string oldPath) || oldPath != path)
                throw new InvalidDataException($"MOD {package.Manifest.Id} 的在用 Bundle {bundleId} 已改变；当前世界只能原位更新 JSON/Lua，Bundle 替换需在主菜单执行。");
            return existing;
        }
        AssetBundle bundle = AssetBundle.LoadFromFile(path);
        if (bundle != null) loadedBundles.Add(bundle);
        return bundle;
    }

    /// <summary>捕获这一代资源的独立释放动作，不持有会随后切换的目录字段。</summary>
    internal Action CaptureResourceRelease()
    {
        ModLuaRuntime[] scripts = luaRuntimes.Values.ToArray();
        UnityEngine.Object[] assets = clonedAssets.ToArray();
        AssetBundle[] bundles = loadedBundles.ToArray();
        return () => ReleaseReloadResources(scripts, assets, bundles, preserveCurrentBundles: true);
    }

    /// <summary>失败候选没有任何世界引用，可以立即释放，不触发 MOD 的退出回调。</summary>
    internal void ReleaseCandidateResources() => ReleaseReloadResources(
        luaRuntimes.Values.ToArray(), clonedAssets.ToArray(), loadedBundles.ToArray(), preserveCurrentBundles: false);

    /// <summary>旧代在世界完全退出后释放；一项失败不阻止其他资源回收。</summary>
    private static void ReleaseReloadResources(ModLuaRuntime[] scripts, UnityEngine.Object[] assets, AssetBundle[] bundles, bool preserveCurrentBundles)
    {
        foreach (ModLuaRuntime script in scripts)
        {
            try { script.DisposeWithoutCallbacks(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
        foreach (UnityEngine.Object asset in assets)
            if (asset != null) Destroy(asset);
        foreach (AssetBundle bundle in bundles)
        {
            if (bundle == null) continue;
            // 新目录可能借用了旧代未变化的 Bundle；旧代回收时将所有权转给当前目录。
            ModRuntimeManager owner = Instance;
            if (preserveCurrentBundles && owner != null && owner.packagesById.Values.Any(package => package.Bundles.Values.Contains(bundle)))
            {
                if (!owner.loadedBundles.Contains(bundle)) owner.loadedBundles.Add(bundle);
            }
            else bundle.Unload(false);
        }
    }

    #endregion
}
