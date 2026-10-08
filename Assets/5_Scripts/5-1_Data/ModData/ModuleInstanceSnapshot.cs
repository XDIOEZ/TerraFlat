using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using UnityEngine;

/// <summary>模块身份与不透明状态负载；具体实现由显式注册的状态编解码器负责。</summary>
[MemoryPackable]
public sealed partial class ModuleInstanceSnapshot
{
    #region 快照状态

    [MemoryPackInclude] private string stableName;
    [MemoryPackInclude] private string moduleId;
    [MemoryPackInclude] private string codecId;
    [MemoryPackInclude] private byte[] payload;

    [MemoryPackIgnore] public string StableName => stableName;
    [MemoryPackIgnore] public string ModuleId => moduleId;
    [MemoryPackIgnore] public string CodecId => codecId;
    [MemoryPackIgnore] public bool IsValid => !string.IsNullOrWhiteSpace(stableName) &&
        !string.IsNullOrWhiteSpace(moduleId) && !string.IsNullOrWhiteSpace(codecId) && payload != null;

    public static ModuleInstanceSnapshot Capture(ModuleData data, bool publicState = false)
    {
        IModuleInstanceStateCodec codec = ModuleInstanceStateCodecs.GetRequired(data.GetType());
        byte[] capturedPayload = codec.Capture(data, publicState) ?? Array.Empty<byte>();
        return new ModuleInstanceSnapshot
        {
            stableName = data.StableName, moduleId = data.ModuleId, codecId = codec.Id,
            payload = (byte[])capturedPayload.Clone()
        };
    }

    public bool CanRestoreTo(ModuleData data) => data != null && data.StableName == stableName &&
        data.ModuleId == moduleId && ModuleInstanceStateCodecs.TryGet(data.GetType(), out var codec) && codec.Id == codecId;

    public void RestoreTo(ModuleData data)
    {
        if (!CanRestoreTo(data)) throw new InvalidDataException("模块快照与当前定义不匹配。");
        // 扩展编解码器只能拿到独立缓冲，不能改写冻结快照。
        ModuleInstanceStateCodecs.GetRequired(data.GetType()).Restore(data, (byte[])payload.Clone());
    }

    public bool Matches(ModuleData data)
    {
        if (!CanRestoreTo(data)) return false;
        ModuleInstanceSnapshot other = Capture(data);
        if (payload.Length != other.payload.Length) return false;
        for (int i = 0; i < payload.Length; i++)
            if (payload[i] != other.payload[i]) return false;
        return true;
    }

    internal ModuleData CreateColdData()
    {
        if (!ModuleInstanceStateCodecs.TryGet(codecId, out var codec)) return null;
        ModuleData data = codec.CreateData();
        data.StableName = stableName;
        data.ModuleId = moduleId;
        return data;
    }

    #endregion
}

/// <summary>MOD 注册自己的实例状态契约，禁止把配置字段反射复制到实例。</summary>
public interface IModuleInstanceStateCodec
{
    // 注册身份在租期内保持不变；释放租期前由宿主完成进行中的状态调用及实例清理。
    string Id { get; }
    Type DataType { get; }
    ModuleData CreateData();
    byte[] Capture(ModuleData data, bool publicState);
    void Restore(ModuleData data, byte[] payload);
}

/// <summary>可选 MOD 快照缓存契约，指纹必须覆盖对应公开或私有编解码载荷的全部实例值。</summary>
public interface IModuleInstanceStateFingerprintProvider
{
    // 不改写活跃数据；无法完整判断变化时返回 false，宿主会继续完整捕获。
    bool TryCalculateStateFingerprint(ModuleData data, bool publicState, out ulong fingerprint);
}

public static class ModuleInstanceStateCodecs
{
    #region 注册表

    private static readonly Dictionary<Type, IModuleInstanceStateCodec> ByType = new();
    private static readonly Dictionary<string, IModuleInstanceStateCodec> ById = new(StringComparer.Ordinal);
    private static readonly object RegistryLock = new();
    public static ulong Revision { get; private set; }

    static ModuleInstanceStateCodecs()
    {
        Register(new JsonStateCodec());
        Register(new BinaryStateCodec());
        Register(new CollectableStateCodec());
        Register(new InventoryStateCodec());
        Register(new FoodStateCodec());
    }

    public static IDisposable Register(IModuleInstanceStateCodec codec)
    {
        Type registeredType = codec?.DataType;
        string registeredId = codec?.Id;
        if (registeredType == null || !typeof(ModuleData).IsAssignableFrom(registeredType) ||
            string.IsNullOrWhiteSpace(registeredId)) throw new ArgumentException("模块状态编解码器声明无效。", nameof(codec));
        lock (RegistryLock)
        {
            if (ByType.ContainsKey(registeredType) || ById.ContainsKey(registeredId))
                throw new InvalidOperationException($"模块状态编解码器重复注册：{registeredId}");
            ByType.Add(registeredType, codec);
            ById.Add(registeredId, codec);
            Revision++;
        }
        return new RegistrationLease(codec, registeredType, registeredId);
    }

    private sealed class RegistrationLease : IDisposable
    {
        private readonly IModuleInstanceStateCodec codec;
        private readonly Type registeredType;
        private readonly string registeredId;
        private bool disposed;
        public RegistrationLease(IModuleInstanceStateCodec codec, Type registeredType, string registeredId)
        {
            this.codec = codec;
            this.registeredType = registeredType;
            this.registeredId = registeredId;
        }
        public void Dispose()
        {
            lock (RegistryLock)
            {
                if (disposed) return;
                if (ByType.TryGetValue(registeredType, out var current) && ReferenceEquals(current, codec))
                    ByType.Remove(registeredType);
                if (ById.TryGetValue(registeredId, out current) && ReferenceEquals(current, codec))
                    ById.Remove(registeredId);
                Revision++;
                disposed = true;
            }
        }
    }

    public static bool TryGet(Type type, out IModuleInstanceStateCodec codec)
    {
        lock (RegistryLock) return ByType.TryGetValue(type, out codec);
    }
    public static bool TryGet(string id, out IModuleInstanceStateCodec codec)
    {
        lock (RegistryLock) return ById.TryGetValue(id, out codec);
    }
    public static IModuleInstanceStateCodec GetRequired(Type type) => TryGet(type, out var codec) ? codec :
        throw new InvalidOperationException($"模块数据未注册实例状态契约：{type?.FullName}");

    #endregion

    #region 内建状态契约

    private sealed class JsonStateCodec : IModuleInstanceStateCodec
    {
        public string Id => "core.module.json.v1";
        public Type DataType => typeof(Ex_ModData);
        public ModuleData CreateData() => new Ex_ModData();
        public byte[] Capture(ModuleData data, bool publicState) => ItemSnapshotSerialization.SerializePayload(((Ex_ModData)data).BitData);
        public void Restore(ModuleData data, byte[] payload) => ((Ex_ModData)data).BitData = MemoryPackSerializer.Deserialize<string>(payload);
    }

    private sealed class BinaryStateCodec : IModuleInstanceStateCodec
    {
        public string Id => "core.module.binary.v1";
        public Type DataType => typeof(Ex_ModData_MemoryPackable);
        public ModuleData CreateData() => new Ex_ModData_MemoryPackable();
        public byte[] Capture(ModuleData data, bool publicState) =>
            ((Ex_ModData_MemoryPackable)data).BitData == null ? Array.Empty<byte>() : (byte[])((Ex_ModData_MemoryPackable)data).BitData.Clone();
        public void Restore(ModuleData data, byte[] payload) => ((Ex_ModData_MemoryPackable)data).BitData = (byte[])payload.Clone();
    }

    private sealed class CollectableStateCodec : IModuleInstanceStateCodec
    {
        public string Id => "core.module.collectable.v1";
        public Type DataType => typeof(CollectableModuleData);
        public ModuleData CreateData() => new CollectableModuleData();
        public byte[] Capture(ModuleData data, bool publicState)
        {
            var state = (CollectableModuleData)data;
            return ItemSnapshotSerialization.SerializePayload(new CollectableInstanceState { Stock = state.CurrentStock, Initialized = state.IsInitialized });
        }
        public void Restore(ModuleData data, byte[] payload)
        {
            var state = MemoryPackSerializer.Deserialize<CollectableInstanceState>(payload);
            var target = (CollectableModuleData)data;
            target.CurrentStock = state.Stock;
            target.IsInitialized = state.Initialized;
        }
    }

    private sealed class InventoryStateCodec : IModuleInstanceStateCodec
    {
        public string Id => "core.module.inventory.v1";
        public Type DataType => typeof(Inventory_ModuleData);
        public ModuleData CreateData() => new Inventory_ModuleData();
        public byte[] Capture(ModuleData data, bool publicState)
        {
            var state = (Inventory_ModuleData)data;
            return ItemSnapshotSerialization.SerializePayload(new InventoryModuleInstanceState
            {
                Inventories = publicState ? null : InventoryInstanceSnapshot.CaptureDictionary(state.Data),
                PanelPosition = state.PanleRectPosition, PanelIsOpen = state.BasePanelIsOpen
            });
        }
        public void Restore(ModuleData data, byte[] payload)
        {
            var state = MemoryPackSerializer.Deserialize<InventoryModuleInstanceState>(payload);
            var target = (Inventory_ModuleData)data;
            if (state.Inventories != null)
                InventoryInstanceSnapshot.RestoreDictionary(state.Inventories, target.Data ??= new());
            target.PanleRectPosition = state.PanelPosition;
            target.BasePanelIsOpen = state.PanelIsOpen;
        }
    }

    private sealed class FoodStateCodec : IModuleInstanceStateCodec
    {
        public string Id => "core.module.food.v1";
        public Type DataType => typeof(ModData_FoodData);
        public ModuleData CreateData() => new ModData_FoodData();
        public byte[] Capture(ModuleData data, bool publicState)
        {
            var state = (ModData_FoodData)data;
            return ItemSnapshotSerialization.SerializePayload(new FoodInstanceState
            {
                MechanicStates = state.MechanicStates, PanelPosition = state.FoodData?.PanelPosition ?? Vector2.zero,
                Nutrition = NutritionInstanceState.Capture(state.FoodData?.nutrition)
            });
        }
        public void Restore(ModuleData data, byte[] payload)
        {
            var state = MemoryPackSerializer.Deserialize<FoodInstanceState>(payload);
            var target = (ModData_FoodData)data;
            target.MechanicStates = state.MechanicStates ?? new();
            Food food = target.EnsureFoodData();
            food.PanelPosition = state.PanelPosition;
            state.Nutrition?.RestoreTo(food.nutrition ??= new Nutrition());
        }
    }

    #endregion
}

[MemoryPackable]
internal sealed partial class CollectableInstanceState
{
    public int Stock;
    public bool Initialized;
}

[MemoryPackable]
internal sealed partial class InventoryModuleInstanceState
{
    public Dictionary<string, InventoryInstanceSnapshot> Inventories;
    public Vector3 PanelPosition;
    public bool PanelIsOpen;
}

[MemoryPackable]
internal sealed partial class FoodInstanceState
{
    public List<FoodMechanicStateData> MechanicStates;
    public Vector2 PanelPosition;
    public NutritionInstanceState Nutrition;
}

/// <summary>营养当前值和运行中增长的脂肪容量持久化，代谢与摄取配置继续读取当前定义。</summary>
[MemoryPackable]
internal sealed partial class NutritionInstanceState
{
    #region 营养实例状态

    public float Carbohydrates;
    public float Fat;
    public float Protein;
    public float Water;
    public float Vitamins;
    public float MaxFat;

    public static NutritionInstanceState Capture(Nutrition data) => data == null ? null : new NutritionInstanceState
    {
        Carbohydrates = data.Carbohydrates, Fat = data.Fat, Protein = data.Protein,
        Water = data.Water, Vitamins = data.Vitamins, MaxFat = data.Max_Fat
    };

    public void RestoreTo(Nutrition data)
    {
        data.Carbohydrates = Carbohydrates;
        data.Fat = Fat;
        data.Protein = Protein;
        data.Water = Water;
        data.Vitamins = Vitamins;
        data.Max_Fat = MaxFat;
    }

    #endregion
}
