using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
#if UNITY_EDITOR
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
#endif

/// <summary>用受控完成时机驱动正式资源队列，只采样调度与真实句柄释放结果。</summary>
public static partial class GameplayTestSystemProbe
{
    #region 资源队列规则采样

    private static JObject ServiceResourceQueue(JObject args)
    {
        string mode = Text(args, "mode", "slow");
        int count = mode == "empty" ? 0 : 6;
        int tick = 0, active = 0, maximum = 0, yielded = 0, budgetChecks = 0;
        int[] results = new int[count];
        int[] startedAt = new int[count], completedAt = new int[count];
        Array.Fill(startedAt, -1);
        Array.Fill(completedAt, -1);
        var progress = new JArray();
        var handles = new List<AsyncOperationHandle<int>>();
        using var manager = new ResourceManager();
        using var scope = new ResourceAssetScope();
        using var pipeline = new ResourceLoadPipeline((_, _) => { }, () => false, () => double.MaxValue);
        IEnumerator Load()
        {
            yield return ResourceLoadQueue.Run(count,
                index =>
                {
                    if (mode == "failed_start" && index == 2)
                        throw new InvalidOperationException("资源发出失败夹具");
                    startedAt[index] = tick;
                    active++;
                    maximum = Math.Max(maximum, active);
                    if (mode == "optional_missing" && index == 2) return default(AsyncOperationHandle<int>);
                    var handle = scope.Own(manager.CreateCompletedOperation(index, (string)null));
                    handles.Add(handle);
                    return handle;
                }, handle => !handle.IsValid() || mode == "instant" || mode == "budget" ||
                    tick >= (handle.Result == 0 ? 3 : 1),
                (index, handle) =>
                {
                    if (mode == "failed_complete" && index == 1)
                        throw new InvalidOperationException("资源收尾失败夹具");
                    results[index] = handle.IsValid() ? handle.Result : -1;
                    completedAt[index] = tick;
                    active--;
                }, () => mode == "budget" && ++budgetChecks % 2 == 0,
                completed => progress.Add(completed));
        }
        pipeline.Add("queue", "资源队列", 1, Load);
        IEnumerator runner = pipeline.Run();
        try
        {
            while (runner.MoveNext())
            {
                yielded++;
                tick++;
                if (mode == "cancel") break;
                if (yielded > 128) throw new InvalidOperationException("资源队列夹具超过步数上限。");
            }
        }
        finally { (runner as IDisposable)?.Dispose(); }
        int ownedBeforeDispose = scope.HandleCount;
        scope.Dispose();
        int validAfterDispose = 0;
        foreach (AsyncOperationHandle<int> handle in handles)
            if (handle.IsValid()) validAfterDispose++;
        return new JObject
        {
            ["maximum"] = maximum, ["started"] = handles.Count, ["yielded"] = yielded,
            ["results"] = new JArray(results), ["startedAt"] = new JArray(startedAt),
            ["completedAt"] = new JArray(completedAt), ["progress"] = progress,
            ["failure"] = pipeline.Failure?.GetType().Name, ["stage"] = pipeline.CurrentStage,
            ["ownedBeforeDispose"] = ownedBeforeDispose, ["ownedAfterDispose"] = scope.HandleCount,
            ["validAfterDispose"] = validAfterDispose
        };
    }

    #endregion

    #region 启动资源前后台策略

    private static JObject ServiceStartupResourceLoadingPriority(JObject args) => new()
    {
        ["background"] = GameRes.ShouldKeepStartupResourceLoadingInBackground(
            Flag(args, "startupReady", false), Flag(args, "enteringWorld", false))
    };

    #endregion

    #region 编辑器资源入口规则采样

    private static JObject ServiceEditorAssetLoading(JObject args)
    {
#if UNITY_EDITOR
        using var manager = new ResourceManager();
        var originalProvider = new AssetDatabaseProvider();
        var customProvider = new GameplayTestCustomAssetProvider();
        manager.ResourceProviders.Add(originalProvider);
        manager.ResourceProviders.Add(customProvider);
        var dependency = new ResourceLocationBase("dependency", "dependency", originalProvider.ProviderId, typeof(Texture2D));
        var source = new ResourceLocationBase("sprite-key", "Assets/fixture.png[slice]", originalProvider.ProviderId,
            typeof(Sprite), dependency) { Data = new object() };
        var warmedProvider = manager.GetResourceProvider(typeof(Sprite), source);
        IResourceLocation mapped = ResourceAssetScope.RemapEditorLoadLocation(manager, source, typeof(Sprite));
        IResourceLocation second = ResourceAssetScope.RemapEditorLoadLocation(manager, source, typeof(Sprite));
        var custom = new ResourceLocationBase("custom", "custom", customProvider.ProviderId, typeof(Sprite));
        var unknown = new ResourceLocationBase("bundle", "bundle", "AssetBundleProvider", typeof(Sprite));
        const string readyPrefabPath = "Assets/2_Prefabs/2-1_UI/Common/Controls/UI_TabButton.prefab";
        var readyPrefabLocation = new ResourceLocationBase("ready-prefab", readyPrefabPath,
            originalProvider.ProviderId, typeof(GameObject));
        var readyPrefab = manager.ProvideResource<GameObject>(
            ResourceAssetScope.RemapEditorLoadLocation(manager, readyPrefabLocation, typeof(GameObject)));
        bool completedImmediately = readyPrefab.IsDone;
        bool loadedExpectedPrefab = readyPrefab.Status == AsyncOperationStatus.Succeeded &&
            readyPrefab.Result != null && UnityEditor.AssetDatabase.GetAssetPath(readyPrefab.Result) == readyPrefabPath;
        manager.Release(readyPrefab);
        return new JObject
        {
            ["completedImmediately"] = completedImmediately, ["loadedExpectedPrefab"] = loadedExpectedPrefab,
            ["immediateRequestReleased"] = !readyPrefab.IsValid(),
            ["available"] = true,
            ["sourceUnchanged"] = source.ProviderId == originalProvider.ProviderId && ReferenceEquals(warmedProvider, originalProvider),
            ["separateProvider"] = mapped.ProviderId != source.ProviderId &&
                !ReferenceEquals(manager.GetResourceProvider(typeof(Sprite), mapped), originalProvider),
            ["identityPreserved"] = mapped.PrimaryKey == source.PrimaryKey && mapped.InternalId == source.InternalId &&
                mapped.ResourceType == source.ResourceType && ReferenceEquals(mapped.Data, source.Data),
            ["dependenciesPreserved"] = mapped.HasDependencies && mapped.Dependencies.Count == 1 &&
                ReferenceEquals(mapped.Dependencies[0], dependency) && mapped.DependencyHashCode == source.DependencyHashCode,
            ["oneProvider"] = manager.ResourceProviders.Count == 3 && second.ProviderId == mapped.ProviderId,
            ["customPreserved"] = ReferenceEquals(custom, ResourceAssetScope.RemapEditorLoadLocation(manager, custom, typeof(Sprite))),
            ["bundlePreserved"] = ReferenceEquals(unknown, ResourceAssetScope.RemapEditorLoadLocation(manager, unknown, typeof(Sprite)))
        };
#else
        return new JObject { ["available"] = false };
#endif
    }

#if UNITY_EDITOR
    private sealed class GameplayTestCustomAssetProvider : AssetDatabaseProvider { }
#endif

    #endregion

    #region 已编译模块参数规则采样

    private static JObject ServiceCompiledModuleParameters(JObject args)
    {
        var source = new GameObject("参数编译原型");
        var first = new GameObject("参数实例一");
        var second = new GameObject("参数实例二");
        source.SetActive(false);
        first.SetActive(false);
        second.SetActive(false);
        try
        {
            var prototype = source.AddComponent<GameplayTestPreparedParameterModule>();
            source.AddComponent<BoxCollider2D>();
            const string json = "{\"Values\":[1,2],\"$collider2D\":{\"type\":\"BoxCollider2D\",\"size\":{\"x\":2,\"y\":3}}}";
            var prepared = ModuleJsonConfigurator.Prepare(prototype, "fixture", "module", "fixture", json);
            prepared.ValidateTarget(prototype);
            prepared.ValidateTarget(prototype);
            int repeatedValidations = prototype.ValidationCalls;
            prototype.RejectParameters = true;
            bool customRejected = Rejected(() => prepared.ValidateTarget(prototype));
            prototype.RejectParameters = false;
            UnityEngine.Object.DestroyImmediate(source.GetComponent<BoxCollider2D>());
            bool missingColliderRejected = Rejected(() => prepared.ValidateTarget(prototype));
            source.AddComponent<CircleCollider2D>();
            bool changedColliderRejected = Rejected(() => prepared.ValidateTarget(prototype));

            var a = first.AddComponent<GameplayTestPreparedParameterModule>();
            var b = second.AddComponent<GameplayTestPreparedParameterModule>();
            first.AddComponent<BoxCollider2D>();
            second.AddComponent<BoxCollider2D>();
            prepared.Apply(a);
            prepared.Apply(b);
            a.Values[0] = 99;
            bool independent = b.Values[0] == 1 && prototype.Values.Count == 0;
            prepared.Apply(a);
            var invalidInputs = new JArray();
            foreach (string invalid in new[] { "{\"unknown\":1}", "{\"Values\":5}", "{\"_Data\":{}}", "{\"$invalid\":{}}" })
                invalidInputs.Add(Rejected(() => ModuleJsonConfigurator.Prepare(b, "fixture", "module", "fixture", invalid)));
            return new JObject
            {
                ["repeatedValidations"] = repeatedValidations, ["customRejected"] = customRejected,
                ["missingColliderRejected"] = missingColliderRejected, ["changedColliderRejected"] = changedColliderRejected,
                ["nullTargetRejected"] = Rejected(() => prepared.ValidateTarget(null)),
                ["independent"] = independent, ["reapplied"] = a.Values[0] == 1,
                ["colliderApplied"] = first.GetComponent<BoxCollider2D>().size == new Vector2(2, 3),
                ["invalidInputs"] = invalidInputs
            };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }

        static bool Rejected(Action action)
        {
            try { action(); return false; }
            catch (InvalidOperationException) { return true; }
        }
    }

    #endregion
}

internal sealed class GameplayTestPreparedParameterModule : Module, IModuleJsonParameterValidator
{
    #region 参数复用夹具

    public List<int> Values = new();
    [NonSerialized] public int ValidationCalls;
    [NonSerialized] public bool RejectParameters;
    public override ModuleData _Data { get; set; } = new Ex_ModData();
    protected override void OnLoad() { }
    protected override void OnSave() { }

    public void ValidateJsonParameters(JObject parameters)
    {
        ValidationCalls++;
        if (RejectParameters) throw new InvalidOperationException("模块原型当前拒绝该配置。");
    }

    #endregion
}
