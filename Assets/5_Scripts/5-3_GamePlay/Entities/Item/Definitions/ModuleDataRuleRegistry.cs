using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>纯模块规则的实例上下文，携带宿主身份但不创建表现组件。</summary>
public readonly struct ModuleDataTickContext
{
    public ModuleDataTickContext(ModuleData moduleData, ItemData itemData,
        Inventory_Data inventoryData, ItemSlot slot, int slotIndex, float deltaTime,
        RuntimeItemDefinition definition = null)
    {
        ModuleData = moduleData;
        ItemData = itemData;
        InventoryData = inventoryData;
        Slot = slot;
        SlotIndex = slotIndex;
        DeltaTime = deltaTime;
        Definition = definition;
        Scheduler = null;
        SchedulerGeneration = 0;
        Owner = null;
        MachineOwner = null;
    }

    internal ModuleDataTickContext(ModuleData moduleData, ItemData itemData,
        Inventory_Data inventoryData, ItemSlot slot, int slotIndex, float deltaTime,
        RuntimeItemDefinition definition, InventoryModuleDataScheduler scheduler, uint schedulerGeneration, Item owner = null,
        MachineEntity machineOwner = null)
        : this(moduleData, itemData, inventoryData, slot, slotIndex, deltaTime, definition)
    {
        Scheduler = scheduler;
        SchedulerGeneration = schedulerGeneration;
        Owner = owner;
        MachineOwner = machineOwner;
    }

    public ModuleData ModuleData { get; }
    public ItemData ItemData { get; }
    public Inventory_Data InventoryData { get; }
    public ItemSlot Slot { get; }
    public int SlotIndex { get; }
    public float DeltaTime { get; }
    public RuntimeItemDefinition Definition { get; }
    public Item Owner { get; } // 冷载荷规则读取真实库存携带者，不能猜测当前本地玩家。
    public MachineEntity MachineOwner { get; } // 数据机器库存以真实逻辑节点作为爆炸位置。
    internal InventoryModuleDataScheduler Scheduler { get; }
    internal uint SchedulerGeneration { get; }
}

/// <summary>不依赖 Unity 组件的能力规则，共享规则只把可变状态写回上下文。</summary>
public interface IModuleDataRule
{
    bool CanStep(ModuleDataTickContext context);
    void Step(ModuleDataTickContext context);
}

/// <summary>按当前只读模块定义编译共享规则，禁止把单个实例状态保存在工厂或规则中。</summary>
public interface IModuleDataRuleFactory
{
    IModuleDataRule Create(RuntimeItemModuleDefinition definition);
}

/// <summary>当前定义的不可变规则列表，执行期间注册表变化不会修改正在遍历的列表。</summary>
public sealed class RuntimeModuleDataRulePlan
{
    #region 规则执行

    private readonly string moduleId;
    private readonly IModuleDataRule[] rules;
    private readonly string[] ruleIds;

    internal RuntimeModuleDataRulePlan(string moduleId, IModuleDataRule[] rules, string[] ruleIds)
    {
        this.moduleId = moduleId;
        this.rules = rules;
        this.ruleIds = ruleIds;
    }

    public bool HasRules => rules.Length != 0;

    internal bool Step(ModuleDataTickContext context)
    {
        bool handled = false;
        for (int index = 0; index < rules.Length; index++)
        {
            if (!ModuleDataRuleRegistry.IsCurrent(context))
                break;
            try
            {
                IModuleDataRule rule = rules[index];
                if (!rule.CanStep(context))
                    continue;
                if (!ModuleDataRuleRegistry.IsCurrent(context))
                    break;
                handled = true;
                rule.Step(context);
            }
            catch (Exception exception)
            {
                handled = true;
                Debug.LogError($"[ModuleDataRule] 能力 {moduleId} 的规则 {ruleIds[index]} 执行失败：{exception}");
            }
        }
        return handled;
    }

    #endregion
}

/// <summary>所有纯数据能力共用的规则注册入口，定义缓存随注册变化和资源重载失效。</summary>
public static class ModuleDataRuleRegistry
{
    #region 规则注册

    private sealed class Registration
    {
        public string RuleId;
        public int Priority;
        public IModuleDataRuleFactory Factory;
    }

    private sealed class DelegateFactory : IModuleDataRuleFactory
    {
        private readonly Func<RuntimeItemModuleDefinition, IModuleDataRule> factory;
        public DelegateFactory(Func<RuntimeItemModuleDefinition, IModuleDataRule> factory) => this.factory = factory;
        public IModuleDataRule Create(RuntimeItemModuleDefinition definition) => factory(definition);
    }

    private static readonly Dictionary<string, Registration[]> registrations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string ModuleId, string StableName), RuntimeModuleDataRulePlan> fallbackPlans = new();
    private static readonly Dictionary<Type, bool> dataUpdateOverrides = new();
    public static uint Revision { get; private set; }

    static ModuleDataRuleRegistry()
    {
        Register(ModText.Food, "food.spoilage", _ => new FoodSpoilageModuleDataObserver());
        Register(ModText.Food, "food.melting", _ => new FoodMeltingModuleDataObserver(), 10);
        Register(Mod_FluidTank.ModuleId, "fluid_tank.overpressure", definition => new FluidTankInventoryRule(definition));
        Register(Mod_Spacesuit.ModuleId, "spacesuit.stored_tank", _ => new SpacesuitInventoryRule());
    }

    public static void Register(string moduleId, string ruleId,
        Func<RuntimeItemModuleDefinition, IModuleDataRule> factory, int priority = 0)
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        Register(moduleId, ruleId, new DelegateFactory(factory), priority);
    }

    /// <summary>同一能力允许多个有稳定名字的规则，按优先级和规则名确定执行顺序。</summary>
    public static void Register(string moduleId, string ruleId, IModuleDataRuleFactory factory, int priority = 0)
    {
        moduleId = RequireId(moduleId, nameof(moduleId));
        ruleId = RequireId(ruleId, nameof(ruleId));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        registrations.TryGetValue(moduleId, out Registration[] previous);
        previous ??= Array.Empty<Registration>();
        foreach (Registration registration in previous)
            if (string.Equals(registration.RuleId, ruleId, StringComparison.Ordinal))
                throw new InvalidOperationException($"模块能力 {moduleId} 的规则 {ruleId} 已注册。");
        var updated = new Registration[previous.Length + 1];
        Array.Copy(previous, updated, previous.Length);
        updated[previous.Length] = new Registration { RuleId = ruleId, Priority = priority, Factory = factory };
        Array.Sort(updated, (left, right) =>
        {
            int priorityOrder = left.Priority.CompareTo(right.Priority);
            return priorityOrder != 0 ? priorityOrder : StringComparer.Ordinal.Compare(left.RuleId, right.RuleId);
        });
        registrations[moduleId] = updated;
        InvalidatePlans();
    }

    public static bool Unregister(string moduleId, string ruleId)
    {
        moduleId = RequireId(moduleId, nameof(moduleId));
        ruleId = RequireId(ruleId, nameof(ruleId));
        if (!registrations.TryGetValue(moduleId, out Registration[] previous))
            return false;
        int removed = Array.FindIndex(previous, value => string.Equals(value.RuleId, ruleId, StringComparison.Ordinal));
        if (removed < 0)
            return false;
        var updated = new Registration[previous.Length - 1];
        Array.Copy(previous, 0, updated, 0, removed);
        Array.Copy(previous, removed + 1, updated, removed, previous.Length - removed - 1);
        if (updated.Length == 0)
            registrations.Remove(moduleId);
        else
            registrations[moduleId] = updated;
        InvalidatePlans();
        return true;
    }

    private static string RequireId(string value, string name)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("规则身份不能为空。", name);

    private static void InvalidatePlans()
    {
        Revision++;
        fallbackPlans.Clear();
    }

    #endregion

    #region 定义编译与实例步进

    internal static RuntimeModuleDataRulePlan Compile(RuntimeItemModuleDefinition definition)
    {
        if (!registrations.TryGetValue(definition.ModuleId, out Registration[] source))
            return new RuntimeModuleDataRulePlan(definition.ModuleId, Array.Empty<IModuleDataRule>(), Array.Empty<string>());
        var rules = new List<IModuleDataRule>(source.Length);
        var ruleIds = new List<string>(source.Length);
        foreach (Registration registration in source)
        {
            IModuleDataRule rule = registration.Factory.Create(definition);
            if (rule == null)
                continue;
            rules.Add(rule);
            ruleIds.Add(registration.RuleId);
        }
        return new RuntimeModuleDataRulePlan(definition.ModuleId, rules.ToArray(), ruleIds.ToArray());
    }

    /// <summary>只在结构变化时解析规则和 MOD 的旧式更新能力，不把空 DataUpdate 登记到活动集合。</summary>
    public static bool TryGetTickPlan(ModuleData data, RuntimeItemDefinition definition,
        out RuntimeModuleDataRulePlan plan)
    {
        plan = null;
        if (data == null)
            return false;
        if (definition == null || !definition.TryGetModuleDataRules(data.StableName, data.ModuleId, out plan))
        {
            var key = (data.ModuleId ?? string.Empty, data.StableName ?? string.Empty);
            if (!fallbackPlans.TryGetValue(key, out plan))
            {
                plan = Compile(new RuntimeItemModuleDefinition(key.Item2, key.Item1, string.Empty, null, data.Enabled));
                fallbackPlans[key] = plan;
            }
        }
        return plan.HasRules || HasDataUpdateOverride(data.GetType());
    }

    private static bool HasDataUpdateOverride(Type type)
    {
        if (dataUpdateOverrides.TryGetValue(type, out bool result))
            return result;
        var method = type.GetMethod(nameof(ModuleData.DataUpdate), new[] { typeof(float) });
        result = method != null && method.IsVirtual && method.DeclaringType != typeof(ModuleData) &&
                 method.GetBaseDefinition().DeclaringType == typeof(ModuleData);
        dataUpdateOverrides.Add(type, result);
        return result;
    }

    internal static bool IsCurrent(ModuleDataTickContext context)
    {
        ModuleData data = context.ModuleData;
        if (data == null || !data.Enabled)
            return false;
        if (context.Scheduler != null && !context.Scheduler.IsCurrentHost(context.InventoryData, context.SchedulerGeneration))
            return false;
        if (context.Slot != null && !ReferenceEquals(context.Slot.itemData, context.ItemData))
            return false;
        if (context.InventoryData != null && context.Slot != null &&
            (context.InventoryData.itemSlots == null ||
             (uint)context.SlotIndex >= (uint)context.InventoryData.itemSlots.Count ||
             !ReferenceEquals(context.InventoryData.itemSlots[context.SlotIndex], context.Slot)))
            return false;
        return context.ItemData == null ||
               (context.ItemData.ModuleDataDic != null && !string.IsNullOrEmpty(data.StableName) &&
                context.ItemData.ModuleDataDic.TryGetValue(data.StableName, out ModuleData current) &&
                ReferenceEquals(current, data));
    }

    /// <summary>库存等纯数据宿主使用同一入口，停用模块不推进规则或默认数据 Tick。</summary>
    public static void Step(ModuleDataTickContext context)
    {
        RuntimeItemDefinition definition = context.Definition;
        if (definition == null && context.ItemData != null && GameRes.ExistingInstance != null)
            GameRes.ExistingInstance.TryGetItemDefinition(context.ItemData.IDName, out definition);
        if (TryGetTickPlan(context.ModuleData, definition, out RuntimeModuleDataRulePlan plan))
            Step(context, plan);
    }

    internal static void Step(ModuleDataTickContext context, RuntimeModuleDataRulePlan plan)
    {
        if (!IsCurrent(context) || context.DeltaTime <= 0f ||
            float.IsNaN(context.DeltaTime) || float.IsInfinity(context.DeltaTime))
            return;
        if (plan.Step(context) || !IsCurrent(context) || !HasDataUpdateOverride(context.ModuleData.GetType()))
            return;
        try
        {
            context.ModuleData.DataUpdate(context.DeltaTime);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ModuleDataRule] 能力 {context.ModuleData.ModuleId} 的数据更新失败：{exception}");
        }
    }

    #endregion
}
