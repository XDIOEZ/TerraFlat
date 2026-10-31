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
    #region 资源配置预取规则采样

    private static JObject ServiceResourcePrefetch(JObject args)
    {
        string mode = Text(args, "mode", "complete");
        int frame = 0, blockedFrame = -1, disposed = 0, nestedDisposed = 0, value = 0;
        bool prefetchStarted = false, startupFinished = false, startedBeforeStartupFinished = false;
        bool prefetchFinished = false, prefetchFinishedBeforeContent = false, published = false;
        using var pipeline = new ResourceLoadPipeline((_, _) => { }, () => mode == "busy_interactive",
            () => blockedFrame == frame ? 0d : double.MaxValue, () => frame);

        IEnumerator Prefetch(Action<int> completed, Action<Exception> failed)
        {
            try
            {
                prefetchStarted = true;
                startedBeforeStartupFinished = !startupFinished;
                if (mode == "nested_throw" || mode == "nested_cancel") yield return Nested();
                yield return null;
                prefetchFinished = true;
                if (mode == "failed") failed(new InvalidOperationException("资源预取失败夹具"));
                else completed(42);
            }
            finally { disposed++; }
        }
        IEnumerator Nested()
        {
            try
            {
                yield return null;
                if (mode == "nested_throw") throw new InvalidOperationException("嵌套预取失败夹具");
            }
            finally { nestedDisposed++; }
        }

        var prefetch = pipeline.Prefetch<int>(Prefetch);
        IEnumerator Startup()
        {
            for (int i = 0; i < (mode == "busy" || mode == "busy_interactive" ? 5 : 2); i++)
            {
                if (mode == "busy" || mode == "busy_interactive") blockedFrame = frame;
                yield return null;
            }
            startupFinished = true;
        }
        IEnumerator Content()
        {
            prefetchFinishedBeforeContent = prefetchFinished;
            yield return prefetch.Wait(result => { value = result; published = true; });
            if (mode == "consume_twice") yield return prefetch.Wait(result => value = result);
        }
        pipeline.Add("startup", "菜单", 1, Startup);
        pipeline.Add("content", "内容", 1, Content, "startup");
        IEnumerator runner = pipeline.Run();
        try
        {
            while (runner.MoveNext())
            {
                frame++;
                if ((mode == "cancel" || mode == "nested_cancel") && prefetchStarted) break;
                if (frame > 128) throw new InvalidOperationException("资源预取夹具超过步数上限。");
            }
        }
        finally { (runner as IDisposable)?.Dispose(); }
        return new JObject
        {
            ["startedBeforeStartupFinished"] = startedBeforeStartupFinished,
            ["prefetchFinishedBeforeContent"] = prefetchFinishedBeforeContent,
            ["disposed"] = disposed, ["nestedDisposed"] = nestedDisposed, ["value"] = value, ["published"] = published,
            ["failure"] = pipeline.Failure?.GetType().Name, ["stage"] = pipeline.CurrentStage
        };
    }

    private static JObject ServiceResourcePrefetchFairness(JObject args)
    {
        int frame = 0, blockedFrame = -1, foregroundSteps = 0, disposed = 0, firstValue = 0, secondValue = 0;
        int firstLastFrame = -1, secondLastFrame = -1;
        bool firstFinished = false, secondStartedBeforeFirstFinished = false, repeatedSameFrame = false;
        using var pipeline = new ResourceLoadPipeline((_, _) => { }, () => false,
            () => blockedFrame == frame ? 0d : double.MaxValue, () => frame);
        IEnumerator Prefetch(bool isFirst, Action<int> completed)
        {
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    if (isFirst)
                    {
                        repeatedSameFrame |= firstLastFrame == frame;
                        firstLastFrame = frame;
                    }
                    else
                    {
                        repeatedSameFrame |= secondLastFrame == frame;
                        secondLastFrame = frame;
                        secondStartedBeforeFirstFinished |= !firstFinished;
                    }
                    blockedFrame = frame;
                    yield return null;
                }
                if (isFirst) firstFinished = true;
                completed(isFirst ? 7 : 9);
            }
            finally { disposed++; }
        }
        var first = pipeline.Prefetch<int>((done, _) => Prefetch(true, done));
        var second = pipeline.Prefetch<int>((done, _) => Prefetch(false, done));
        IEnumerator Startup()
        {
            for (int i = 0; i < 5; i++) { foregroundSteps++; yield return null; }
        }
        IEnumerator Content()
        {
            yield return first.Wait(value => firstValue = value);
            yield return second.Wait(value => secondValue = value);
        }
        pipeline.Add("startup", "菜单", 1, Startup);
        pipeline.Add("content", "内容", 1, Content, "startup");
        IEnumerator runner = pipeline.Run();
        bool checkedSameFrame = false;
        try
        {
            while (runner.MoveNext())
            {
                if (!checkedSameFrame)
                {
                    blockedFrame = -1;
                    if (!runner.MoveNext()) throw new InvalidOperationException("预取公平性夹具提前结束。");
                    checkedSameFrame = true;
                }
                if (++frame > 128) throw new InvalidOperationException("预取公平性夹具超过步数上限。");
            }
        }
        finally { (runner as IDisposable)?.Dispose(); }
        return new JObject
        {
            ["foregroundSteps"] = foregroundSteps, ["disposed"] = disposed,
            ["firstValue"] = firstValue, ["secondValue"] = secondValue,
            ["secondStartedBeforeFirstFinished"] = secondStartedBeforeFirstFinished,
            ["repeatedSameFrame"] = repeatedSameFrame, ["failure"] = pipeline.Failure?.GetType().Name
        };
    }

    private static JObject ServiceResourcePrefetchCandidate(JObject args)
    {
        string mode = Text(args, "mode", "complete");
        var active = new Dictionary<string, int> { ["official"] = 7 };
        Dictionary<string, int> official = active;
        var context = new ResourceReloadContext();
        context.AddDictionary(() => active, value => active = value);
        int frame = 0, disposed = 0;
        bool candidateContext = true, officialBetweenFrames = true;
        using var pipeline = new ResourceLoadPipeline((_, _) => { }, () => false, () => double.MaxValue, () => frame);
        IEnumerator Prefetch(Action<int> completed, Action<Exception> failed)
        {
            try
            {
                candidateContext &= !ReferenceEquals(active, official);
                int first = active["candidate"];
                yield return null;
                candidateContext &= !ReferenceEquals(active, official);
                if (mode == "failed") failed(new InvalidOperationException("候选预取失败夹具"));
                else completed(first + active["candidate"]);
            }
            finally { disposed++; }
        }
        var prefetch = pipeline.Prefetch<int>(Prefetch);
        IEnumerator Startup()
        {
            active["candidate"] = 11;
            yield return null;
            active["candidate"] = 13;
            yield return null;
        }
        IEnumerator Content() { yield return prefetch.Wait(value => active["result"] = value); }
        pipeline.Add("startup", "菜单", 1, Startup);
        pipeline.Add("content", "内容", 1, Content, "startup");
        IEnumerator runner = context.Run(pipeline.Run(), () => true);
        try
        {
            while (runner.MoveNext())
            {
                officialBetweenFrames &= ReferenceEquals(active, official) && active["official"] == 7;
                official["live"] = ++frame;
                if (mode == "cancel") break;
                if (frame > 128) throw new InvalidOperationException("候选预取夹具超过步数上限。");
            }
        }
        finally { (runner as IDisposable)?.Dispose(); }
        if (mode == "complete" && pipeline.Failure == null) context.Commit();
        return new JObject
        {
            ["candidateContext"] = candidateContext, ["officialBetweenFrames"] = officialBetweenFrames,
            ["officialProgressPreserved"] = official.TryGetValue("live", out int live) && live == frame,
            ["disposed"] = disposed, ["committed"] = !ReferenceEquals(active, official),
            ["result"] = active.TryGetValue("result", out int result) ? result : 0,
            ["failure"] = pipeline.Failure?.GetType().Name, ["stage"] = pipeline.CurrentStage
        };
    }

    #endregion

    #region 世界液体资源计划采样

    private static JObject ServiceWorldLiquidResourcePlan(JObject args)
    {
        LiquidDefinition Liquid(string id, string sprite, string material) => new(
            id, id, "", "", "", Color.white, 1f, false, 0f, null, null,
            worldWater: new WorldLiquidSettings { SpriteAddress = sprite, MaterialAddress = material });
        var sprites = new Dictionary<string, string>(StringComparer.Ordinal);
        var materials = new Dictionary<string, string>(StringComparer.Ordinal);
        GameRes.CollectWorldLiquidResourceAddresses(new[]
        {
            Liquid("first", "shared-sprite", "shared-material"),
            Liquid("second", "  shared-sprite  ", " shared-material "),
            Liquid("third", "other-sprite", "shared-material")
        }, sprites, materials);
        bool invalidRejected = false;
        try
        {
            GameRes.CollectWorldLiquidResourceAddresses(new[] { Liquid("invalid", "", "shared-material") },
                new Dictionary<string, string>(), new Dictionary<string, string>());
        }
        catch (System.IO.InvalidDataException) { invalidRejected = true; }
        return new JObject
        {
            ["spriteCount"] = sprites.Count, ["materialCount"] = materials.Count,
            ["sharedSpriteOwner"] = sprites["shared-sprite"], ["sharedMaterialOwner"] = materials["shared-material"],
            ["invalidRejected"] = invalidRejected
        };
    }

    #endregion

    #region Actor 目录输入采样

    private static JObject ServiceActorCatalogInputs(JObject args)
    {
        var roots = new[]
        {
            ActorDefinitionCatalogLoader.ConvertActorCatalogToItemRoot(
                "{\"schemaVersion\":1,\"actors\":[{\"id\":\"base\",\"abstract\":true,\"weight\":3,\"modules\":{\"m\":{\"prefab\":\"Module_Example\"}}}]}", "base"),
            ActorDefinitionCatalogLoader.ConvertActorCatalogToItemRoot(
                "{\"schemaVersion\":1,\"actors\":[{\"id\":\"child\",\"parent\":\"base\",\"weight\":5}]}", "child")
        };
        List<JObject> resolved = ItemDefinitionCatalogLoader.ResolveDefinitionObjectsFromCatalogRoots(roots);
        bool invalidRejected = false;
        try { ActorDefinitionCatalogLoader.ConvertActorCatalogToItemRoot("{\"schemaVersion\":1}", "invalid"); }
        catch (System.IO.InvalidDataException) { invalidRejected = true; }
        JObject child = resolved.Find(value => value.Value<string>("id") == "child");
        return new JObject
        {
            ["count"] = resolved.Count, ["childWeight"] = child?.Value<float>("weight"),
            ["inheritedPrefab"] = (string)child?["modules"]?["m"]?["prefab"], ["invalidRejected"] = invalidRejected
        };
    }

    #endregion

    #region 分包解析采样

    private static JObject ServiceCatalogPackageDeserialization(JObject args)
    {
        int builds = JsonConfigurationCache.Builds;
        RecipeCatalogDto recipes = RecipeRuntimeFactory.DeserializeWithoutDocumentCache(
            "{\"schemaVersion\":1,\"recipes\":[{\"id\":\"recipe:test\"}]}");
        BuffCatalogDto buffs = BuffDefinitionFactory.DeserializeWithoutDocumentCache(
            "{\"schemaVersion\":1,\"buffs\":[{\"id\":\"buff:test\",\"effects\":[]}]}");
        bool strictBuffRejected = false;
        try { BuffDefinitionFactory.DeserializeWithoutDocumentCache("{\"schemaVersion\":1,\"buffs\":[],\"unknown\":true}"); }
        catch (Newtonsoft.Json.JsonSerializationException) { strictBuffRejected = true; }
        return new JObject
        {
            ["recipeId"] = recipes.Recipes[0].Id, ["buffId"] = buffs.Buffs[0].Id,
            ["noNestedCacheBuild"] = JsonConfigurationCache.Builds == builds, ["strictBuffRejected"] = strictBuffRejected
        };
    }

    #endregion

    #region 资源队列规则采样

    private static JObject ServiceItemSpriteCompilationPipeline(JObject args)
    {
        string mode = Text(args, "mode", "complete");
        var definitions = new List<ItemDefinitionDto>
        {
            new() { Id = "empty" },
            new() { Id = "both", Visual = new ItemVisualDefinitionDto
            {
                SpriteAddress = " A ", SpriteStates = new Dictionary<string, string> { ["same"] = "A", ["other"] = "B" }
            } },
            new() { Id = "a", Visual = new ItemVisualDefinitionDto { SpriteAddress = "A" } },
            new() { Id = "b", Visual = new ItemVisualDefinitionDto { SpriteAddress = "B" } },
            new() { Id = "abstract", Abstract = true, Visual = new ItemVisualDefinitionDto { SpriteAddress = "C" } }
        };
        var readiness = new ItemDefinitionCatalogLoader.SpriteDefinitionCompilationReadiness(definitions);
        var caseReadiness = new ItemDefinitionCatalogLoader.SpriteDefinitionCompilationReadiness(new[]
        {
            new ItemDefinitionDto { Visual = new ItemVisualDefinitionDto { SpriteAddress = "A" } },
            new ItemDefinitionDto { Visual = new ItemVisualDefinitionDto { SpriteAddress = "a" } }
        });
        var addresses = new JArray();
        foreach (string address in readiness.SpriteAddresses) addresses.Add(address);
        var compiled = new bool[definitions.Count];
        var order = new JArray();
        int frame = 0, blockedFrame = -1, started = 0, disposed = 0, beforeAllSprites = 0;
        int lastWorkFrame = -1, workInFrame = 0, maxWorkPerFrame = 0, compiledInQueue = 0;
        bool loadedA = false, loadedB = false, builtWithoutDependencies = false, failed = false, finished = false, insideQueue = false;
        bool ShouldYield() => blockedFrame == frame;
        bool CompileOne()
        {
            if (failed || !readiness.TryTakeReadyDefinition(out int index)) return false;
            if (lastWorkFrame != frame) { lastWorkFrame = frame; workInFrame = 0; }
            maxWorkPerFrame = Math.Max(maxWorkPerFrame, ++workInFrame);
            if (readiness.HasPendingSprites) beforeAllSprites++;
            if (insideQueue && readiness.HasPendingSprites) compiledInQueue++;
            builtWithoutDependencies |= index == 1 && (!loadedA || !loadedB) || index == 2 && !loadedA || index == 3 && !loadedB;
            compiled[index] = true;
            order.Add(index);
            if (mode == "failed" && index == 2) failed = true;
            if (mode == "budget") blockedFrame = frame;
            return !failed;
        }
        IEnumerator Requests()
        {
            IEnumerator queue = ResourceLoadQueue.Run<int>(2, index => { started++; return index; },
                index =>
                {
                    bool done = frame >= (index == 0 ? 2 : 4);
                    if (mode == "busy" && !done) blockedFrame = frame;
                    return done;
                }, (index, _) =>
                {
                    if (index == 0) loadedA = true; else loadedB = true;
                    readiness.MarkSpriteReady(index == 0 ? "A" : "B");
                    readiness.MarkSpriteReady(index == 0 ? "A" : "B");
                    if (mode == "busy") blockedFrame = frame;
                }, () => failed || ShouldYield(),
                _ => { if (mode == "busy" || mode == "completion") blockedFrame = frame; }, availableWork: CompileOne);
            try
            {
                while (true)
                {
                    bool moved;
                    insideQueue = true;
                    try { moved = queue.MoveNext(); }
                    finally { insideQueue = false; }
                    if (!moved) break;
                    yield return queue.Current;
                }
            }
            finally { (queue as IDisposable)?.Dispose(); disposed++; }
        }
        IEnumerator runner = ItemDefinitionCatalogLoader.RunAssetQueueWithAvailableMainThreadWork(
            Requests(), CompileOne, ShouldYield, () => failed);
        try
        {
            while (runner.MoveNext())
            {
                if (mode == "cancel") break;
                if (++frame > 32) throw new InvalidOperationException("物品 Sprite 流水测试超过步数上限。");
            }
            finished = mode != "cancel";
        }
        finally { (runner as IDisposable)?.Dispose(); }
        var published = new JArray();
        if (finished && !failed)
        {
            while (readiness.HasReadyDefinitions)
            {
                if (ShouldYield()) frame++;
                if (!CompileOne()) break;
            }
            for (int i = 0; i < compiled.Length; i++) if (compiled[i]) published.Add(i);
        }
        return new JObject
        {
            ["started"] = started, ["disposed"] = disposed, ["beforeAllSprites"] = beforeAllSprites,
            ["builtWithoutDependencies"] = builtWithoutDependencies, ["failed"] = failed,
            ["compiledInQueue"] = compiledInQueue,
            ["maxWorkPerFrame"] = maxWorkPerFrame, ["order"] = order, ["published"] = published, ["addresses"] = addresses,
            ["pendingSprites"] = readiness.HasPendingSprites, ["readinessCaseSensitive"] = caseReadiness.SpriteAddresses.Count == 2
        };
    }

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
        const string directSpritePath = "Assets/6_Art/Generated/Atmosphere/AtmosphereAnalyzer/AtmosphereAnalyzer.png";
        const string slicedSpritePath = "Assets/6_Art/Tilemap/Grass.png[Water_0]";
        bool MatchesSprite(string path)
        {
            if (!ItemDefinitionCatalogLoader.TryLoadEditorSprite(path, out Sprite expected, out _)) return false;
            var location = new ResourceLocationBase(path, path, originalProvider.ProviderId, typeof(Sprite));
            var loaded = manager.ProvideResource<Sprite>(ResourceAssetScope.RemapEditorLoadLocation(manager, location, typeof(Sprite)));
            bool matches = loaded.IsDone && loaded.Status == AsyncOperationStatus.Succeeded && loaded.Result == expected;
            manager.Release(loaded);
            return matches;
        }
        bool directSpriteMatches = MatchesSprite(directSpritePath);
        bool slicedSpriteMatches = MatchesSprite(slicedSpritePath);
        return new JObject
        {
            ["completedImmediately"] = completedImmediately, ["loadedExpectedPrefab"] = loadedExpectedPrefab,
            ["directSpriteMatches"] = directSpriteMatches, ["slicedSpriteMatches"] = slicedSpriteMatches,
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
            bool missingColliderAccepted = !Rejected(() => prepared.ValidateTarget(prototype));
            source.AddComponent<CircleCollider2D>();
            bool changedColliderRejected = Rejected(() => prepared.ValidateTarget(prototype));

            GameItem coldOwner = first.AddComponent<GameItem>();
            var a = first.AddComponent<GameplayTestPreparedParameterModule>();
            var b = second.AddComponent<GameplayTestPreparedParameterModule>();
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
                ["missingColliderAccepted"] = missingColliderAccepted, ["changedColliderRejected"] = changedColliderRejected,
                ["coldColliderDeclared"] = first.GetComponent<Collider2D>() == null &&
                    ItemPhysicsDefinitionRuntime.GetItemPhysicsConfiguration(coldOwner).Colliders.TryGetValue(a, out var declared) &&
                    declared.Type == "BoxCollider2D" && declared.Size == new Vector2(2, 3),
                ["nullTargetRejected"] = Rejected(() => prepared.ValidateTarget(null)),
                ["independent"] = independent, ["reapplied"] = a.Values[0] == 1,
                ["colliderApplied"] = second.GetComponent<BoxCollider2D>().size == new Vector2(2, 3),
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
