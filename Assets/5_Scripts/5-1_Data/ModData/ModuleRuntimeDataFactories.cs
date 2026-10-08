using System;
using System.Collections.Generic;

/// <summary>MOD 可编译自己的纯内存默认状态，创建阶段无需调用持久化编解码器。</summary>
public interface IModuleRuntimeDataFactory
{
    Type DataType { get; }
    // 编译入口只接收独立默认数据；返回的委托每次必须创建独立状态，注册释放前先清理存活实例与计划。
    Func<ModuleData> Compile(ModuleData defaults);
}

public static class ModuleRuntimeDataFactories
{
    #region 运行态工厂注册

    private static readonly Dictionary<Type, IModuleRuntimeDataFactory> Factories = new();
    private static readonly object RegistryLock = new();

    public static IDisposable Register(IModuleRuntimeDataFactory factory)
    {
        Type type = factory?.DataType;
        if (type == null || !typeof(ModuleData).IsAssignableFrom(type) || IsBuiltin(type))
            throw new ArgumentException("运行态工厂必须声明独立的扩展模块数据类别。", nameof(factory));
        lock (RegistryLock)
        {
            if (Factories.ContainsKey(type)) throw new InvalidOperationException($"模块运行态工厂重复注册：{type.FullName}");
            Factories.Add(type, factory);
        }
        return new RegistrationLease(type, factory);
    }

    public static Func<ModuleData> Compile(ModuleData source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(source.StableName) || string.IsNullOrWhiteSpace(source.ModuleId))
            throw new InvalidOperationException("模块默认状态必须具有确定的稳定名与能力 ID。");
        Type type = source.GetType();
        // 自定义数据仍须声明持久化契约，运行态工厂不能绕开 MOD 状态边界。
        ModuleInstanceStateCodecs.GetRequired(type);
        Func<ModuleData> create = CompileBuiltin(source);
        ModuleData independentDefaults = source;
        if (create == null)
        {
            IModuleRuntimeDataFactory factory;
            lock (RegistryLock) Factories.TryGetValue(type, out factory);
            ModuleData frozen = ItemInstanceDataFactory.CloneModuleDefault(source);
            ModuleInstanceSnapshot state = ModuleInstanceSnapshot.Capture(source);
            state.RestoreTo(frozen);
            independentDefaults = frozen;
            if (factory != null)
                create = factory.Compile(frozen) ?? throw new InvalidOperationException($"模块运行态工厂没有提供创建入口：{type.FullName}");
            else
            {
                // 未提供内存工厂的 MOD 沿用自己的状态契约，不能丢掉被通用克隆忽略的负载。
                create = () =>
                {
                    ModuleData data = ItemInstanceDataFactory.CloneModuleDefault(frozen);
                    state.RestoreTo(data);
                    return data;
                };
            }
        }
        string stableName = source.StableName;
        string moduleId = source.ModuleId;
        bool enabled = source.Enabled;
        ModuleType moduleType = source.Type;
        return () =>
        {
            ModuleData data = create();
            if (data == null || data.GetType() != type || ReferenceEquals(data, source) || ReferenceEquals(data, independentDefaults))
                throw new InvalidOperationException($"模块 {stableName} 的运行态工厂未创建独立的 {type.Name} 数据。");
            data.StableName = stableName;
            data.ModuleId = moduleId;
            data.Enabled = enabled;
            data.Type = moduleType;
            return data;
        };
    }

    private static bool IsBuiltin(Type type) => type == typeof(Ex_ModData) || type == typeof(Ex_ModData_MemoryPackable) ||
        type == typeof(CollectableModuleData) || type == typeof(Inventory_ModuleData) || type == typeof(ModData_FoodData);

    private sealed class RegistrationLease : IDisposable
    {
        private readonly Type type;
        private readonly IModuleRuntimeDataFactory factory;
        private bool disposed;

        public RegistrationLease(Type type, IModuleRuntimeDataFactory factory) { this.type = type; this.factory = factory; }
        public void Dispose()
        {
            lock (RegistryLock)
            {
                if (disposed) return;
                if (Factories.TryGetValue(type, out var current) && ReferenceEquals(current, factory)) Factories.Remove(type);
                disposed = true;
            }
        }
    }

    #endregion

    #region 内建内存状态计划

    private static Func<ModuleData> CompileBuiltin(ModuleData source)
    {
        Type type = source.GetType();
        if (type == typeof(Ex_ModData))
        {
            string payload = ((Ex_ModData)source).BitData;
            return () => new Ex_ModData { BitData = payload };
        }
        if (type == typeof(Ex_ModData_MemoryPackable))
        {
            byte[] payload = CopyBytes(((Ex_ModData_MemoryPackable)source).BitData);
            return () => new Ex_ModData_MemoryPackable { BitData = CopyBytes(payload) };
        }
        if (type == typeof(CollectableModuleData))
        {
            var state = (CollectableModuleData)source;
            int stock = state.CurrentStock;
            bool initialized = state.IsInitialized;
            return () => new CollectableModuleData { CurrentStock = stock, IsInitialized = initialized };
        }
        if (type == typeof(Inventory_ModuleData))
        {
            var state = (Inventory_ModuleData)source;
            Func<Dictionary<string, Inventory_Data>> inventories = InventoryRuntimeDataFactory.CompileDictionary(state.Data);
            var position = state.PanleRectPosition;
            string initName = state.InventoryInitName;
            bool open = state.BasePanelIsOpen;
            return () => new Inventory_ModuleData
            {
                Data = inventories(), PanleRectPosition = position, InventoryInitName = initName, BasePanelIsOpen = open
            };
        }
        if (type == typeof(ModData_FoodData))
        {
            var state = (ModData_FoodData)source;
            Food food = CloneFood(state.FoodData ?? new Food());
            List<FoodMechanicStateData> mechanics = CloneMechanics(state.MechanicStates);
            return () => new ModData_FoodData { FoodData = CloneFood(food), MechanicStates = CloneMechanics(mechanics) };
        }
        return null;
    }

    private static byte[] CopyBytes(byte[] value) => value == null ? null : (byte[])value.Clone();

    private static Food CloneFood(Food source)
    {
        if (source.GetType() != typeof(Food)) return CloneExtension(source);
        return new Food
        {
            nutrition = CloneNutrition(source.nutrition),
            Max_EatingProgress = source.Max_EatingProgress,
            nutritionConsumeSpeed = source.nutritionConsumeSpeed?.GetType() == typeof(GameValue_float)
                ? ItemInstanceDataFactory.CloneGameValue(source.nutritionConsumeSpeed) : CloneExtension(source.nutritionConsumeSpeed),
            FeelGood = source.FeelGood, PanelPosition = source.PanelPosition,
            WaterConsumeSpeedRate = source.WaterConsumeSpeedRate, nutritionConsumeRate = source.nutritionConsumeRate
        };
    }

    private static Nutrition CloneNutrition(Nutrition source)
    {
        if (source == null) return null;
        if (source.GetType() != typeof(Nutrition)) return CloneExtension(source);
        return new Nutrition
        {
            Carbohydrates = source.Carbohydrates, Max_Carbohydrates = source.Max_Carbohydrates,
            Fat = source.Fat, Max_Fat = source.Max_Fat, Protein = source.Protein, Max_Protein = source.Max_Protein,
            Water = source.Water, Max_Water = source.Max_Water, Vitamins = source.Vitamins, Max_Vitamins = source.Max_Vitamins
        };
    }

    private static List<FoodMechanicStateData> CloneMechanics(List<FoodMechanicStateData> source)
    {
        var result = new List<FoodMechanicStateData>(source?.Count ?? 0);
        if (source != null)
            foreach (FoodMechanicStateData state in source)
                result.Add(state?.GetType() == typeof(FoodMechanicStateData) ? FoodMechanicStateData.CloneRuntime(state) : CloneExtension(state));
        return result;
    }

    private static T CloneExtension<T>(T source) where T : class
    {
        if (source == null) return null;
        T clone = FastCloner.FastCloner.DeepClone<object>(source) as T;
        if (clone == null || clone.GetType() != source.GetType() || ReferenceEquals(source, clone))
            throw new InvalidOperationException($"扩展运行态复制失败：{source.GetType().FullName}");
        return clone;
    }

    #endregion
}

public partial class FoodMechanicStateData
{
    #region 纯内存状态复制

    // 最新浮点缓存直接复制，不需要为了新建物品把数值先格式化再解析。
    internal static FoodMechanicStateData CloneRuntime(FoodMechanicStateData source) => new()
    {
        StateKey = source.StateKey,
        Data = source.Data == null ? null : new Dictionary<string, string>(source.Data, source.Data.Comparer),
        Payload = source.Payload == null ? null : (byte[])source.Payload.Clone(),
        runtimeFloats = source.runtimeFloats == null ? null : new Dictionary<string, float>(source.runtimeFloats, source.runtimeFloats.Comparer)
    };

    #endregion
}
