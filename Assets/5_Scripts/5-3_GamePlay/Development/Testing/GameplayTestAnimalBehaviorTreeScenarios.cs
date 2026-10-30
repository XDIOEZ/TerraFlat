using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>为 Lua 和编辑器外验证提供相同的正式行为树采样。</summary>
internal static class GameplayTestAnimalBehaviorTreeScenarios
{
    #region 生产树动作采样

    private sealed class Environment
    {
        public readonly List<string> Events = new();
        public bool Gate;
    }

    private sealed class RecordingAction : AnimalBehaviorTreeNode
    {
        private readonly Environment environment;
        private readonly string label;
        private readonly int completeAfter;
        private readonly AnimalBehaviorTreeStatus result;
        private readonly bool recordAbort;
        private int ticks;

        public RecordingAction(Environment environment, JObject parameters)
        {
            this.environment = environment ?? throw new InvalidOperationException("行为树采样缺少实例环境。");
            label = parameters.Value<string>("label");
            completeAfter = parameters.Value<int?>("completeAfter") ?? 0;
            recordAbort = parameters.Value<bool?>("recordAbort") == true;
            result = parameters.Value<bool?>("fail") == true
                ? AnimalBehaviorTreeStatus.Failure : AnimalBehaviorTreeStatus.Success;
        }

        protected override void OnEnter()
        {
            ticks = 0;
            environment.Events.Add(label + ".enter");
        }

        protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
        {
            environment.Events.Add(label + ".tick");
            ticks++;
            return completeAfter > 0 && ticks >= completeAfter ? result : AnimalBehaviorTreeStatus.Running;
        }

        protected override void OnAbort()
        {
            if (recordAbort) environment.Events.Add(label + ".abort");
        }
        protected override void OnExit() => environment.Events.Add(label + ".exit");
    }

    private static bool registered;
    private static Environment buildingEnvironment;

    private static void EnsureRegistered()
    {
        if (registered) return;
        foreach (string name in new[] { "test_low", "test_high", "test_once", "test_waiting", "test_last", "test_param", "test_service" })
            AnimalBehaviorTreeRegistry.RegisterAction(name, (_, parameters) => new RecordingAction(buildingEnvironment, parameters));
        AnimalBehaviorTreeRegistry.RegisterCondition("test_gate", (_, _) =>
        {
            Environment environment = buildingEnvironment ?? throw new InvalidOperationException("行为树条件缺少实例环境。");
            return () => environment.Gate;
        });
        registered = true;
    }

    private static AnimalBehaviorTreeRuntime Create(AnimalBehaviorTreeTemplate template, Environment environment)
    {
        buildingEnvironment = environment;
        try { return template.CreateRuntime(null); }
        finally { buildingEnvironment = null; }
    }

    private static AnimalBehaviorTreeTemplate Compile(JObject root, JObject subtrees = null)
    {
        EnsureRegistered();
        var json = new JObject { ["schemaVersion"] = 1, ["root"] = root };
        if (subtrees != null) json["subtrees"] = subtrees;
        return AnimalBehaviorTreeCompiler.Compile(json.ToObject<AnimalBehaviorTreeDefinition>());
    }

    private static JObject Group(string type, params JObject[] children) => new()
    {
        ["type"] = type, ["children"] = new JArray(children)
    };

    private static JObject Action(string name, string label, int completeAfter = 0, bool fail = false, bool recordAbort = false) => new()
    {
        ["type"] = "action", ["name"] = name,
        ["parameters"] = new JObject { ["label"] = label, ["completeAfter"] = completeAfter,
            ["fail"] = fail, ["recordAbort"] = recordAbort }
    };

    private static JObject Condition() => new() { ["type"] = "condition", ["name"] = "test_gate" };

    private static JObject Subtree(string name, string label = null)
    {
        var node = new JObject { ["type"] = "subtree", ["name"] = name };
        if (label != null) node["parameters"] = new JObject { ["label"] = label };
        return node;
    }

    private static JObject SharedDefinitions() => new()
    {
        ["outer"] = new JObject
        {
            ["parameters"] = new JObject { ["label"] = "default" },
            ["root"] = Subtree("inner", "$label")
        },
        ["inner"] = new JObject
        {
            ["parameters"] = new JObject { ["label"] = "inner-default" },
            ["root"] = new JObject
            {
                ["type"] = "action", ["name"] = "test_param", ["id"] = "$label",
                ["parameters"] = new JObject { ["label"] = "$label", ["completeAfter"] = 2 }
            }
        }
    };

    #endregion

    #region 树执行与配置采样

    internal static JObject Transitions(JObject args)
    {
        var environment = new Environment();
        AnimalBehaviorTreeRuntime runtime = Create(Compile(Group("selector",
            Group("sequence", Condition(), Action("test_high", "chase")),
            Action("test_low", "idle"))), environment);
        runtime.Tick(.1f);
        environment.Gate = args?.Value<bool?>("blocked") != true;
        runtime.Tick(.1f);
        string selected = runtime.CurrentAction;
        runtime.Abort();
        runtime.Abort();
        string afterAbort = runtime.CurrentAction;
        environment.Gate = false;
        runtime.Tick(.1f);
        string afterRestart = runtime.CurrentAction;
        runtime.Abort();
        return new JObject
        {
            ["events"] = new JArray(environment.Events), ["selected"] = selected ?? string.Empty,
            ["afterAbort"] = afterAbort ?? string.Empty, ["afterRestart"] = afterRestart ?? string.Empty
        };
    }

    internal static JObject Priority(JObject ignored)
    {
        var environment = new Environment();
        AnimalBehaviorTreeRuntime runtime = Create(Compile(Group("selector",
            Group("sequence", Condition(), Action("test_high", "high")),
            Action("test_low", "low"))), environment);
        var actionEvents = new List<string>();
        runtime.ActionChanged += (previous, next) => actionEvents.Add((previous ?? string.Empty) + "->" + (next ?? string.Empty));
        string initialStatus = runtime.Tick(.1f).ToString(), initialAction = runtime.CurrentAction;
        environment.Gate = true;
        string threatStatus = runtime.Tick(.1f).ToString(), threatAction = runtime.CurrentAction;
        environment.Gate = false;
        string clearedStatus = runtime.Tick(.1f).ToString(), clearedAction = runtime.CurrentAction;
        runtime.Abort();
        runtime.Abort();
        return new JObject
        {
            ["initialStatus"] = initialStatus, ["initialAction"] = initialAction ?? string.Empty,
            ["threatStatus"] = threatStatus, ["threatAction"] = threatAction ?? string.Empty,
            ["clearedStatus"] = clearedStatus, ["clearedAction"] = clearedAction ?? string.Empty,
            ["events"] = new JArray(environment.Events), ["actionEvents"] = new JArray(actionEvents),
            ["afterAbortAction"] = runtime.CurrentAction ?? string.Empty
        };
    }

    internal static JObject Sequence(JObject ignored)
    {
        var completion = new Environment();
        AnimalBehaviorTreeRuntime runtime = Create(Compile(Group("sequence",
            Action("test_once", "once", 1), Action("test_waiting", "waiting", 2),
            Action("test_last", "last", 1))), completion);
        string firstStatus = runtime.Tick(.1f).ToString(), firstAction = runtime.CurrentAction;
        string completedStatus = runtime.Tick(.1f).ToString(), completedAction = runtime.CurrentAction;
        runtime.Abort();

        var invalidation = new Environment { Gate = true };
        AnimalBehaviorTreeRuntime guarded = Create(Compile(Group("sequence", Condition(),
            Action("test_waiting", "waiting"))), invalidation);
        guarded.Tick(.1f);
        invalidation.Gate = false;
        string invalidatedStatus = guarded.Tick(.1f).ToString(), invalidatedAction = guarded.CurrentAction;
        guarded.Abort();
        return new JObject
        {
            ["firstStatus"] = firstStatus, ["firstAction"] = firstAction ?? string.Empty,
            ["completedStatus"] = completedStatus, ["completedAction"] = completedAction ?? string.Empty,
            ["completionEvents"] = new JArray(completion.Events),
            ["invalidatedStatus"] = invalidatedStatus, ["invalidatedAction"] = invalidatedAction ?? string.Empty,
            ["invalidationEvents"] = new JArray(invalidation.Events)
        };
    }

    internal static JObject Subtrees(JObject ignored)
    {
        var root = Group("sequence", Subtree("outer", "alpha"), Subtree("outer", "beta"));
        var json = new JObject { ["schemaVersion"] = 1, ["root"] = root, ["subtrees"] = SharedDefinitions() };
        string original = json.ToString(Formatting.None);
        EnsureRegistered();
        AnimalBehaviorTreeDefinition definition = json.ToObject<AnimalBehaviorTreeDefinition>();
        string definitionBefore = JsonConvert.SerializeObject(definition);
        AnimalBehaviorTreeTemplate template = AnimalBehaviorTreeCompiler.Compile(definition);
        var first = new Environment();
        var second = new Environment();
        AnimalBehaviorTreeRuntime firstRuntime = Create(template, first), secondRuntime = Create(template, second);
        firstRuntime.Tick(.1f); secondRuntime.Tick(.1f);
        string firstInitialAction = firstRuntime.CurrentAction, secondInitialAction = secondRuntime.CurrentAction;
        firstRuntime.Tick(.1f); secondRuntime.Tick(.1f);
        string firstNextAction = firstRuntime.CurrentAction, secondNextAction = secondRuntime.CurrentAction;
        string firstStatus = firstRuntime.Tick(.1f).ToString(), secondStatus = secondRuntime.Tick(.1f).ToString();
        bool sourceUnchanged = original == json.ToString(Formatting.None) &&
            definitionBefore == JsonConvert.SerializeObject(definition);
        firstRuntime.Abort(); secondRuntime.Abort();

        var defaultEnvironment = new Environment();
        var overrideEnvironment = new Environment();
        AnimalBehaviorTreeRuntime defaultRuntime = Create(Compile(Subtree("outer"), SharedDefinitions()), defaultEnvironment);
        AnimalBehaviorTreeRuntime overrideRuntime = Create(Compile(Subtree("outer", "override"), SharedDefinitions()), overrideEnvironment);
        defaultRuntime.Tick(.1f); overrideRuntime.Tick(.1f);
        string defaultParameter = defaultEnvironment.Events[0].Split('.')[0];
        string overrideParameter = overrideEnvironment.Events[0].Split('.')[0];
        string defaultAction = defaultRuntime.CurrentAction, overrideAction = overrideRuntime.CurrentAction;
        defaultRuntime.Abort(); overrideRuntime.Abort();
        return new JObject
        {
            ["firstEvents"] = new JArray(first.Events), ["secondEvents"] = new JArray(second.Events),
            ["firstStatus"] = firstStatus, ["secondStatus"] = secondStatus,
            ["sourceUnchanged"] = sourceUnchanged, ["defaultParameter"] = defaultParameter,
            ["overrideParameter"] = overrideParameter,
            ["firstInitialAction"] = firstInitialAction, ["secondInitialAction"] = secondInitialAction,
            ["firstNextAction"] = firstNextAction, ["secondNextAction"] = secondNextAction,
            ["defaultAction"] = defaultAction, ["overrideAction"] = overrideAction
        };
    }

    internal static JObject Validation(JObject ignored)
    {
        bool Rejected(JObject root, JObject subtrees = null)
        {
            try { Compile(root, subtrees); return false; }
            catch (Exception) { return true; }
        }

        var cycle = new JObject
        {
            ["a"] = new JObject { ["root"] = Subtree("b") },
            ["b"] = new JObject { ["root"] = Subtree("a") }
        };
        var success = new Environment();
        var failure = new Environment();
        AnimalBehaviorTreeRuntime invertedSuccess = Create(Compile(Group("inverter", Action("test_once", "success", 1))), success);
        AnimalBehaviorTreeRuntime invertedFailure = Create(Compile(Group("inverter", Action("test_once", "failure", 1, true))), failure);
        string successStatus = invertedSuccess.Tick(.1f).ToString();
        string failureStatus = invertedFailure.Tick(.1f).ToString();
        invertedSuccess.Abort(); invertedFailure.Abort();
        return new JObject
        {
            ["unknownActionRejected"] = Rejected(Action("test_missing_action", "missing")),
            ["unknownConditionRejected"] = Rejected(new JObject { ["type"] = "condition", ["name"] = "test_missing_condition" }),
            ["missingSubtreeRejected"] = Rejected(Subtree("missing")),
            ["cycleRejected"] = Rejected(Subtree("a"), cycle),
            ["badChildrenRejected"] = Rejected(Group("inverter")),
            ["invertedSuccess"] = successStatus, ["invertedFailure"] = failureStatus
        };
    }

    internal static JObject Service(JObject ignored)
    {
        var aborted = new Environment();
        AnimalBehaviorTreeRuntime runtime = Create(Compile(Group("service",
            Action("test_service", "service", 1, recordAbort: true),
            Action("test_waiting", "main", recordAbort: true))), aborted);
        var actionEvents = new List<string>();
        runtime.ActionChanged += (previous, next) => actionEvents.Add((previous ?? string.Empty) + "->" + (next ?? string.Empty));
        string firstStatus = runtime.Tick(.1f).ToString(), firstAction = runtime.CurrentAction;
        string secondStatus = runtime.Tick(.1f).ToString(), secondAction = runtime.CurrentAction;
        runtime.Abort();
        runtime.Abort();

        var completed = new Environment();
        AnimalBehaviorTreeRuntime completing = Create(Compile(Group("service",
            Action("test_service", "service", recordAbort: true),
            Action("test_once", "main", 1, recordAbort: true))), completed);
        string completedStatus = completing.Tick(.1f).ToString();
        completing.Abort();
        return new JObject
        {
            ["firstStatus"] = firstStatus, ["firstAction"] = firstAction ?? string.Empty,
            ["secondStatus"] = secondStatus, ["secondAction"] = secondAction ?? string.Empty,
            ["actionEvents"] = new JArray(actionEvents), ["abortedEvents"] = new JArray(aborted.Events),
            ["completedStatus"] = completedStatus, ["completedEvents"] = new JArray(completed.Events)
        };
    }

    #endregion
}
