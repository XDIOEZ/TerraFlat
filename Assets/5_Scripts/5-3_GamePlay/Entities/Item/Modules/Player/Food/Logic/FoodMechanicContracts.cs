using System;
using System.Collections.Generic;

/// <summary>食物扩展的最小规则接口；规则只声明身份和执行顺序。</summary>
public interface IFoodMechanic
{
    string MechanicId { get; }
    int Priority { get; }
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
}

/// <summary>使用前拦截器，可阻止本次使用。</summary>
public interface IFoodUseGuard
{
    bool CanUse(FoodUseContext context, out string reason);
}

/// <summary>使用时执行。</summary>
public interface IFoodUseRule
{
    void OnFoodUse(FoodUseContext context);
}

/// <summary>模块 Tick 时执行。</summary>
public interface IFoodTickObserver
{
    void OnFoodTick(FoodTickContext context);
}

/// <summary>完整吃完一份食物时执行。</summary>
public interface IFoodConsumptionObserver
{
    void OnFoodConsumed(FoodConsumeResult result);
}

/// <summary>食物状态改变时执行。</summary>
public interface IFoodStateObserver
{
    void OnFoodStateChanged(FoodStateChangedContext context);
}

/// <summary>复活时重置。</summary>
public interface IFoodRespawnRule
{
    void OnFoodRespawn();
}

/// <summary>规则自己的独立存档。</summary>
public interface IFoodMechanicStateProvider
{
    string StateKey { get; }
    byte[] CaptureState();
    void RestoreState(byte[] payload);
}

/// <summary>按物品条件创建扩展规则。</summary>
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
        if (!string.IsNullOrWhiteSpace(ownerId))
            registrations.RemoveAll(item => string.Equals(
                item.OwnerId, ownerId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按物品 ID 注册。</summary>
    public static void RegisterForItemId(
        string ownerId,
        string itemId,
        Func<IFoodRuntimeContext, IFoodMechanic> factory,
        int priority = 0)
    {
        Register(ownerId,
            context => context?.ItemData != null && string.Equals(
                context.ItemData.IDName, itemId, StringComparison.OrdinalIgnoreCase),
            factory,
            priority);
    }

    /// <summary>按物品标签注册。</summary>
    public static void RegisterForTag(
        string ownerId,
        string tag,
        Func<IFoodRuntimeContext, IFoodMechanic> factory,
        int priority = 0)
    {
        Register(ownerId,
            context => context?.ItemData?.Tags != null && context.ItemData.Tags.Exists(
                value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase)),
            factory,
            priority);
    }

    internal static List<IFoodMechanic> CreateFor(IFoodRuntimeContext context)
    {
        List<Registration> matched = new List<Registration>();
        if (context == null)
            return new List<IFoodMechanic>();

        for (int i = 0; i < registrations.Count; i++)
        {
            Registration registration = registrations[i];
            try
            {
                if (registration.Matcher(context))
                    matched.Add(registration);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"[FoodMechanic] 匹配失败：{exception.Message}");
            }
        }

        matched.Sort((a, b) =>
        {
            int priority = a.Priority.CompareTo(b.Priority);
            return priority != 0
                ? priority
                : StringComparer.OrdinalIgnoreCase.Compare(a.OwnerId, b.OwnerId);
        });

        List<IFoodMechanic> result = new List<IFoodMechanic>(matched.Count);
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
                UnityEngine.Debug.LogError($"[FoodMechanic] 创建失败：{exception.Message}");
            }
        }

        return result;
    }
}

/// <summary>库存中任意 ModuleData 的 Tick 上下文。</summary>
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

/// <summary>库存模块 Tick 扩展点；未知模块仍由 Inventory 原逻辑处理。</summary>
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
        bool observed = false;
        for (int i = 0; i < observers.Count; i++)
        {
            IModuleDataTickObserver observer = observers[i];
            if (observer == null || !observer.CanObserve(context.ModuleData))
                continue;

            observed = true;
            try
            {
                observer.OnModuleDataTick(context);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"[ModuleDataTick] 执行失败：{exception.Message}");
            }
        }

        return observed;
    }
}
