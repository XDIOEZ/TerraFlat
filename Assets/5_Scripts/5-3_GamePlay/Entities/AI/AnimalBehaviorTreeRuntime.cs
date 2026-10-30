using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#region 行为树数据与节点生命周期

public enum AnimalBehaviorTreeStatus { Success, Failure, Running }

/// <summary>节点运行态只属于一只生物，结束和抢占共用唯一退出入口。</summary>
public abstract class AnimalBehaviorTreeNode
{
    private bool running;
    public virtual bool CanRun => true;
    internal virtual bool IsGuard => false;

    public AnimalBehaviorTreeStatus Tick(float deltaTime)
    {
        if (!running)
        {
            running = true;
            OnEnter();
        }
        AnimalBehaviorTreeStatus status = OnTick(deltaTime);
        if (status != AnimalBehaviorTreeStatus.Running) Stop(false);
        return status;
    }

    public void Abort()
    {
        Stop(true);
    }

    private void Stop(bool aborted)
    {
        if (!running) return;
        running = false;
        if (aborted) OnAbort();
        OnExit();
    }

    protected virtual void OnEnter() { }
    protected abstract AnimalBehaviorTreeStatus OnTick(float deltaTime);
    protected virtual void OnAbort() { }
    protected virtual void OnExit() { }
}

[Serializable]
public sealed class AnimalBehaviorTreeDefinition
{
    [JsonProperty("schemaVersion")] public int SchemaVersion = 1;
    [JsonProperty("capabilities")] public JObject Capabilities = new();
    [JsonProperty("root")] public AnimalBehaviorTreeNodeDefinition Root;
    [JsonProperty("subtrees")] public Dictionary<string, AnimalBehaviorSubtreeDefinition> Subtrees = new(StringComparer.Ordinal);
}

[Serializable]
public sealed class AnimalBehaviorTreeNodeDefinition
{
    [JsonProperty("type")] public string Type;
    [JsonProperty("id")] public string Id;
    [JsonProperty("name")] public string Name;
    [JsonProperty("parameters")] public JObject Parameters = new();
    [JsonProperty("children")] public List<AnimalBehaviorTreeNodeDefinition> Children = new();
}

[Serializable]
public sealed class AnimalBehaviorSubtreeDefinition
{
    [JsonProperty("parameters")] public JObject Parameters = new();
    [JsonProperty("root")] public AnimalBehaviorTreeNodeDefinition Root;
}

#endregion

#region 共用选择、顺序与条件节点

internal sealed class AnimalBehaviorConditionNode : AnimalBehaviorTreeNode
{
    private readonly Func<bool> predicate;
    public AnimalBehaviorConditionNode(Func<bool> predicate) => this.predicate = predicate;
    public override bool CanRun => predicate();
    internal override bool IsGuard => true;
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
        => predicate() ? AnimalBehaviorTreeStatus.Success : AnimalBehaviorTreeStatus.Failure;
}

internal sealed class AnimalBehaviorCompositeNode : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeNode[] children;
    private readonly bool selector;
    private int current;
    private int active = -1;

    public AnimalBehaviorCompositeNode(AnimalBehaviorTreeNode[] children, bool selector)
    {
        this.children = children;
        this.selector = selector;
    }

    internal override bool IsGuard
    {
        get
        {
            for (int i = 0; i < children.Length; i++)
                if (!children[i].IsGuard) return false;
            return true;
        }
    }

    public override bool CanRun
    {
        get
        {
            if (selector)
            {
                for (int i = 0; i < children.Length; i++)
                    if (children[i].CanRun) return true;
                return false;
            }
            // 已完成的条件仍是守卫，已完成的动作不会再次执行。
            for (int i = 0; i < children.Length; i++)
            {
                if (!children[i].IsGuard)
                {
                    if (i >= current) return children[i].CanRun;
                    continue;
                }
                if (!children[i].CanRun) return false;
            }
            return true;
        }
    }

    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        if (!CanRun) return AnimalBehaviorTreeStatus.Failure;
        if (selector)
        {
            for (int i = 0; i < children.Length; i++)
            {
                if (!children[i].CanRun) continue;
                // 先释放旧动作，再进入更高优先分支，避免同帧争用移动和攻击窗口。
                if (active >= 0 && active != i) children[active].Abort();
                active = i;
                AnimalBehaviorTreeStatus status = children[i].Tick(deltaTime);
                if (status == AnimalBehaviorTreeStatus.Failure) { active = -1; continue; }
                return status;
            }
            return AnimalBehaviorTreeStatus.Failure;
        }
        while (current < children.Length)
        {
            active = current;
            AnimalBehaviorTreeStatus status = children[current].Tick(deltaTime);
            if (status != AnimalBehaviorTreeStatus.Success) return status;
            current++;
        }
        return AnimalBehaviorTreeStatus.Success;
    }

    protected override void OnExit()
    {
        if (active >= 0) children[active].Abort();
        active = -1;
        current = 0;
    }
}

internal sealed class AnimalBehaviorInverterNode : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeNode child;
    public AnimalBehaviorInverterNode(AnimalBehaviorTreeNode child) => this.child = child;
    internal override bool IsGuard => child.IsGuard;
    public override bool CanRun => !child.IsGuard || !child.CanRun;
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
        => child.Tick(deltaTime) switch
        {
            AnimalBehaviorTreeStatus.Success => AnimalBehaviorTreeStatus.Failure,
            AnimalBehaviorTreeStatus.Failure => AnimalBehaviorTreeStatus.Success,
            _ => AnimalBehaviorTreeStatus.Running
        };
    protected override void OnExit() => child.Abort();
}

/// <summary>伴随服务只维护能力事实，主子树仍独占动作选择。</summary>
internal sealed class AnimalBehaviorServiceNode : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeNode[] children;
    public AnimalBehaviorServiceNode(AnimalBehaviorTreeNode[] children) => this.children = children;
    public override bool CanRun => children[children.Length - 1].CanRun;
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        for (int i = 0; i < children.Length - 1; i++) children[i].Tick(deltaTime);
        return children[children.Length - 1].Tick(deltaTime);
    }
    protected override void OnExit()
    {
        for (int i = children.Length - 1; i >= 0; i--) children[i].Abort();
    }
}

#endregion

#region 节点注册与初始化编译

/// <summary>通用能力注册一次，物种只在 JSON 中组合节点。</summary>
public static class AnimalBehaviorTreeRegistry
{
    private static readonly Dictionary<string, Func<AnimalBehaviorTreeContext, JObject, AnimalBehaviorTreeNode>> actions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<AnimalBehaviorTreeContext, JObject, Func<bool>>> conditions = new(StringComparer.Ordinal);

    public static void RegisterAction(string name, Func<AnimalBehaviorTreeContext, JObject, AnimalBehaviorTreeNode> factory)
        => actions[name] = factory;
    public static void RegisterCondition(string name, Func<AnimalBehaviorTreeContext, JObject, Func<bool>> factory)
        => conditions[name] = factory;

    internal static Func<AnimalBehaviorTreeContext, AnimalBehaviorTreeNode> CompileAction(string name, JObject parameters)
    {
        if (name == null || !actions.TryGetValue(name, out var factory))
            throw new InvalidDataException($"行为树动作未注册：{name}");
        return context => factory(context, parameters);
    }

    internal static Func<AnimalBehaviorTreeContext, AnimalBehaviorTreeNode> CompileCondition(string name, JObject parameters)
    {
        if (name == null || !conditions.TryGetValue(name, out var factory))
            throw new InvalidDataException($"行为树条件未注册：{name}");
        return context => new AnimalBehaviorConditionNode(factory(context, parameters));
    }
}

/// <summary>展开共享子树并冻结节点参数，创建生物时只绑定实例能力。</summary>
public static class AnimalBehaviorTreeCompiler
{
    public static AnimalBehaviorTreeTemplate Compile(AnimalBehaviorTreeDefinition definition)
    {
        if (definition == null || definition.SchemaVersion != 1 || definition.Root == null)
            throw new InvalidDataException("行为树需要 schemaVersion=1 和 root。");
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        AnimalBehaviorTreeTemplate.CompiledNode root = CompileNode(definition.Root, definition, new JObject(), ancestors);
        return new AnimalBehaviorTreeTemplate(root, (JObject)(definition.Capabilities?.DeepClone() ?? new JObject()));
    }

    private static AnimalBehaviorTreeTemplate.CompiledNode CompileNode(AnimalBehaviorTreeNodeDefinition node,
        AnimalBehaviorTreeDefinition definition, JObject arguments, HashSet<string> ancestors)
    {
        if (node == null) throw new InvalidDataException("行为树包含空节点。");
        JObject parameters = (JObject)ResolveParameters(node.Parameters ?? new JObject(), arguments);
        switch (node.Type)
        {
            case "subtree":
                if (node.Name == null || !definition.Subtrees.TryGetValue(node.Name, out var subtree))
                    throw new InvalidDataException($"行为子树不存在：{node.Name}");
                if (!ancestors.Add(node.Name)) throw new InvalidDataException($"行为子树循环引用：{node.Name}");
                var supplied = (JObject)(subtree.Parameters?.DeepClone() ?? new JObject());
                foreach (JProperty pair in parameters.Properties()) supplied[pair.Name] = pair.Value.DeepClone();
                AnimalBehaviorTreeTemplate.CompiledNode expanded = CompileNode(subtree.Root, definition, supplied, ancestors);
                ancestors.Remove(node.Name);
                return expanded;
            case "condition":
                return new AnimalBehaviorTreeTemplate.CompiledNode(AnimalBehaviorTreeRegistry.CompileCondition(node.Name, parameters));
            case "action":
                string actionId = node.Id == null ? node.Name : ResolveParameters(new JValue(node.Id), arguments).Value<string>();
                return new AnimalBehaviorTreeTemplate.CompiledNode(AnimalBehaviorTreeRegistry.CompileAction(node.Name, parameters), actionId);
            case "selector":
            case "sequence":
            case "service":
                if (node.Children == null || node.Children.Count == 0)
                    throw new InvalidDataException($"行为树 {node.Type} 需要子节点。");
                var children = new AnimalBehaviorTreeTemplate.CompiledNode[node.Children.Count];
                for (int i = 0; i < children.Length; i++) children[i] = CompileNode(node.Children[i], definition, arguments, ancestors);
                return new AnimalBehaviorTreeTemplate.CompiledNode(node.Type, children);
            case "inverter":
                if (node.Children?.Count != 1) throw new InvalidDataException("行为树 inverter 需要一个子节点。");
                return new AnimalBehaviorTreeTemplate.CompiledNode(node.Type,
                    new[] { CompileNode(node.Children[0], definition, arguments, ancestors) });
            default:
                throw new InvalidDataException($"行为树节点类型无效：{node.Type}");
        }
    }

    private static JToken ResolveParameters(JToken token, JObject arguments)
    {
        if (token.Type == JTokenType.String && token.Value<string>() is string text && text.StartsWith("$", StringComparison.Ordinal))
        {
            JToken value = arguments[text.Substring(1)];
            if (value == null) throw new InvalidDataException($"行为子树参数缺失：{text}");
            return value.DeepClone();
        }
        if (token is JObject obj)
        {
            var result = new JObject();
            foreach (JProperty property in obj.Properties()) result.Add(property.Name, ResolveParameters(property.Value, arguments));
            return result;
        }
        if (token is JArray array)
        {
            var result = new JArray();
            foreach (JToken child in array) result.Add(ResolveParameters(child, arguments));
            return result;
        }
        return token.DeepClone();
    }
}

#endregion

#region 模板共享与独立运行实例

public sealed class AnimalBehaviorTreeTemplate
{
    internal sealed class CompiledNode
    {
        private readonly Func<AnimalBehaviorTreeContext, AnimalBehaviorTreeNode> create;
        private readonly string type;
        private readonly string action;
        private readonly CompiledNode[] children;
        internal CompiledNode(Func<AnimalBehaviorTreeContext, AnimalBehaviorTreeNode> create, string action = null)
        { this.create = create; this.action = action; }
        internal CompiledNode(string type, CompiledNode[] children) { this.type = type; this.children = children; }

        internal AnimalBehaviorTreeNode Instantiate(AnimalBehaviorTreeContext context, Action<object, string> entered, Action<object> exited, bool trackAction = true)
        {
            if (create != null)
            {
                AnimalBehaviorTreeNode node = create(context);
                return action == null || !trackAction ? node : new NamedAction(node, action, entered, exited);
            }
            var instances = new AnimalBehaviorTreeNode[children.Length];
            for (int i = 0; i < instances.Length; i++)
                instances[i] = children[i].Instantiate(context, entered, exited,
                    trackAction && (type != "service" || i == instances.Length - 1));
            return type == "inverter" ? new AnimalBehaviorInverterNode(instances[0]) : type == "service" ? new AnimalBehaviorServiceNode(instances) :
                new AnimalBehaviorCompositeNode(instances, type == "selector");
        }
    }

    private sealed class NamedAction : AnimalBehaviorTreeNode
    {
        private readonly AnimalBehaviorTreeNode node;
        private readonly string name;
        private readonly Action<object, string> entered;
        private readonly Action<object> exited;
        internal NamedAction(AnimalBehaviorTreeNode node, string name, Action<object, string> entered, Action<object> exited)
        { this.node = node; this.name = name; this.entered = entered; this.exited = exited; }
        public override bool CanRun => node.CanRun;
        protected override void OnEnter() => entered(this, name);
        protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
        {
            entered(this, name);
            return node.Tick(deltaTime);
        }
        protected override void OnExit() { node.Abort(); exited(this); }
    }

    private readonly CompiledNode root;
    public JObject Capabilities { get; }
    internal AnimalBehaviorTreeTemplate(CompiledNode root, JObject capabilities)
    { this.root = root; Capabilities = capabilities; }
    public AnimalBehaviorTreeRuntime CreateRuntime(AnimalBehaviorTreeContext context = null)
        => new(root, context);
}

public sealed class AnimalBehaviorTreeRuntime
{
    private readonly AnimalBehaviorTreeNode root;
    private object currentActionOwner;
    public string CurrentAction { get; private set; }
    public event Action<string, string> ActionChanged;
    internal AnimalBehaviorTreeRuntime(AnimalBehaviorTreeTemplate.CompiledNode template, AnimalBehaviorTreeContext context)
        => root = template.Instantiate(context, EnterAction, ExitAction);
    public AnimalBehaviorTreeStatus Tick(float deltaTime) => root.Tick(Math.Max(0f, deltaTime));
    public void Abort() { root.Abort(); CurrentAction = null; }
    private void EnterAction(object owner, string action)
    {
        if (ReferenceEquals(currentActionOwner, owner)) return;
        string previous = CurrentAction;
        currentActionOwner = owner;
        CurrentAction = action;
        ActionChanged?.Invoke(previous, action);
    }
    private void ExitAction(object owner)
    {
        if (!ReferenceEquals(currentActionOwner, owner)) return;
        string previous = CurrentAction;
        currentActionOwner = null;
        CurrentAction = null;
        ActionChanged?.Invoke(previous, null);
    }
}

#endregion
