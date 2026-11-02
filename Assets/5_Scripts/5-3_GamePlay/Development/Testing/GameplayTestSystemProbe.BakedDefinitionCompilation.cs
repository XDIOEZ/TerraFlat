using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 构建期模块与行为树规则采样

    private static void RegisterBakedDefinitionCompilationOperations(Dictionary<string, Func<JObject, JObject>> target)
    {
        target.Add("core_lifecycle/baked_module_parameters", ProbeBakedModuleParameters);
        target.Add("core_lifecycle/baked_behavior_tree", ProbeBakedBehaviorTree);
        target.Add("core_lifecycle/animal_detection_radius_contributions", ProbeAnimalDetectionRadiusContributions);
    }

    private static JObject ProbeBakedModuleParameters(JObject args)
    {
        const string moduleName = "fixture", moduleId = "fixture-module";
        const string json = "{\"Values\":[1,2],\"$transform\":{\"localPosition\":{\"x\":1,\"y\":2,\"z\":3}},\"$collider2D\":{\"isTrigger\":true}}";
        const string boxOnly = "{\"Values\":[1,2],\"$collider2D\":{\"size\":{\"x\":2,\"y\":3}}}";
        const string modJson = "{\"Values\":[9,4]}";
        var source = new GameObject("烘焙参数原型");
        var firstObject = new GameObject("烘焙参数实例一");
        var secondObject = new GameObject("烘焙参数实例二");
        var replacement = new GameObject("MOD 替换原型");
        source.SetActive(false);
        firstObject.SetActive(false);
        secondObject.SetActive(false);
        replacement.SetActive(false);
        try
        {
            var prototype = source.AddComponent<GameplayTestPreparedParameterModule>();
            BoxCollider2D sourceCollider = source.AddComponent<BoxCollider2D>();
            var first = firstObject.AddComponent<GameplayTestPreparedParameterModule>();
            var second = secondObject.AddComponent<GameplayTestPreparedParameterModule>();
            BoxCollider2D firstCollider = firstObject.AddComponent<BoxCollider2D>();
            secondObject.AddComponent<BoxCollider2D>();
            var circlePrototype = replacement.AddComponent<GameplayTestPreparedParameterModule>();
            CircleCollider2D circle = replacement.AddComponent<CircleCollider2D>();

            byte[] archive = RecordBakedDefinitionProbeArchive(() =>
            {
                ModuleJsonConfigurator.Bake(prototype, "baked-item", moduleName, moduleId, json);
                ModuleJsonConfigurator.Bake(prototype, "box-only-item", moduleName, moduleId, boxOnly);
            });
            bool originalUnchanged = prototype.Values.Count == 0 && source.transform.localPosition == Vector3.zero &&
                                     !sourceCollider.isTrigger;
            var result = new JObject { ["prototypeUnchanged"] = originalUnchanged };
            using (BuiltInContentBinaryCatalog.UseArchive(archive))
            {
                int reads = BuiltInContentBinaryCatalog.ReadCount;
                int validations = prototype.ValidationCalls;
                ModuleJsonConfigurator.PreparedParameters prepared =
                    ModuleJsonConfigurator.Prepare(prototype, "baked-item", moduleName, moduleId, json);
                result["moduleHit"] = BuiltInContentBinaryCatalog.ReadCount == reads + 1;
                result["validatorRan"] = prototype.ValidationCalls == validations + 1;
                prepared.Apply(first);
                prepared.Apply(second);
                first.Values[0] = 99;
                result["independentValues"] = second.Values[0] == 1 && prototype.Values.Count == 0;
                result["specialApplied"] = firstObject.transform.localPosition == new Vector3(1, 2, 3) && firstCollider.isTrigger;
                prepared.Apply(first);
                result["reapplyRestored"] = first.Values[0] == 1;

                reads = BuiltInContentBinaryCatalog.ReadCount;
                ModuleJsonConfigurator.PreparedParameters mod =
                    ModuleJsonConfigurator.Prepare(prototype, "baked-item", moduleName, moduleId, modJson);
                mod.Apply(first);
                result["modFallback"] = BuiltInContentBinaryCatalog.ReadCount == reads && first.Values[0] == 9;

                int validationsBeforeMissing = prototype.ValidationCalls;
                bool missingBuiltInRejected = false;
                try { ModuleJsonConfigurator.Prepare(prototype, "baked-item", moduleName, moduleId, modJson, true); }
                catch (InvalidOperationException) { missingBuiltInRejected = true; }
                result["missingBuiltInRejected"] = missingBuiltInRejected &&
                    BuiltInContentBinaryCatalog.ReadCount == reads && prototype.ValidationCalls == validationsBeforeMissing;

                reads = BuiltInContentBinaryCatalog.ReadCount;
                ModuleJsonConfigurator.PreparedParameters changedPrefab =
                    ModuleJsonConfigurator.Prepare(circlePrototype, "baked-item", moduleName, moduleId, json);
                changedPrefab.Apply(circlePrototype);
                result["replacementFallback"] = BuiltInContentBinaryCatalog.ReadCount == reads + 1 && circle.isTrigger;
                reads = BuiltInContentBinaryCatalog.ReadCount;
                bool builtInColliderRejected = false;
                try { ModuleJsonConfigurator.Prepare(circlePrototype, "baked-item", moduleName, moduleId, json, true); }
                catch (InvalidOperationException) { builtInColliderRejected = true; }
                result["builtInColliderRejected"] = builtInColliderRejected && BuiltInContentBinaryCatalog.ReadCount == reads + 1;
                reads = BuiltInContentBinaryCatalog.ReadCount;
                bool unsupportedRejected = false;
                try { ModuleJsonConfigurator.Prepare(circlePrototype, "box-only-item", moduleName, moduleId, boxOnly); }
                catch (InvalidOperationException) { unsupportedRejected = true; }
                result["replacementInvalidRejected"] = unsupportedRejected && BuiltInContentBinaryCatalog.ReadCount == reads + 1;

                reads = BuiltInContentBinaryCatalog.ReadCount;
                using (BuiltInContentBinaryCatalog.UseSourceConfigurations())
                    ModuleJsonConfigurator.Prepare(prototype, "baked-item", moduleName, moduleId, modJson, true);
                result["sourceReloadFallback"] = BuiltInContentBinaryCatalog.ReadCount == reads && BuiltInContentBinaryCatalog.IsActive;
            }
            return result;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(firstObject);
            UnityEngine.Object.DestroyImmediate(secondObject);
            UnityEngine.Object.DestroyImmediate(replacement);
        }
    }

    private static JObject ProbeBakedBehaviorTree(JObject args)
    {
        const string actionName = "baked-probe.action", subtreeName = "baked-probe.subtree";
        var result = new JObject();
        var resources = new ResourceReloadContext();
        AnimalBehaviorTreeNodeCatalog.ConfigureResourceReload(resources);
        resources.InCandidate(() =>
        {
            // 先按正式运行路径初始化节点，再临时隔离测试工厂。
            using (BuiltInContentBinaryCatalog.UseSourceConfigurations())
                AnimalBehaviorTreeNodeCatalog.Compile(new AnimalBehaviorTreeDefinition
                { Root = new AnimalBehaviorTreeNodeDefinition { Type = "condition", Name = "always" } });
            Action restoreFactories = AnimalBehaviorTreeRegistry.BeginIsolatedFactoryRegistrations();
            try
            {
                int originalFactoryCalls = 0, replacementFactoryCalls = 0;
                var observed = new List<int>();
                AnimalBehaviorTreeRegistry.RegisterAction(actionName, (_, parameters) =>
                {
                    originalFactoryCalls++;
                    return new BakedBehaviorTreeProbeNode(parameters.Value<int>("value"), observed);
                });
                var shared = new JObject
                {
                    [subtreeName] = new JObject
                    {
                        ["parameters"] = new JObject { ["value"] = 17 },
                        ["root"] = new JObject
                        {
                            ["type"] = "action", ["name"] = actionName, ["id"] = "$id",
                            ["parameters"] = new JObject { ["value"] = "$value" }
                        }
                    }
                };
                var definition = new AnimalBehaviorTreeDefinition
                {
                    Root = new AnimalBehaviorTreeNodeDefinition
                    { Type = "subtree", Name = subtreeName, Parameters = new JObject { ["id"] = "alpha" } }
                };
                AnimalBehaviorTreeNodeCatalog.SetSharedSubtrees(shared);
                string originalDefinition = JsonConvert.SerializeObject(definition);
                string originalShared = shared.ToString(Formatting.None);
                byte[] archive = RecordBakedDefinitionProbeArchive(() => AnimalBehaviorTreeNodeCatalog.Bake(definition));
                result["bakeDidNotRunFactory"] = originalFactoryCalls == 0;
                result["sourceUnchanged"] = originalDefinition == JsonConvert.SerializeObject(definition) &&
                                            originalShared == shared.ToString(Formatting.None);
                AnimalBehaviorTreeRegistry.RegisterAction(actionName, (_, parameters) =>
                {
                    replacementFactoryCalls++;
                    return new BakedBehaviorTreeProbeNode(parameters.Value<int>("value"), observed);
                });
                using (BuiltInContentBinaryCatalog.UseArchive(archive))
                {
                    int reads = BuiltInContentBinaryCatalog.ReadCount;
                    AnimalBehaviorTreeTemplate template = AnimalBehaviorTreeNodeCatalog.Compile(definition);
                    result["treeHit"] = BuiltInContentBinaryCatalog.ReadCount == reads + 1;
                    AnimalBehaviorTreeRuntime first = template.CreateRuntime();
                    AnimalBehaviorTreeRuntime second = template.CreateRuntime();
                    first.Tick(0f);
                    first.Tick(0f);
                    second.Tick(0f);
                    result["factoryRebound"] = originalFactoryCalls == 0 && replacementFactoryCalls == 2;
                    result["independentRuntime"] = observed.Count == 3 && observed[0] == 18 && observed[1] == 19 && observed[2] == 18;
                    result["actionId"] = first.CurrentAction;
                    first.Abort();
                    second.Abort();
                    result["abortCleared"] = first.CurrentAction == null && second.CurrentAction == null;

                    shared[subtreeName]["parameters"]["value"] = 29;
                    AnimalBehaviorTreeNodeCatalog.SetSharedSubtrees(shared);
                    reads = BuiltInContentBinaryCatalog.ReadCount;
                    AnimalBehaviorTreeRuntime changed = AnimalBehaviorTreeNodeCatalog.Compile(definition).CreateRuntime();
                    changed.Tick(0f);
                    result["sharedChangeFallback"] = BuiltInContentBinaryCatalog.ReadCount == reads && observed[observed.Count - 1] == 30;
                    changed.Abort();

                    shared[subtreeName]["root"]["name"] = "baked-probe.unregistered";
                    AnimalBehaviorTreeNodeCatalog.SetSharedSubtrees(shared);
                    bool invalidRejected = false;
                    try { AnimalBehaviorTreeNodeCatalog.Compile(definition); }
                    catch (InvalidDataException) { invalidRejected = true; }
                    shared[subtreeName]["root"]["name"] = actionName;
                    AnimalBehaviorTreeNodeCatalog.SetSharedSubtrees(shared);
                    AnimalBehaviorTreeRuntime recovered = AnimalBehaviorTreeNodeCatalog.Compile(definition).CreateRuntime();
                    recovered.Tick(0f);
                    result["failedTreeRecovered"] = invalidRejected && observed[observed.Count - 1] == 30;
                    recovered.Abort();
                }
                ProbeBehaviorTreeHostRollback(result);
            }
            finally { restoreFactories(); }
        });
        return result;
    }

    private static byte[] RecordBakedDefinitionProbeArchive(Action record)
    {
        BuiltInContentBinaryCatalog.BeginRecording();
        try { record(); return BuiltInContentBinaryCatalog.FinishRecording(); }
        finally { if (BuiltInContentBinaryCatalog.IsRecording) BuiltInContentBinaryCatalog.CancelRecording(); }
    }

    private static void ProbeBehaviorTreeHostRollback(JObject result)
    {
        const string action = "baked-probe.host-action", failingAction = "baked-probe.host-failure";
        AnimalBehaviorTreeRegistry.RegisterAction(action, (context, _) =>
        {
            context.RequireMinimumDetectionRadius(90f);
            return new BakedBehaviorTreeProbeNode(0, new List<int>());
        });
        AnimalBehaviorTreeRegistry.RegisterAction(failingAction, (context, _) =>
        {
            context.RequireMinimumDetectionRadius(95f);
            throw new InvalidOperationException("模拟节点工厂失败。");
        });
        var root = new GameObject("行为树回滚夹具");
        root.SetActive(false);
        Mod_AnimalBehaviorTree tree = null;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var load = typeof(Mod_AnimalBehaviorTree).GetMethod("OnLoad", flags);
        var unload = typeof(Mod_AnimalBehaviorTree).GetMethod("OnUnload", flags);
        var runtime = typeof(Mod_AnimalBehaviorTree).GetField("runtime", flags);
        try
        {
            var actor = root.AddComponent<GameItem>();
            var detector = root.AddComponent<Mod_ItemDetector>();
            detector.ModData = new Ex_ModData_MemoryPackable { StableName = "detector", ModuleId = ModText.Detector };
            detector.DetectionRadius = 14f;
            actor.itemMods.AddMod(detector);
            var first = root.AddComponent<GameplayTestFailingTemperatureMovement>();
            var second = root.AddComponent<GameplayTestFailingTemperatureMovement>();
            first._Data.StableName = "temperature-one";
            first._Data.ModuleId = "temperature-one";
            second._Data.StableName = "temperature-two";
            second._Data.ModuleId = "temperature-two";
            actor.itemMods.AddMod(first);
            actor.itemMods.AddMod(second);
            tree = root.AddComponent<Mod_AnimalBehaviorTree>();
            tree.item = actor;
            tree.BehaviorTree = new AnimalBehaviorTreeDefinition
            { Root = new AnimalBehaviorTreeNodeDefinition { Type = "action", Name = action } };
            using (BuiltInContentBinaryCatalog.UseSourceConfigurations()) load.Invoke(tree, null);
            bool started = tree.Context != null && detector.DetectionRadius == 90f;

            tree.BehaviorTree = new AnimalBehaviorTreeDefinition
            { Root = new AnimalBehaviorTreeNodeDefinition { Type = "action", Name = failingAction } };
            bool factoryFailed = false;
            try { using (BuiltInContentBinaryCatalog.UseSourceConfigurations()) tree.OnResourcesReloaded(); }
            catch (InvalidOperationException) { factoryFailed = true; }
            result["hostFactoryRollback"] = started && factoryFailed && tree.Context == null &&
                runtime.GetValue(tree) == null && detector.DetectionRadius == 14f;

            tree.BehaviorTree = new AnimalBehaviorTreeDefinition
            { Root = new AnimalBehaviorTreeNodeDefinition { Type = "action", Name = action } };
            first.FailPriority = second.FailPriority = true;
            int previousClears = first.ClearCalls + second.ClearCalls;
            tree.SetTemperatureSafetyDestination(Vector2.one);
            bool initializationFailed = false;
            try { using (BuiltInContentBinaryCatalog.UseSourceConfigurations()) load.Invoke(tree, null); }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is InvalidOperationException)
            { initializationFailed = true; }
            result["hostInitializationRollback"] = initializationFailed && tree.Context == null &&
                runtime.GetValue(tree) == null && detector.DetectionRadius == 14f &&
                !tree.HasTemperatureSafetyRequest && first.ClearCalls + second.ClearCalls > previousClears;
        }
        finally
        {
            try { if (tree != null && tree.Context != null) unload.Invoke(tree, null); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    private static JObject ProbeAnimalDetectionRadiusContributions(JObject args)
    {
        var root = new GameObject("检测范围来源夹具");
        root.SetActive(false);
        AnimalBehaviorTreeContext high = null, low = null, failed = null;
        try
        {
            Item actor = root.AddComponent<GameItem>();
            Mod_ItemDetector detector = root.AddComponent<Mod_ItemDetector>();
            detector.DetectionRadius = 10f;
            AnimalBehaviorTreeContext CreateContext() => new(actor, null, detector, null, null, null,
                0f, 1f, null, 0f, 0f, Vector3.zero);
            high = CreateContext();
            high.GetOrCreateCapability("threat", _ => new AnimalPerceivedThreatCapability(high,
                new JObject { ["radius"] = 30f }, null));
            float threat = detector.DetectionRadius;
            high.GetOrCreateCapability("hunting", _ => new AnimalHuntingCapability(high,
                new JObject { ["hungryRange"] = 40f, ["lossRange"] = 50f },
                new JObject { ["hungerRemaining"] = 0f }));
            float hunting = detector.DetectionRadius;
            high.GetOrCreateCapability("territory", _ => new AnimalTerritorialHatredCapability(high,
                new JObject { ["presenceRadius"] = 60f, ["chaseDistance"] = 65f }, null));
            float territory = detector.DetectionRadius;
            high.GetOrCreateCapability("pack", _ => new AnimalPackCapability(high,
                new JObject { ["chaseDistance"] = 80f, ["avoidSafeDistance"] = 70f }, null));
            float pack = detector.DetectionRadius;

            low = CreateContext();
            low.GetOrCreateCapability("threat", _ => new AnimalPerceivedThreatCapability(low,
                new JObject { ["radius"] = 30f }, null));
            var otherSource = new object();
            detector.SetMinimumDetectionRadiusForSource(otherSource, 45f);
            long staleRequest = detector.RequestedVersion;
            long appliedBefore = detector.AppliedVersion;
            high.Dispose();
            high = null;
            float afterHighRemoved = detector.DetectionRadius;
            detector.ApplyDetectorResults(staleRequest, null);
            bool staleRejected = detector.RequestedVersion > staleRequest && detector.AppliedVersion == appliedBefore;

            detector.SetMinimumDetectionRadiusForSource(otherSource, 20f);
            float afterSourceReduced = detector.DetectionRadius;
            long unchangedVersion = detector.RequestedVersion;
            detector.SetMinimumDetectionRadiusForSource(otherSource, 20f);
            bool unchangedNotQueued = detector.RequestedVersion == unchangedVersion;
            detector.RemoveMinimumDetectionRadiusForSource(otherSource);
            float afterOtherRemoved = detector.DetectionRadius;
            detector.DetectionRadius = 12f;
            detector.detectionRadius = 14f;
            detector.OnResourcesReloaded();
            float whileLowRemains = detector.DetectionRadius;
            low.Dispose();
            low = null;
            float afterAllRemoved = detector.DetectionRadius;

            detector.DetectionRadius = 100f;
            detector.SetMinimumDetectionRadiusForSource(otherSource, 50f);
            long beforeOverlappingReload = detector.RequestedVersion;
            detector.detectionRadius = 80f;
            detector.SetMinimumDetectionRadiusForSource(otherSource, 40f);
            bool overlappingReloadInvalidated = detector.DetectionRadius == 80f &&
                                                detector.RequestedVersion == beforeOverlappingReload + 1;
            detector.OnResourcesReloaded();
            overlappingReloadInvalidated &= detector.RequestedVersion == beforeOverlappingReload + 1;
            detector.RemoveMinimumDetectionRadiusForSource(otherSource);
            detector.DetectionRadius = 14f;

            failed = CreateContext();
            bool factoryFailed = false;
            try
            {
                failed.GetOrCreateCapability<AnimalPerceivedThreatCapability>("failed", _ =>
                {
                    new AnimalPerceivedThreatCapability(failed, new JObject { ["radius"] = 90f }, null);
                    throw new InvalidOperationException("模拟节点能力工厂失败。");
                });
            }
            catch (InvalidOperationException) { factoryFailed = true; }
            float duringFailure = detector.DetectionRadius;
            failed.Dispose();
            failed = null;
            return new JObject
            {
                ["threat"] = threat, ["hunting"] = hunting, ["territory"] = territory, ["pack"] = pack,
                ["afterHighRemoved"] = afterHighRemoved, ["afterSourceReduced"] = afterSourceReduced,
                ["afterOtherRemoved"] = afterOtherRemoved, ["whileLowRemains"] = whileLowRemains,
                ["afterAllRemoved"] = afterAllRemoved, ["baseValue"] = detector.detectionRadius,
                ["staleRejected"] = staleRejected, ["unchangedNotQueued"] = unchangedNotQueued,
                ["overlappingReloadInvalidated"] = overlappingReloadInvalidated,
                ["factoryFailed"] = factoryFailed, ["duringFailure"] = duringFailure,
                ["afterFailure"] = detector.DetectionRadius
            };
        }
        finally
        {
            high?.Dispose();
            low?.Dispose();
            failed?.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private sealed class BakedBehaviorTreeProbeNode : AnimalBehaviorTreeNode
    {
        private readonly int value;
        private readonly List<int> observed;
        private int ticks;

        public BakedBehaviorTreeProbeNode(int value, List<int> observed)
        { this.value = value; this.observed = observed; }

        protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
        {
            observed.Add(value + ++ticks);
            return AnimalBehaviorTreeStatus.Running;
        }
    }

    #endregion
}

internal sealed class GameplayTestFailingTemperatureMovement : Module, ITemperatureSafetyMovement, IActorDebugOverlayOwner
{
    #region 行为树初始化失败夹具
    public bool FailPriority;
    public int ClearCalls;
    public override ModuleData _Data { get; set; } = new Ex_ModData();
    public int TemperatureSafetyMovementPriority => FailPriority ? throw new InvalidOperationException("模拟移动依赖失败。") : 0;
    public bool ShouldAdvanceTemperatureSafetyDestination => true;
    public void SetTemperatureSafetyDestination(Vector2 destination) { }
    public void ClearTemperatureSafetyDestination() => ClearCalls++;
    protected override void OnLoad() { }
    protected override void OnSave() { }
    #endregion
}
