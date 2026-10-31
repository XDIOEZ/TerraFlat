using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>首次加载的吞吐策略与配置预取发布边界，只采样正式生产入口。</summary>
public static partial class GameplayTestSystemProbe
{
    #region 首次加载规则

    private static void RegisterStartupPerformanceOperations(Dictionary<string, Func<JObject, JObject>> target)
    {
        RegisterBuiltInBinaryCatalogOperations(target);
        RegisterBakedDefinitionCompilationOperations(target);
        target.Add("core_lifecycle/startup_loading_policy", StartupLoadingPolicy);
        target.Add("core_lifecycle/startup_request_batch", StartupRequestBatch);
        target.Add("core_lifecycle/startup_editor_sprite_request_route", StartupEditorSpriteRequestRoute);
        target.Add("core_lifecycle/startup_catalog_input_transaction", StartupCatalogInputTransaction);
        target.Add("core_lifecycle/startup_supplemental_inputs", StartupSupplementalCatalogInputs);
        target.Add("core_lifecycle/startup_private_text_parser", StartupPrivateTextParser);
        target.Add("core_lifecycle/startup_configuration_publication", StartupConfigurationPublication);
        target.Add("core_lifecycle/startup_local_item_catalog", StartupLocalItemCatalog);
        target.Add("core_lifecycle/startup_behavior_tree_templates", StartupBehaviorTreeTemplates);
        target.Add("core_lifecycle/startup_module_parameter_templates", StartupModuleParameterTemplates);
        target.Add("core_lifecycle/startup_concurrent_stages", StartupConcurrentStages);
        target.Add("core_lifecycle/startup_eager_data_request", StartupEagerDataRequest);
        target.Add("core_lifecycle/startup_production_dependencies", StartupProductionDependencies);
        target.Add("core_lifecycle/startup_small_configuration", StartupSmallConfiguration);
        target.Add("core_lifecycle/startup_resident_sprites", StartupResidentSprites);
        target.Add("core_lifecycle/startup_definition_default_copies", StartupDefinitionDefaultCopies);
        target.Add("core_lifecycle/startup_execution_timing", StartupExecutionTiming);
    }

    private static JObject StartupLoadingPolicy(JObject args)
    {
        bool initial = args.Value<bool>("initial"), reload = args.Value<bool>("reload");
        double? budget = GameRes.ResolveResourceWorkBudget(initial, reload, args.Value<bool>("testCache"));
        return new JObject
        {
            ["background"] = GameRes.ShouldKeepStartupResourceLoadingInBackground(
                args.Value<bool>("menuReady"), args.Value<bool>("enteringWorld"), initial),
            ["explicitBudget"] = budget.HasValue,
            ["budgetMs"] = budget ?? -1d,
            ["targetMs"] = GameRes.InitialResourceLoadTargetMilliseconds
        };
    }

    private static JObject StartupConfigurationPublication(JObject args)
    {
        string mode = Text(args, "mode", "complete");
        int frame = 0, disposed = 0, publications = 0;
        bool initialized = false, parsedBeforeInitialization = false, publishedTooEarly = false;
        using var pipeline = new ResourceLoadPipeline((_, _) => { }, () => false,
            () => double.MaxValue, () => frame);
        IEnumerator Configuration(Action<string> done, Action<Exception> fail)
        {
            try
            {
                yield return null;
                parsedBeforeInitialization = !initialized;
                if (mode == "failed") fail(new InvalidOperationException("配置读取失败夹具"));
                else done(mode == "null" ? null : "configuration");
            }
            finally { disposed++; }
        }
        IEnumerator Initialization()
        {
            for (int i = 0; i < 4; i++) yield return null;
            initialized = true;
        }
        void Publish(string value)
        {
            publishedTooEarly |= !initialized;
            publications++;
        }
        IEnumerator Bind(string value) { Publish(value); yield return null; }
        Func<IEnumerator> publish = args.Value<bool>("deferredBinding")
            ? GameRes.PrepareCatalogWithDeferredBinding<string>(pipeline, Configuration, Bind)
            : GameRes.PrepareConfigurationCatalog<string>(pipeline, Configuration, Publish);
        pipeline.Add("initialization", "资源初始化", 1, Initialization);
        pipeline.Add("configuration", "发布配置", 1, publish, "initialization");
        IEnumerator runner = pipeline.Run();
        try
        {
            while (runner.MoveNext())
            {
                if (++frame > 32) throw new InvalidOperationException("启动配置夹具未收敛。");
                if (mode == "cancel") break;
            }
        }
        finally { (runner as IDisposable)?.Dispose(); }
        return new JObject
        {
            ["parsedBeforeInitialization"] = parsedBeforeInitialization,
            ["publishedTooEarly"] = publishedTooEarly,
            ["publications"] = publications,
            ["disposed"] = disposed,
            ["failure"] = pipeline.Failure?.GetType().Name,
            ["stage"] = pipeline.CurrentStage
        };
    }

    #endregion

    #region 独立输入一次发出与后台事务

    private static JObject StartupPrivateTextParser(JObject args)
    {
        string directory = Path.Combine(Path.GetTempPath(), "FlatWorldPrivateParser", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "value.json");
        File.WriteAllText(path, "{\"value\":17}", new System.Text.UTF8Encoding(true));
        int main = System.Threading.Thread.CurrentThread.ManagedThreadId, worker = 0, callback = 0, value = 0, errors = 0;
        using var began = new System.Threading.ManualResetEventSlim();
        try
        {
            IEnumerator request = StreamingAssetsTextLoader.ReadAndParsePrivateDataAsync(path, text =>
            { worker = System.Threading.Thread.CurrentThread.ManagedThreadId; began.Set(); return JObject.Parse(text).Value<int>("value"); },
                result => { callback = System.Threading.Thread.CurrentThread.ManagedThreadId; value = result; }, _ => errors++);
            bool started = began.Wait(TimeSpan.FromSeconds(5)), pending = value == 0;
            DrainStartupInputRoutine(request);
            DrainStartupInputRoutine(StreamingAssetsTextLoader.ReadAndParsePrivateDataAsync<int>(path,
                _ => throw new InvalidDataException("解析失败夹具"), _ => value = -1,
                error => { if (error is InvalidDataException && System.Threading.Thread.CurrentThread.ManagedThreadId == main) errors++; }));
            DrainStartupInputRoutine(StreamingAssetsTextLoader.ReadAndParsePrivateDataAsync(path + ".missing", text => text,
                _ => value = -2, error => { if (error is IOException) errors++; }));
            DrainStartupInputRoutine(StreamingAssetsTextLoader.ReadAndParsePrivateDataAsync<string>(null, text => text,
                _ => value = -3, error => { if (error is ArgumentException) errors++; }));
            return new JObject { ["startedImmediately"] = started, ["notPublishedEarly"] = pending,
                ["workerIsPrivate"] = worker != main, ["callbackIsMain"] = callback == main,
                ["value"] = value, ["errors"] = errors };
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void DrainStartupInputRoutine(IEnumerator routine)
    {
        var pending = new Stack<IEnumerator>();
        pending.Push(routine);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (pending.Count != 0)
            {
                if (timer.Elapsed.TotalSeconds > 5) throw new TimeoutException("输入事务夹具未结束");
                IEnumerator current = pending.Peek();
                if (!current.MoveNext()) { pending.Pop(); (current as IDisposable)?.Dispose(); }
                else if (current.Current is IEnumerator nested) pending.Push(nested);
                else System.Threading.Thread.Sleep(1);
            }
        }
        finally { while (pending.Count != 0) (pending.Pop() as IDisposable)?.Dispose(); }
    }

    private static JObject StartupSupplementalCatalogInputs(JObject args)
    {
        string kind = Text(args, "kind", "tiles");
        string root = Path.Combine(Path.GetTempPath(), "FlatWorldSupplementalInputs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.json"), manifest = Path.Combine(root, "manifest.json");
        string cachePath = Path.Combine(root, "catalog.fwbin");
        try
        {
            if (kind == "tiles")
            {
                JObject originalManifest = JObject.Parse(File.ReadAllText(TileDefinitionCatalogLoader.BuiltInManifestPath));
                JObject package = (JObject)originalManifest["packages"][0];
                File.Copy(StreamingAssetsTextLoader.CombinePath(TileDefinitionCatalogLoader.BuiltInRoot, package.Value<string>("path")), source);
                File.WriteAllText(manifest, "{\"schemaVersion\":1,\"packages\":[{\"id\":\"fixture\",\"path\":\"source.json\",\"enabled\":true}]}");
            }
            else File.Copy(AnimalSkillCatalogLoader.BuiltInConfigPath, source);
            BinaryContentCache Session() => new(cachePath, "supplemental-v1", root, kind == "tiles" ? manifest : source);
            int Read(BinaryContentCache cache)
            {
                Task<int> task = Task.Run(() => kind == "tiles"
                    ? TileDefinitionCatalogLoader.LoadLocalCatalogInput(cache, root, manifest).Definitions.Count
                    : AnimalSkillCatalogLoader.LoadLocalCatalogInput(cache, source).Skills.Count);
                if (!task.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("补充目录输入超时");
                return task.GetAwaiter().GetResult();
            }
            bool Rejected()
            {
                try { Read(Session()); return false; }
                catch (Exception error) { return error.GetBaseException() is InvalidDataException or IOException or Newtonsoft.Json.JsonException; }
            }
            var cold = Session(); int count = Read(cold);
            var warm = Session(); int warmCount = Read(warm);
            var result = new JObject { ["coldBuilt"] = count > 0 && !cold.IsHit && File.Exists(cachePath),
                ["warmRead"] = warm.IsHit && warmCount == count };
            File.WriteAllText(cachePath, "corrupt");
            result["corruptFallback"] = Read(Session()) == count;
            File.WriteAllText(source, "{\"schemaVersion\":0}");
            result["invalidRejected"] = Rejected();
            File.Delete(source);
            result["missingRejected"] = Rejected();
            return result;
        }
        finally { Directory.Delete(root, true); }
    }

    private static JObject StartupEditorSpriteRequestRoute(JObject args)
    {
#if UNITY_EDITOR
        var provider = new UnityEngine.ResourceManagement.ResourceProviders.AssetDatabaseProvider();
        return new JObject
        {
            ["available"] = true,
            ["initialUsesSingleRequest"] = ResourceAssetScope.ShouldLoadInitialEditorSpriteDirectly(provider, true),
            ["reloadKeepsAsyncPreparation"] = !ResourceAssetScope.ShouldLoadInitialEditorSpriteDirectly(provider, false),
            ["customNotOverridden"] = !ResourceAssetScope.ShouldLoadInitialEditorSpriteDirectly(new GameplayTestCustomAssetProvider(), true),
            ["missingProviderRejected"] = !ResourceAssetScope.ShouldLoadInitialEditorSpriteDirectly(null, true)
        };
#else
        return new JObject { ["available"] = false };
#endif
    }

    private static JObject StartupRequestBatch(JObject args)
    {
        string mode = Text(args, "mode", "complete");
        int frame = 0, requests = 0, callbacks = 0, publications = 0, beganAtFirstWork = -1;
        bool initialized = false, publishedEarly = false;
        using var plan = new ResourceLoadPipeline((_, _) => { }, () => false,
            () => double.MaxValue, () => frame, concurrentStages: true);
        IEnumerator Request(Action<int> done, Action<Exception> failed)
        {
            requests++;
            if (mode == "failed") throw new InvalidDataException("独立输入发出失败夹具");
            return Complete();
            IEnumerator Complete()
            {
                yield return null;
                callbacks++;
                done(7);
            }
        }
        var first = plan.Prefetch<int>(Request);
        var second = plan.Prefetch<int>(Request);
        IEnumerator Initialization()
        {
            beganAtFirstWork = requests;
            for (int i = 0; i < 3; i++) yield return null;
            initialized = true;
        }
        IEnumerator Publish()
        {
            void Accept(int value) { publishedEarly |= !initialized; publications += value; }
            yield return first.Wait(Accept);
            yield return second.Wait(Accept);
        }
        plan.Add("initialize", "独立初始化", 1, Initialization);
        plan.Add("publish", "输入交接", 1, Publish, mode == "invalid_graph" ? "missing" : "initialize");
        _ = plan.CaptureDependencies();
        bool diagnosticIsLazy = requests == 0;
        IEnumerator run = plan.Run();
        try
        {
            while (run.MoveNext())
            {
                if (++frame > 32) throw new TimeoutException("输入批发出夹具没有收敛");
                if (mode == "cancel") break;
            }
        }
        finally { (run as IDisposable)?.Dispose(); }
        int callbacksAtDispose = callbacks;
        ((ResourceLoadPipeline.IPrefetchedResourceData)first).StartRequest();
        ((ResourceLoadPipeline.IPrefetchedResourceData)first).Advance(++frame, () => false);
        return new JObject
        {
            ["diagnosticIsLazy"] = diagnosticIsLazy, ["requests"] = requests,
            ["beganAtFirstWork"] = beganAtFirstWork, ["callbacks"] = callbacks,
            ["publications"] = publications, ["publishedEarly"] = publishedEarly,
            ["noCallbackAfterDispose"] = callbacksAtDispose == callbacks,
            ["failure"] = plan.Failure?.GetType().Name, ["stage"] = plan.CurrentStage
        };
    }

    private static JObject StartupCatalogInputTransaction(JObject args)
    {
        string kind = Text(args, "kind", "recipes");
        string root = Path.Combine(Path.GetTempPath(), "FlatWorldInputTransaction", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string manifest = Path.Combine(root, "manifest.json"), package = Path.Combine(root, "package.json");
        string cachePath = Path.Combine(root, "catalog.fwbin");
        string manifestJson = "{\"schemaVersion\":1,\"packages\":[{\"id\":\"fixture\",\"path\":\"package.json\",\"enabled\":true}," +
            "{\"id\":\"off\",\"path\":\"missing.json\",\"enabled\":false}]}";
        string source = kind == "recipes" ? "{\"schemaVersion\":1,\"recipes\":[{\"id\":\"fixture-one\"}]}" :
            kind == "buffs" ? "{\"schemaVersion\":1,\"buffs\":[{\"id\":\"fixture-one\",\"effects\":[]}]}" :
            "{\"schemaVersion\":1,\"actors\":[{\"id\":\"fixture-one\",\"modules\":{\"ai\":{\"prefab\":\"fixture\"," +
            "\"parameters\":{\"behaviorTree\":{\"root\":{\"type\":\"subtree\",\"name\":\"fixture\"}}}}}}]}";
        BinaryContentCache Session() => new(cachePath, "input-transaction-v1", root, manifest);
        object Read(BinaryContentCache cache, bool reload = false)
        {
            Task<object> task = Task.Run<object>(() => kind switch
            {
                "recipes" => RecipeCatalogLoader.LoadLocalCatalogInput(cache, root, manifest),
                "buffs" => BuffCatalogLoader.LoadLocalCatalogInput(cache, root, manifest),
                _ => ActorDefinitionCatalogLoader.LoadLocalCatalogInput(cache, root, manifest, reload)
            });
            if (!task.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("后台输入事务夹具超时");
            return task.GetAwaiter().GetResult();
        }
        string Id(object value) => value switch
        {
            RecipeCatalogLoader.RecipeCatalogPreparation recipes => recipes.Catalog.Catalogs[0].Recipes[0].Id,
            BuffCatalogLoader.BuffCatalogPreparation buffs => buffs.Catalog.Catalogs[0].Buffs[0].Id,
            ActorDefinitionCatalogLoader.ActorCatalogPreparation actors => actors.Sources[0].Value<string>("id"),
            _ => throw new InvalidOperationException("输入事务类型不匹配")
        };
        bool Store(BinaryContentCache cache, object value) => value switch
        {
            RecipeCatalogLoader.RecipeCatalogPreparation recipes => cache.Store(recipes.Catalog),
            BuffCatalogLoader.BuffCatalogPreparation buffs => cache.Store(buffs.Catalog),
            ActorDefinitionCatalogLoader.ActorCatalogPreparation actors => cache.Store(new ActorDefinitionInputs
                { Sources = actors.Sources, Definitions = new List<ItemDefinitionDto>() }),
            _ => false
        };
        bool Rejected()
        {
            try { Read(Session()); return false; }
            catch (Exception error) { return error.GetBaseException() is InvalidDataException or IOException or Newtonsoft.Json.JsonException; }
        }
        try
        {
            File.WriteAllText(manifest, manifestJson);
            File.WriteAllText(package, source);
            File.WriteAllText(Path.Combine(root, "behavior-subtrees.json"),
                "{\"subtrees\":{\"fixture\":\"tree.json\",\"nested\":\"leaf.json\",\"unused\":\"leaf.json\"}}");
            File.WriteAllText(Path.Combine(root, "tree.json"), "{\"root\":{\"type\":\"subtree\",\"name\":\"nested\"}}");
            File.WriteAllText(Path.Combine(root, "leaf.json"), "{\"root\":{\"type\":\"condition\",\"name\":\"always\"}}");
            var cold = Session();
            object original = Read(cold);
            var result = new JObject { ["coldRead"] = !cold.IsHit && Id(original) == "fixture-one",
                ["stored"] = Store(cold, original) };
            var warm = Session();
            result["warmRead"] = Id(Read(warm)) == "fixture-one" && warm.IsHit;
            if (kind == "actors")
            {
                var reload = Session();
                result["reloadBypassesCache"] = Id(Read(reload, true)) == "fixture-one" && !reload.IsHit;
                var actors = (ActorDefinitionCatalogLoader.ActorCatalogPreparation)original;
                result["sharedTreeAttached"] = actors.Sources[0]["modules"]?["ai"]?["parameters"]?["behaviorTree"]?["subtrees"]?["fixture"] != null;
                ItemDefinitionDto compact = ActorDefinitionCatalogLoader.ConvertResolvedActor(actors.Sources[0]);
                JObject compactTree = (JObject)compact.Modules["ai"].Parameters["behaviorTree"];
                result["runtimeKeepsReferenceClosure"] = ((JObject)compactTree["subtrees"]).Count == 2 &&
                    compactTree["subtrees"]["fixture"] != null && compactTree["subtrees"]["nested"] != null &&
                    compactTree["subtrees"]["unused"] == null &&
                    AnimalBehaviorTreeNodeCatalog.Compile(compactTree.ToObject<AnimalBehaviorTreeDefinition>())
                        .CreateRuntime().Tick(0) == AnimalBehaviorTreeStatus.Success;
                compactTree["subtrees"]["nested"]["root"]["name"] = "changed";
                var originalLibrary = (JObject)actors.Sources[0]["modules"]["ai"]["parameters"]["behaviorTree"]["subtrees"];
                result["completeSourceIsolated"] = originalLibrary.Count == 3 &&
                    originalLibrary["nested"]["root"].Value<string>("name") == "always";
                JObject invalid = (JObject)actors.Sources[0].DeepClone();
                invalid["modules"]["ai"]["parameters"]["behaviorTree"]["subtrees"]["nested"]["root"]["name"] = "unknown-condition";
                try { ActorDefinitionCatalogLoader.ConvertResolvedActor(invalid); result["invalidUsedTreeRejected"] = false; }
                catch (Exception) { result["invalidUsedTreeRejected"] = true; }
            }
            DateTime timestamp = File.GetLastWriteTimeUtc(package);
            File.WriteAllText(package, source.Replace("fixture-one", "fixture-two"));
            File.SetLastWriteTimeUtc(package, timestamp);
            var changed = Session();
            result["sameLengthChangedRead"] = Id(Read(changed)) == "fixture-two" && !changed.IsHit && Id(original) == "fixture-one";
            File.WriteAllText(cachePath, "broken");
            result["corruptFallback"] = Id(Read(Session())) == "fixture-two";
            File.Delete(package);
            result["missingRejected"] = Rejected();
            File.WriteAllText(package, "invalid json");
            result["invalidRejected"] = Rejected();
            File.WriteAllText(package, source);
            File.WriteAllText(manifest, manifestJson.Replace("package.json", "../package.json"));
            result["escapeRejected"] = Rejected();
            File.WriteAllText(manifest, manifestJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":0"));
            result["schemaRejected"] = Rejected();
            return result;
        }
        finally { Directory.Delete(root, true); }
    }

    #endregion

    #region 定义默认数据独立复制

    [Serializable]
    private sealed class StartupExtendedItemData : Data_GeneralItem
    {
        public List<int> Extra = new() { 7, 8 };
    }

    [Serializable]
    private sealed class StartupExtendedModuleData : Ex_ModData
    {
        public List<int> Extra = new() { 3, 4 };
        // 扩展即便使用值相等，也不能把不同原型的默认状态合并。
        public override bool Equals(object other) => other is StartupExtendedModuleData;
        public override int GetHashCode() => 1;
    }

    private static JObject StartupDefinitionDefaultCopies(JObject args)
    {
        var originalModule = new Ex_ModData { StableName = "old", ModuleId = "old-capability", BitData = "original" };
        var source = new StartupExtendedItemData
        {
            IDName = "startup-copy", GameName = "复制样本", code = "extension-code",
            Tags = new List<string> { "Material" }, Stack = new ItemStack { Amount = 2, Weight = 3 },
            ModuleDataDic = new Dictionary<string, ModuleData>(StringComparer.Ordinal) { ["old"] = originalModule }
        };
        ModuleDataCollection originalModules = source.ModuleDataDic;
        int structureEvents = 0, massEvents = 0;
        source.ModuleStructureChanged += _ => structureEvents++;
        source.MassChanged += _ => massEvents++;
        var shell = (StartupExtendedItemData)source.CloneDefinitionDefaultsWithoutModules();
        bool noSourceEvents = structureEvents == 0 && massEvents == 0;
        var result = new JObject
        {
            ["shellModulesNotCopied"] = shell.ModuleDataDic == null && ReferenceEquals(source.ModuleDataDic, originalModules),
            ["shellSubtypePreserved"] = shell.GetType() == source.GetType() && shell.code == "extension-code" && shell.Extra[0] == 7,
            ["sourceUntouched"] = noSourceEvents && ReferenceEquals(originalModules["old"], originalModule)
        };
        shell.Extra[0] = 99;
        shell.Tags[0] = "changed";
        shell.Stack.Amount = 5;
        shell.ModuleDataDic = new Dictionary<string, ModuleData>(StringComparer.Ordinal);
        result["shellFieldsIndependent"] = source.Extra[0] == 7 && source.Tags[0] == "Material" && source.Stack.Amount == 2 &&
            source.ModuleDataDic.Count == 1 && massEvents == 0 && structureEvents == 0;
        originalModule.Enabled = false;
        result["sourceOwnerStillBound"] = structureEvents == 1;

        var cache = new ItemModulePrototypeCache();
        var prototype = new Ex_ModData { BitData = "first", Type = ModuleType.Equipment, Enabled = false };
        var first = (Ex_ModData)cache.CreateModuleDefinitionDefaults(prototype);
        first.StableName = "definition-a";
        first.ModuleId = "capability-a";
        first.BitData = "mutated";
        first.Enabled = true;
        var second = (Ex_ModData)cache.CreateModuleDefinitionDefaults(prototype);
        result["factoryReused"] = cache.ModuleDefaultFactoryCount == 1 && cache.ModuleDefaultReuseCount == 1;
        result["unboundDefaultsPreserved"] = second.StableName == null && second.ModuleId == null &&
            !second.Enabled && second.Type == ModuleType.Equipment;
        result["moduleValuesIndependent"] = second.BitData == "first" && prototype.BitData == "first" &&
            prototype.StableName == null && !ReferenceEquals(first, second) && !ReferenceEquals(second, prototype);
        prototype.BitData = "new-source";
        result["sessionSnapshotIsolated"] = ((Ex_ModData)cache.CreateModuleDefinitionDefaults(prototype)).BitData == "first" &&
            ((Ex_ModData)new ItemModulePrototypeCache().CreateModuleDefinitionDefaults(prototype)).BitData == "new-source";

        var stock = new CollectableModuleData { CurrentStock = 23, IsInitialized = true };
        var stockCopy = (CollectableModuleData)cache.CreateModuleDefinitionDefaults(stock);
        stockCopy.CurrentStock = 0;
        var stockAgain = (CollectableModuleData)cache.CreateModuleDefinitionDefaults(stock);
        result["stockIndependent"] = stock.CurrentStock == 23 && stockAgain.CurrentStock == 23 && stockAgain.IsInitialized;

        var binary = new Ex_ModData_MemoryPackable { BitData = new byte[] { 1, 2, 3 } };
        var binaryCopy = (Ex_ModData_MemoryPackable)cache.CreateModuleDefinitionDefaults(binary);
        binaryCopy.BitData[0] = 9;
        var binaryAgain = (Ex_ModData_MemoryPackable)cache.CreateModuleDefinitionDefaults(binary);
        result["binaryPayloadIndependent"] = binary.BitData[0] == 1 && binaryAgain.BitData[0] == 1 &&
            !ReferenceEquals(binaryCopy.BitData, binaryAgain.BitData);

        var extension = new StartupExtendedModuleData { BitData = "custom", Extra = new List<int> { 11, 12 } };
        var extensionCopy = (StartupExtendedModuleData)cache.CreateModuleDefinitionDefaults(extension);
        extensionCopy.Extra[0] = 99;
        var extensionAgain = (StartupExtendedModuleData)cache.CreateModuleDefinitionDefaults(extension);
        result["extensionPreserved"] = extensionAgain.GetType() == extension.GetType() && extensionAgain.BitData == "custom" &&
            extensionAgain.Extra[0] == 11 && extension.Extra[0] == 11;
        var equalButDifferent = new StartupExtendedModuleData { BitData = "other", Extra = new List<int> { 33 } };
        var equalCopy = (StartupExtendedModuleData)cache.CreateModuleDefinitionDefaults(equalButDifferent);
        result["prototypeReferenceIdentity"] = equalButDifferent.Equals(extension) && equalCopy.BitData == "other" && equalCopy.Extra[0] == 33;

        var inventory = new Inventory_ModuleData { Data = null, InventoryInitName = "raw", BasePanelIsOpen = false };
        var inventoryCopy = (Inventory_ModuleData)cache.CreateModuleDefinitionDefaults(inventory);
        var food = new ModData_FoodData { FoodData = null, MechanicStates = null };
        var foodCopy = (ModData_FoodData)cache.CreateModuleDefinitionDefaults(food);
        result["rawNullsNotNormalized"] = inventoryCopy.Data == null && inventoryCopy.InventoryInitName == "raw" &&
            !inventoryCopy.BasePanelIsOpen && foodCopy.FoodData == null && foodCopy.MechanicStates == null;
        try { cache.CreateModuleDefinitionDefaults(null); result["nullRejected"] = false; }
        catch (ArgumentNullException) { result["nullRejected"] = true; }
        return result;
    }

    #endregion

    #region 已驻留贴图复用

    private static JObject StartupResidentSprites(JObject args)
    {
#if UNITY_EDITOR
        const string direct = "Assets/6_Art/Generated/Atmosphere/AtmosphereAnalyzer/AtmosphereAnalyzer.png";
        const string sliced = "Assets/6_Art/Tilemap/Grass.png[Water_0]";
        var requests = new Dictionary<string, UnityEditor.AssetDatabaseLoadOperation>(StringComparer.OrdinalIgnoreCase);
        bool directValid = ItemDefinitionCatalogLoader.TryLoadEditorSprite(direct, out var first, out _);
        bool slicedValid = ItemDefinitionCatalogLoader.TryLoadEditorSprite(sliced, out var second, out _);
        return new JObject
        {
            ["available"] = true,
            ["resident"] = directValid && slicedValid && first != null && second != null,
            ["noRepeatedRequest"] = ItemDefinitionCatalogLoader.BeginEditorSpriteTexture(direct, requests) == null &&
                ItemDefinitionCatalogLoader.BeginEditorSpriteTexture(sliced, requests) == null,
            ["atlasDeduplicated"] = ItemDefinitionCatalogLoader.BeginEditorSpriteTexture("Assets/6_Art/Tilemap/Grass.png", requests) == null && requests.Count == 2,
            ["invalidSliceRejected"] = !ItemDefinitionCatalogLoader.TryLoadEditorSprite("Assets/6_Art/Tilemap/Grass.png[不存在的切片]", out _, out _)
        };
#else
        return new JObject { ["available"] = false };
#endif
    }

    #endregion

    #region 小配置直接解析

    private static JObject StartupSmallConfiguration(JObject args)
    {
        int hits = JsonConfigurationCache.Hits, builds = JsonConfigurationCache.Builds, revision = BinaryContentCache.BuildRevision;
        int parses = 0, invalid = 0;
        const string json = "{\"schemaVersion\":1,\"libraries\":{\"fixture\":[\"first\"]}}";
        TextLibraryCatalogDto Parse()
        {
            parses++;
            return Newtonsoft.Json.JsonConvert.DeserializeObject<TextLibraryCatalogDto>(json);
        }
        TextLibraryCatalogDto first = JsonConfigurationCache.Parse(json, Parse);
        first.Libraries["fixture"][0] = "changed";
        TextLibraryCatalogDto second = JsonConfigurationCache.Parse(json, Parse);
        JObject token = JObject.Parse(json);
        TextLibraryCatalogDto fromToken = JsonConfigurationCache.FromToken<TextLibraryCatalogDto>(token);
        fromToken.Libraries["fixture"][0] = "mutated";
        TextLibraryCatalogDto independent = JsonConfigurationCache.FromToken<TextLibraryCatalogDto>(token);
        var strict = Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
            { MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error });
        for (int i = 0; i < 2; i++)
        {
            try { JsonConfigurationCache.FromToken<TextLibraryCatalogDto>(new JObject { ["unknown"] = true }, strict); }
            catch (Newtonsoft.Json.JsonSerializationException) { invalid++; }
        }
        int boundary = JsonConfigurationCache.MinimumAutomaticCacheCharacters;
        return new JObject
        {
            ["parsedEachTime"] = parses == 2 && second.Libraries["fixture"][0] == "first",
            ["tokenIndependent"] = independent.Libraries["fixture"][0] == "first" && token["libraries"]["fixture"][0].Value<string>() == "first",
            ["invalidRejected"] = invalid == 2,
            ["noDiskCache"] = JsonConfigurationCache.Hits == hits && JsonConfigurationCache.Builds == builds && BinaryContentCache.BuildRevision == revision,
            ["largeConfigurationCached"] = !JsonConfigurationCache.ShouldCacheConfiguration(boundary - 1) &&
                JsonConfigurationCache.ShouldCacheConfiguration(boundary) && JsonConfigurationCache.ShouldCacheConfiguration(int.MaxValue),
            ["nullPreserved"] = JsonConfigurationCache.FromToken<TextLibraryCatalogDto>(null) == null
        };
    }

    #endregion

    #region 正式加载图依赖

    private static JObject StartupProductionDependencies(JObject args)
    {
        var root = new UnityEngine.GameObject("只读启动阶段图");
        root.SetActive(false);
        try
        {
            var resources = root.AddComponent<GameRes>();
            var dependencies = resources.CaptureResourceStageDependencies();
            var result = new JObject();
            foreach (string stage in new[] { "prefabs", "items", "recipes", "buffs", "liquids", "quests" })
                result[stage] = new JArray(dependencies[stage]);
            result["noSessionStarted"] = resources.LoadState == ResourceLoadState.NotStarted;
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    #endregion

    #region 后台数据请求发出时机

    private static JObject StartupEagerDataRequest(JObject args)
    {
        int mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        int workerThread = 0, callbackThread = 0, value = 0, errors = 0, cancelledPublications = 0;
        using var began = new System.Threading.ManualResetEventSlim();
        IEnumerator eager = StreamingAssetsTextLoader.BeginPureDataAsync(() =>
        {
            workerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            began.Set();
            return 17;
        }, result => { value = result; callbackThread = System.Threading.Thread.CurrentThread.ManagedThreadId; }, _ => errors++);
        bool startedImmediately = began.Wait(TimeSpan.FromSeconds(5));
        bool unpublished = value == 0;
        Drain(eager);
        int lazyWork = 0;
        IEnumerator lazy = StreamingAssetsTextLoader.RunPureDataAsync(() =>
            System.Threading.Interlocked.Increment(ref lazyWork), _ => { }, _ => errors++);
        bool lazyPreserved = System.Threading.Volatile.Read(ref lazyWork) == 0;
        Drain(lazy);
        IEnumerator failure = StreamingAssetsTextLoader.BeginPureDataAsync<int>(
            () => throw new InvalidDataException("后台失败夹具"), _ => value = -1,
            exception => { if (exception is InvalidDataException) errors++; });
        Drain(failure);
        using var cancelBegan = new System.Threading.ManualResetEventSlim();
        IEnumerator cancelled = StreamingAssetsTextLoader.BeginPureDataAsync(() =>
        { cancelBegan.Set(); return 9; }, _ => cancelledPublications++, _ => cancelledPublications++);
        bool cancellationStarted = cancelBegan.Wait(TimeSpan.FromSeconds(5));
        (cancelled as IDisposable)?.Dispose();
        return new JObject
        {
            ["startedImmediately"] = startedImmediately, ["unpublishedBeforeAdvance"] = unpublished,
            ["workerIsBackground"] = workerThread != mainThread, ["callbackIsMainThread"] = callbackThread == mainThread,
            ["value"] = value, ["errors"] = errors, ["lazyPreserved"] = lazyPreserved && lazyWork == 1,
            ["cancelledWithoutPublication"] = cancellationStarted && cancelledPublications == 0
        };

        static void Drain(IEnumerator routine)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (routine.MoveNext())
                {
                    if (timer.Elapsed.TotalSeconds > 5) throw new TimeoutException("后台任务夹具超时。");
                    System.Threading.Thread.Sleep(1);
                }
            }
            finally { (routine as IDisposable)?.Dispose(); }
        }
    }

    #endregion

    #region 并行阶段依赖与回收

    private static JObject StartupExecutionTiming(JObject args)
    {
        int frame = 0, completed = 0, released = 0;
        bool cancel = args.Value<bool>("cancel");
        using var plan = new ResourceLoadPipeline((_, _) => { }, () => false,
            () => double.MaxValue, () => frame, concurrentStages: args.Value<bool>("concurrent"));
        IEnumerator Stage()
        {
            try
            {
                System.Threading.Thread.Sleep(2);
                yield return null;
                completed++;
            }
            finally { released++; }
        }
        plan.Add("measured", "实际推进与调度间隔", 1, Stage);
        IEnumerator runner = plan.Run();
        try
        {
            while (runner.MoveNext())
            {
                if (cancel) break;
                System.Threading.Thread.Sleep(5);
                if (++frame > 10) throw new InvalidOperationException("计时夹具没有完成。");
            }
        }
        finally { (runner as IDisposable)?.Dispose(); }
        return new JObject
        {
            ["workMeasured"] = plan.ExecutionMilliseconds > 0,
            ["waitMeasured"] = plan.BetweenAdvancesMilliseconds > 0,
            ["longestBounded"] = plan.LongestBetweenAdvancesMilliseconds <= plan.BetweenAdvancesMilliseconds,
            ["slices"] = plan.ExecutionSlices,
            ["completed"] = completed, ["released"] = released, ["failure"] = plan.Failure?.GetType().Name
        };
    }

    private static JObject StartupConcurrentStages(JObject args)
    {
        string mode = Text(args, "mode", "complete");
        int frame = 0, blockedFrame = -1, lastWorkFrame = -1, workInFrame = 0, maxWorkInFrame = 0;
        int startedA = -1, startedB = -1, finishedA = -1, finishedB = -1, joined = -1, nestedDisposed = 0;
        var disposedOrder = new List<string>();
        float lastProgress = 0;
        bool progressMonotonic = true, dependencyViolated = false;
        using var plan = new ResourceLoadPipeline((_, value) =>
        {
            progressMonotonic &= value >= lastProgress && value >= 0 && value <= 1;
            lastProgress = value;
        }, () => false, () => blockedFrame == frame ? 0d : double.MaxValue, () => frame, concurrentStages: true);
        IEnumerator Work(bool first)
        {
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    if (!first && i == 1 && mode == "failed") throw new InvalidOperationException("并行阶段失败夹具");
                    if (!first && i == 1 && mode == "unsupported") { yield return new object(); yield break; }
                    if (lastWorkFrame != frame) { lastWorkFrame = frame; workInFrame = 0; }
                    maxWorkInFrame = Math.Max(maxWorkInFrame, ++workInFrame);
                    plan.Report((i + 1) / 5f);
                    if (mode == "budget") blockedFrame = frame;
                    yield return null;
                }
            }
            finally { nestedDisposed++; }
        }
        IEnumerator Branch(bool first)
        {
            try
            {
                if (first) startedA = frame; else startedB = frame;
                yield return Work(first);
                if (first) finishedA = frame; else finishedB = frame;
            }
            finally { disposedOrder.Add(first ? "a" : "b"); }
        }
        IEnumerator Join()
        {
            dependencyViolated = finishedA < 0 || finishedB < 0;
            joined = frame;
            yield break;
        }
        plan.Add("a", "独立阶段甲", 2, () => Branch(true));
        plan.Add("b", "独立阶段乙", 3, () => Branch(false));
        plan.Add("join", "依赖收齐", 1, Join, "a", "b");
        IEnumerator runner = plan.Run();
        try
        {
            while (runner.MoveNext())
            {
                if (++frame > 40) throw new InvalidOperationException("并行阶段夹具超出帧数上限。");
                if (mode == "cancel" && frame >= 2) break;
            }
        }
        finally { (runner as IDisposable)?.Dispose(); }
        return new JObject
        {
            ["overlapped"] = startedA >= 0 && startedB >= 0 && (finishedA < 0 || startedB < finishedA),
            ["dependencyViolated"] = dependencyViolated, ["joined"] = joined >= 0,
            ["progressMonotonic"] = progressMonotonic, ["progress"] = lastProgress,
            ["maxWorkPerFrame"] = maxWorkInFrame, ["nestedDisposed"] = nestedDisposed,
            ["disposed"] = new JArray(disposedOrder), ["failure"] = plan.Failure?.GetType().Name,
            ["stage"] = plan.CurrentStage
        };
    }

    #endregion

    #region 模块参数编译复用

    private static JObject StartupModuleParameterTemplates(JObject args)
    {
        var source = new UnityEngine.GameObject("启动参数模板原型");
        var target = new UnityEngine.GameObject("启动参数模板实例");
        source.SetActive(false);
        target.SetActive(false);
        try
        {
            var prototype = source.AddComponent<GameplayTestPreparedParameterModule>();
            var instance = target.AddComponent<GameplayTestPreparedParameterModule>();
            source.AddComponent<UnityEngine.BoxCollider2D>();
            target.AddComponent<UnityEngine.BoxCollider2D>();
            const string json = "{\"Values\":[1,2],\"$collider2D\":{\"type\":\"BoxCollider2D\",\"size\":{\"x\":2,\"y\":3}}}";
            var cache = new ItemModulePrototypeCache();
            var first = cache.PrepareModuleParameters(prototype, "first", "a", "fixture", json);
            var second = cache.PrepareModuleParameters(prototype, "second", "b", "fixture", json);
            var result = new JObject
            {
                ["compiledOnce"] = cache.PreparedParameterTemplateCount == 1 && cache.PreparedParameterReuseCount == 1,
                ["customValidationPreserved"] = prototype.ValidationCalls == 2,
                ["independentBindings"] = !ReferenceEquals(first, second)
            };
            first.Apply(instance);
            instance.Values[0] = 99;
            second.Apply(instance);
            result["independentValues"] = instance.Values[0] == 1 && prototype.Values.Count == 0;
            JObject fields = first.CopyRegularFields(new[] { "values", "absent" });
            fields["values"][0] = 77;
            result["fieldSnapshotsIsolated"] = first.CopyRegularFields(new[] { "Values" })["Values"][0].Value<int>() == 1 &&
                fields.Count == 1 && first.CopyRegularFields(new[] { "absent" }) == null;
            cache.PrepareModuleParameters(instance, "other", "a", "fixture", json);
            result["prototypeSeparated"] = cache.PreparedParameterTemplateCount == 2;
            cache.PrepareModuleParameters(prototype, "changed", "a", "fixture", json.Replace("[1,2]", "[3,4]"));
            result["contentSeparated"] = cache.PreparedParameterTemplateCount == 3;
            prototype.RejectParameters = true;
            try { cache.PrepareModuleParameters(prototype, "reject-current", "c", "fixture", json); }
            catch (InvalidOperationException error) { result["currentValidatorRejected"] = error.Message.Contains("reject-current"); }
            prototype.RejectParameters = false;
            UnityEngine.Object.DestroyImmediate(source.GetComponent<UnityEngine.BoxCollider2D>());
            // 已编译的盒形不能被当前原型中的圆形静默替换。
            source.AddComponent<UnityEngine.CircleCollider2D>();
            try { cache.PrepareModuleParameters(prototype, "missing-current", "c", "fixture", json); }
            catch (InvalidOperationException error) { result["currentStructureRejected"] = error.Message.Contains("missing-current"); }
            result["newSessionEmpty"] = new ItemModulePrototypeCache().PreparedParameterTemplateCount == 0;
            return result;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    #endregion

    #region 行为树启动模板缓存

    private static JObject StartupBehaviorTreeTemplates(JObject args)
    {
        var context = new ResourceReloadContext();
        AnimalBehaviorTreeNodeCatalog.ConfigureResourceReload(context);
        var result = new JObject();
        AnimalBehaviorTreeDefinition Definition() => new()
        { Root = new AnimalBehaviorTreeNodeDefinition { Type = "subtree", Name = "startup-fixture" } };
        var source = new JObject
        {
            ["startup-fixture"] = new JObject
            { ["root"] = new JObject { ["type"] = "condition", ["name"] = "always" } }
        };
        AnimalBehaviorTreeTemplate original = null;
        context.InCandidate(() =>
        {
            AnimalBehaviorTreeNodeCatalog.SetSharedSubtrees(source);
            original = AnimalBehaviorTreeNodeCatalog.Compile(Definition());
            result["equivalentDefinitionReused"] = ReferenceEquals(original,
                AnimalBehaviorTreeNodeCatalog.Compile(Definition()));
            source["startup-fixture"]["root"]["name"] = "invalid-condition";
            var snapshot = AnimalBehaviorTreeNodeCatalog.GetSharedSubtree("startup-fixture");
            snapshot.Root.Name = "invalid-condition";
            result["inputsIsolated"] = AnimalBehaviorTreeNodeCatalog.GetSharedSubtree("startup-fixture").Root.Name == "always";
            var nested = new ResourceReloadContext();
            AnimalBehaviorTreeNodeCatalog.ConfigureResourceReload(nested);
            nested.InCandidate(() =>
            {
                AnimalBehaviorTreeNodeCatalog.SetSharedSubtrees(source);
                try { AnimalBehaviorTreeNodeCatalog.Compile(Definition()); result["changedLibraryRejected"] = false; }
                catch (Exception) { result["changedLibraryRejected"] = true; }
            });
            result["failedCandidatePreservedActive"] = ReferenceEquals(original,
                AnimalBehaviorTreeNodeCatalog.Compile(Definition()));
            source["startup-fixture"]["root"]["name"] = "always";
            AnimalBehaviorTreeNodeCatalog.SetSharedSubtrees(source);
            AnimalBehaviorTreeTemplate replaced = AnimalBehaviorTreeNodeCatalog.Compile(Definition());
            result["newLibraryRecompiled"] = !ReferenceEquals(original, replaced);
            result["runtimeStillValid"] = replaced.CreateRuntime().Tick(0f) == AnimalBehaviorTreeStatus.Success;
        });
        return result;
    }

    #endregion

    #region 本地目录后台事务

    private static JObject StartupLocalItemCatalog(JObject args)
    {
        string root = Path.Combine(Path.GetTempPath(), "FlatWorldStartup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string manifest = Path.Combine(root, "manifest.json"), source = Path.Combine(root, "items.json");
        string cachePath = Path.Combine(root, "items.fwbin");
        const string manifestJson = "{\"schemaVersion\":1,\"packages\":[" +
            "{\"id\":\"fixture\",\"path\":\"items.json\",\"enabled\":true}," +
            "{\"id\":\"disabled\",\"path\":\"missing.json\",\"enabled\":false}]}";
        const string sourceJson = "{\"schemaVersion\":1,\"items\":[" +
            "{\"id\":\"parent\",\"abstract\":true,\"weight\":3}," +
            "{\"id\":\"child\",\"parent\":\"parent\"}]}";
        BinaryContentCache Session() => new(cachePath, "startup-local-v1", root, manifest);
        List<ItemDefinitionDto> Read(BinaryContentCache cache)
        {
            // 工作线程实际执行正式加载器，禁止通过同步主线程假装验证后台路径。
            Task<List<ItemDefinitionDto>> task = Task.Run(() =>
                ItemDefinitionCatalogLoader.LoadLocalDefinitions(cache, root, manifest));
            if (!task.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("本地目录夹具超时。");
            return task.GetAwaiter().GetResult();
        }
        bool Rejected()
        {
            try { Read(Session()); return false; }
            catch (Exception error) { return error.GetBaseException() is InvalidDataException or IOException; }
        }
        try
        {
            File.WriteAllText(manifest, manifestJson);
            File.WriteAllText(source, sourceJson);
            var result = new JObject();
            var cold = Session();
            List<ItemDefinitionDto> definitions = Read(cold);
            result["coldBuilt"] = !cold.IsHit && File.Exists(cachePath) && definitions.Count == 2;
            result["inheritedWeight"] = definitions.Find(value => value.Id == "child")?.Weight;
            var warm = Session();
            definitions.Find(value => value.Id == "child").Weight = 99;
            result["warmIndependent"] = Read(warm).Find(value => value.Id == "child").Weight == 3 && warm.IsHit;
            DateTime timestamp = File.GetLastWriteTimeUtc(source);
            File.WriteAllText(source, sourceJson.Replace("\"weight\":3", "\"weight\":4"));
            File.SetLastWriteTimeUtc(source, timestamp);
            var changed = Session();
            result["contentRebuilt"] = Read(changed).Find(value => value.Id == "child").Weight == 4 && !changed.IsHit;
            File.WriteAllText(cachePath, "corrupt");
            result["corruptRecovered"] = Read(Session()).Count == 2;
            File.Delete(source);
            result["missingRejected"] = Rejected();
            File.WriteAllText(source, sourceJson);
            File.WriteAllText(manifest, manifestJson.Replace("items.json", "../items.json"));
            result["escapeRejected"] = Rejected();
            File.WriteAllText(manifest, manifestJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":0"));
            result["schemaRejected"] = Rejected();
            File.WriteAllText(manifest, manifestJson);
            File.WriteAllText(source, sourceJson.Replace("\"id\":\"parent\",\"abstract\":true",
                "\"id\":\"parent\",\"parent\":\"child\",\"abstract\":true"));
            result["cycleRejected"] = Rejected();
            File.WriteAllText(source, sourceJson);
            File.Delete(cachePath);
            Directory.CreateDirectory(cachePath);
            result["writeFailureFallback"] = Read(Session()).Count == 2;
            return result;
        }
        finally { Directory.Delete(root, true); }
    }

    #endregion
}
