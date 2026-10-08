using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>运行态版本先读取纯数据；只有变化的模块重新冻结，实例变化后才编码网络负载。</summary>
internal static class ItemNetworkCaptureCache
{
    #region 按实例租期隔离的缓存

    internal readonly struct CaptureResult
    {
        public readonly ulong Version;
        public readonly byte[] Payload;
        public readonly uint Hash;
        public readonly bool Reusable;
        public CaptureResult(ulong version, byte[] payload, uint hash, bool reusable)
        { Version = version; Payload = payload; Hash = hash; Reusable = reusable; }
    }

    private sealed class ModuleState
    {
        public ulong Signature;
        public ModuleInstanceSnapshot Snapshot;
    }

    private sealed class CaptureState
    {
        public readonly HashSet<ItemData> Visiting = new();
        public readonly Dictionary<ModuleData, ModuleState> Modules = new();
        public readonly Func<ModuleData, bool, ModuleInstanceSnapshot> CaptureModule;
        public ulong Signature;
        public bool HasSignature;
        public uint StructureVersion;
        public CaptureResult Result;

        public CaptureState() => CaptureModule = CaptureModuleState;

        private ModuleInstanceSnapshot CaptureModuleState(ModuleData data, bool publicState)
        {
            Visiting.Clear();
            bool versioned = ItemRuntimeStateFingerprint.TryCalculateModule(data, publicState, Visiting, out ulong signature);
            if (versioned && Modules.TryGetValue(data, out ModuleState known) && known.Signature == signature)
                return known.Snapshot;
            ModuleInstanceSnapshot snapshot = ModuleInstanceSnapshot.Capture(data, publicState);
            if (versioned) Modules[data] = new ModuleState { Signature = signature, Snapshot = snapshot };
            else Modules.Remove(data);
            return snapshot;
        }
    }

    private sealed class InstanceCache
    {
        public ItemData Data;
        public uint Generation;
        public ulong NextVersion;
        public readonly Dictionary<int, CaptureState> States = new();
    }

    private static readonly ConditionalWeakTable<Item, InstanceCache> Instances = new();

    public static void Invalidate(Item item)
    {
        if (!ReferenceEquals(item, null)) Instances.Remove(item);
    }

    #endregion

    #region 捕获版本与模块增量

    public static CaptureResult Capture(Item item, bool ignoreTransform)
    {
        ItemData data = item.itemData;
        InstanceCache instance = Instances.GetValue(item, static _ => new InstanceCache());
        if (!ReferenceEquals(instance.Data, data) || instance.Generation != item.RuntimeGeneration)
        {
            instance.Data = data;
            instance.Generation = item.RuntimeGeneration;
            instance.States.Clear();
        }

        bool publicState = item is Player;
        int mode = (ignoreTransform ? 1 : 0) | (publicState ? 2 : 0);
        if (!instance.States.TryGetValue(mode, out CaptureState state))
            instance.States.Add(mode, state = new CaptureState());
        // 相同数值的模块替换也要释放旧引用，负载相同仍可继续复用。
        if (state.StructureVersion != data.ModuleStructureVersion)
        {
            state.Modules.Clear();
            state.StructureVersion = data.ModuleStructureVersion;
        }
        state.Visiting.Clear();
        bool versioned = ItemRuntimeStateFingerprint.TryCalculate(data, !ignoreTransform, publicState,
            state.Visiting, out ulong signature);
        if (versioned && state.HasSignature && state.Signature == signature)
            return state.Result;

        ItemInstanceSnapshot snapshot = ItemInstanceSnapshot.Capture(data, !ignoreTransform,
            publicState, MachineInventoryCommands.PublicSpecialData, state.CaptureModule);
        byte[] payload = ItemNetworkStateSerialization.SerializeSnapshot(snapshot);
        state.Signature = signature;
        state.HasSignature = versioned;
        state.Result = new CaptureResult(++instance.NextVersion, payload,
            ItemNetworkStateSerialization.CalculateHash(payload), versioned);
        return state.Result;
    }

    #endregion
}
