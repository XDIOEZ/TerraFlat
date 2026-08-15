using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 食物运行时扩展契约。食物机制只依赖窄接口，不直接依赖 Mod_Food 的具体实现，
/// 便于本体和编译型 MOD 在不修改食物执行器的情况下增加新规则。
/// </summary>
public interface IFoodMechanic : IDisposable
{
    string MechanicId { get; }
    int Priority { get; }
    void Attach(IFoodRuntimeContext context);
}

/// <summary>食物运行时只读/受控上下文。</summary>
public interface IFoodRuntimeContext
{
    Item Item { get; }
    ItemData ItemData { get; }
    Food Data { get; }
    ModData_FoodData PersistentData { get; }
    float EatingProgress { get; set; }
    bool ConsumptionEnabled { get; }
    string ConsumeCompleteReplacementItemID { get; }
    FoodConsumeKind ConsumeKind { get; }
    bool IsPlayer { get; }
    IFoodItemOperationGateway ItemOperations { get; }
    FoodMechanicEventHub Events { get; }
}

/// <summary>使用前拦截器，可阻止某次使用并返回原因。</summary>
public interface IFoodUseGuard
{
    bool CanUse(FoodUseContext context, out string reason);
}

/// <summary>监听食物 Tick 的机制接口。</summary>
public interface IFoodTickObserver
{
    void OnFoodTick(FoodTickContext context);
}

/// <summary>监听完整食用结果的机制接口。</summary>
public interface IFoodConsumptionObserver
{
    void OnFoodConsumed(FoodConsumeResult result);
}

/// <summary>监听食物状态刷新的机制接口。</summary>
public interface IFoodStateObserver
{
    void OnFoodStateChanged(FoodStateChangedContext context);
}

/// <summary>为自定义食物机制提供独立存档负载。</summary>
public interface IFoodMechanicStateProvider
{
    string StateKey { get; }
    byte[] CaptureState();
    void RestoreState(byte[] payload);
}

public readonly struct FoodUseContext
{
    public FoodUseContext(IFoodRuntimeContext food, IFoodRuntimeContext consumer)
    {
        Food = food;
        Consumer = consumer;
    }

    public IFoodRuntimeContext Food { get; }
    public IFoodRuntimeContext Consumer { get; }
}

public readonly struct FoodTickContext
{
    public FoodTickContext(IFoodRuntimeContext food, float deltaTime)
    {
        Food = food;
        DeltaTime = deltaTime;
    }

    public IFoodRuntimeContext Food { get; }
    public float DeltaTime { get; }
}

public readonly struct FoodStateChangedContext
{
    public FoodStateChangedContext(IFoodRuntimeContext food)
    {
        Food = food;
    }

    public IFoodRuntimeContext Food { get; }
}

/// <summary>
/// 食物事件总线。事件分发逐个保护异常，单个 MOD 机制失败不会中断基础食物流程。
/// </summary>
public sealed class FoodMechanicEventHub
{
    public event Action<FoodUseContext> UseRequested;
    public event Action<FoodTickContext> Tick;
    public event Action<FoodConsumeResult> Consumed;
    public event Action<FoodStateChangedContext> StateChanged;

    internal bool CanUse(FoodUseContext context, IReadOnlyList<IFoodUseGuard> guards)
    {
        if (guards == null)
            return true;

        for (int i = 0; i < guards.Count; i++)
        {
            IFoodUseGuard guard = guards[i];
            if (guard == null)
                continue;

            try
            {
                if (!guard.CanUse(context, out string reason))
                {
                    if (!string.IsNullOrWhiteSpace(reason))
                        UnityEngine.Debug.LogWarning($"[FoodMechanic] 使用被机制阻止：{reason}");
                    return false;
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"[FoodMechanic] 使用拦截器执行失败：{exception.Message}");
                UnityEngine.Debug.LogException(exception);
            }
        }

        return true;
    }

    internal void PublishUse(FoodUseContext context)
    {
        InvokeSafely(UseRequested, context, "使用事件");
    }

    internal void PublishTick(FoodTickContext context)
    {
        InvokeSafely(Tick, context, "Tick 事件");
    }

    internal void PublishConsumed(FoodConsumeResult result)
    {
        InvokeSafely(Consumed, result, "完成食用事件");
    }

    internal void PublishStateChanged(FoodStateChangedContext context)
    {
        InvokeSafely(StateChanged, context, "状态变化事件");
    }

    private static void InvokeSafely<T>(Action<T> handlers, T value, string eventName)
    {
        if (handlers == null)
            return;

        Delegate[] invocationList = handlers.GetInvocationList();
        for (int i = 0; i < invocationList.Length; i++)
        {
            if (!(invocationList[i] is Action<T> handler))
                continue;

            try
            {
                handler.Invoke(value);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"[FoodMechanic] {eventName}监听器执行失败：{exception.Message}");
                UnityEngine.Debug.LogException(exception);
            }
        }
    }
}

/// <summary>按物品 ID、标签或任意只读上下文条件注册食物机制。</summary>
public static class FoodMechanicRegistry
{
    private sealed class Registration
    {
        public string OwnerId;
        public int Priority;
        public Func<IFoodRuntimeContext, bool> Matcher;
        public Func<IFoodRuntimeContext, IFoodMechanic> Factory;
    }

    private static readonly List<Registration> registrations = new List<Registration>();

    public static void Register(
        string ownerId,
        Func<IFoodRuntimeContext, bool> matcher,
        Func<IFoodRuntimeContext, IFoodMechanic> factory,
        int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("食物机制注册者 ID 不能为空", nameof(ownerId));
        if (matcher == null)
            throw new ArgumentNullException(nameof(matcher));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        Unregister(ownerId);
        registrations.Add(new Registration
        {
            OwnerId = ownerId.Trim(),
            Priority = priority,
            Matcher = matcher,
            Factory = factory
        });
    }

    public static void Unregister(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return;

        registrations.RemoveAll(item =>
            string.Equals(item.OwnerId, ownerId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    internal static List<IFoodMechanic> CreateFor(IFoodRuntimeContext context)
    {
        List<IFoodMechanic> result = new List<IFoodMechanic>();
        if (context == null)
            return result;

        List<Registration> matched = registrations
            .Where(item => item != null && item.Matcher != null && item.Factory != null)
            .Where(item =>
            {
                try
                {
                    return item.Matcher(context);
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogError($"[FoodMechanic] 机制匹配失败：{exception.Message}");
                    return false;
                }
            })
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.OwnerId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < matched.Count; i++)
        {
            try
            {
                IFoodMechanic mechanic = matched[i].Factory(context);
                if (mechanic != null)
                    result.Add(mechanic);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"[FoodMechanic] 创建机制失败：{exception.Message}");
                UnityEngine.Debug.LogException(exception);
            }
        }

        return result;
    }
}

/// <summary>库存中的 ModuleData 通用 Tick 上下文，避免 Inventory 直接依赖具体食物逻辑。</summary>
public readonly struct ModuleDataTickContext
{
    public ModuleDataTickContext(
        ModuleData moduleData,
        ItemData itemData,
        Inventory_Data inventoryData,
        ItemSlot slot,
        int slotIndex,
        float deltaTime)
    {
        ModuleData = moduleData;
        ItemData = itemData;
        InventoryData = inventoryData;
        Slot = slot;
        SlotIndex = slotIndex;
        DeltaTime = deltaTime;
    }

    public ModuleData ModuleData { get; }
    public ItemData ItemData { get; }
    public Inventory_Data InventoryData { get; }
    public ItemSlot Slot { get; }
    public int SlotIndex { get; }
    public float DeltaTime { get; }
}

public interface IModuleDataTickObserver
{
    bool CanObserve(ModuleData moduleData);
    void OnModuleDataTick(ModuleDataTickContext context);
}

/// <summary>通用 ModuleData 观察者注册表；未知模块继续走原有 DataUpdate 后备入口。</summary>
public static class ModuleDataTickObserverRegistry
{
    private static readonly List<IModuleDataTickObserver> observers =
        new List<IModuleDataTickObserver>();

    static ModuleDataTickObserverRegistry()
    {
        Register(new FoodSpoilageModuleDataObserver());
    }

    public static void Register(IModuleDataTickObserver observer)
    {
        if (observer != null && !observers.Contains(observer))
            observers.Add(observer);
    }

    public static void Unregister(IModuleDataTickObserver observer)
    {
        if (observer != null)
            observers.Remove(observer);
    }

    public static bool Publish(ModuleDataTickContext context)
    {
        for (int i = 0; i < observers.Count; i++)
        {
            IModuleDataTickObserver observer = observers[i];
            if (observer == null)
                continue;

            try
            {
                if (observer.CanObserve(context.ModuleData))
                {
                    observer.OnModuleDataTick(context);
                    return true;
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"[ModuleDataTick] 观察者执行失败：{exception.Message}");
                UnityEngine.Debug.LogException(exception);
                return true;
            }
        }

        return false;
    }
}
