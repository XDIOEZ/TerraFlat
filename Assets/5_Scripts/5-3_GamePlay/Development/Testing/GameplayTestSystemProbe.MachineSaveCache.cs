using System;
using System.Collections.Generic;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 机械存档缓存采样入口
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterMachineSaveSnapshotCacheRules()
    {
        Register("save", "machine_snapshot_cache", _ => MachineWorld.CaptureTestFrozenMachineSnapshots());
    }
    #endregion
}

public static partial class MachineWorld
{
    #region 独立机械快照缓存规则采样
    internal static JObject CaptureTestFrozenMachineSnapshots()
    {
        var child = new Data_GeneralItem { IDName = "probe:child", Guid = 7202, Stack = new ItemStack { Amount = 3f } };
        var node = new MachineEntity
        {
            Id = 7201, ScopeKey = "probe:machine", Definition = new MachineDefinition { Id = "probe:machine" },
            Snapshot = new Data_GeneralItem { IDName = "probe:machine", Guid = 7201, MaxDurability = 20f, Durability = 20f },
            State = new MachineState { Hp = 10f }
        };
        node.State.Processing.Input = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = child } }, "input");
        FrozenMachineSnapshot first = FreezeMachineSaveSnapshot(node);
        FrozenMachineSnapshot unchanged = FreezeMachineSaveSnapshot(node);
        node.State.Processing.Progress = .4f;
        FrozenMachineSnapshot progress = FreezeMachineSaveSnapshot(node);
        child.Stack.Amount = 7f;
        FrozenMachineSnapshot contents = FreezeMachineSaveSnapshot(node);
        node.State.Processing.Input.itemSlots[0].SlotMaxVolume = 12f;
        node.State.Processing.Input.itemSlots[0].CanAcceptTags.Add("probe:accepted");
        node.State.Processing.Input.ToggleActionName = "probe:toggle";
        node.State.Processing.Input.UIPrefabName = "probe:panel";
        FrozenMachineSnapshot layout = FreezeMachineSaveSnapshot(node);
        node.State.ManualSeconds = 4f;
        node.State.ElectricalStoredJoules = 42f;
        FrozenMachineSnapshot timing = FreezeMachineSaveSnapshot(node);
        node.ScopeKey = "probe:transferred";
        FrozenMachineSnapshot transferred = FreezeMachineSaveSnapshot(node);

        MachineState ReadState(FrozenMachineSnapshot frozen) => MachinePersistence.Read<MachineState>(
            MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(frozen.Payload).CreateColdData(), "core");
        MachineState original = ReadState(first);
        MachineState final = ReadState(timing);
        MachineState savedLayout = ReadState(layout);
        var logic = new MachineSaveCacheProbeLogic(node);
        node.Logic = logic;
        FrozenMachineSnapshot uncachedFirst = FreezeMachineSaveSnapshot(node);
        FrozenMachineSnapshot uncachedSecond = FreezeMachineSaveSnapshot(node);
        int beforeBatch = logic.Captures;
        FrozenMachineSnapshot batchFirst, batchSecond, revised;
        using (BeginSaveSnapshotCapture())
        {
            batchFirst = FreezeMachineSaveSnapshot(node);
            batchSecond = FreezeMachineSaveSnapshot(node);
            PublishMachineStateChanged(node);
            revised = FreezeMachineSaveSnapshot(node);
        }
        int batchCaptures = logic.Captures - beforeBatch;
        FrozenMachineSnapshot nextBatch = FreezeMachineSaveSnapshot(node);

        var source = new GameSaveData();
        source.Mechanical.Worlds.Add("probe:surface", new List<ItemData> { first.ArchiveData });
        source.Mechanical.Worlds.Add("probe:ship", new List<ItemData> { layout.ArchiveData });
        source.Mechanical.RemovedGenerated.Add("probe:surface", new HashSet<int> { 91 });
        source.Mechanical.NextPressureExplosionId = 77;
        source.Mechanical.PendingPressureLiquidSpills.Add(new PressureLiquidSpillState
        { WorldKey = "probe:surface", FluidId = "probe:liquid", LiquidMoles = 2m });
        byte[] archiveBytes = CaptureArchive(source);
        var restored = new GameSaveData();
        RestoreArchive(restored, archiveBytes);
        MachineState restoredLayout = MachinePersistence.Read<MachineState>(restored.Mechanical.Worlds["probe:ship"][0], "core");
        bool rejectedOldVersion = false;
        try { RestoreArchive(new GameSaveData(), MemoryPackSerializer.Serialize(new FrozenMachineArchivePayload { Version = 1 })); }
        catch (InvalidOperationException) { rejectedOldVersion = true; }

        FrozenMachineSnapshot privateCapture = FreezeMachineSaveSnapshot(node);
        bool isolatedPublic;
        using (ItemSnapshotSerialization.BeginPublicPlayerState(value => value))
            isolatedPublic = !ReferenceEquals(privateCapture.Payload, FreezeMachineSaveSnapshot(node).Payload);
        logic.Dispose();
        return new JObject
        {
            ["unchangedReused"] = ReferenceEquals(first.Payload, unchanged.Payload),
            ["progressRefrozen"] = !ReferenceEquals(first.Payload, progress.Payload),
            ["contentsRefrozen"] = !ReferenceEquals(progress.Payload, contents.Payload),
            ["layoutRefrozen"] = !ReferenceEquals(contents.Payload, layout.Payload),
            ["originalProgress"] = original.Processing.Progress,
            ["originalAmount"] = original.Processing.Input.itemSlots[0].itemData.Stack.Amount,
            ["finalProgress"] = final.Processing.Progress,
            ["finalAmount"] = final.Processing.Input.itemSlots[0].itemData.Stack.Amount,
            ["manualSeconds"] = final.ManualSeconds, ["electricalJoules"] = final.ElectricalStoredJoules,
            ["slotCapacity"] = savedLayout.Processing.Input.itemSlots[0].SlotMaxVolume,
            ["slotTag"] = savedLayout.Processing.Input.itemSlots[0].CanAcceptTags[0],
            ["inputAction"] = savedLayout.Processing.Input.ToggleActionName,
            ["panelPrefab"] = savedLayout.Processing.Input.UIPrefabName,
            ["scopeRefrozen"] = !ReferenceEquals(timing.Payload, transferred.Payload),
            ["unknownRefrozen"] = !ReferenceEquals(uncachedFirst.Payload, uncachedSecond.Payload),
            ["batchReused"] = ReferenceEquals(batchFirst.Payload, batchSecond.Payload),
            ["batchRevisionApplied"] = !ReferenceEquals(batchSecond.Payload, revised.Payload),
            ["batchCaptures"] = batchCaptures,
            ["batchEnded"] = !ReferenceEquals(revised.Payload, nextBatch.Payload),
            ["archiveVersion"] = restored.Mechanical.Version,
            ["archiveWorlds"] = restored.Mechanical.Worlds.Count,
            ["archiveAmount"] = restoredLayout.Processing.Input.itemSlots[0].itemData.Stack.Amount,
            ["archiveRemoved"] = restored.Mechanical.RemovedGenerated["probe:surface"].Contains(91),
            ["archiveEventId"] = restored.Mechanical.NextPressureExplosionId,
            ["archiveLiquid"] = (double)restored.Mechanical.PendingPressureLiquidSpills[0].LiquidMoles,
            ["oldVersionRejected"] = rejectedOldVersion, ["publicPolicyIsolated"] = isolatedPublic
        };
    }

    private sealed class MachineSaveCacheProbeLogic : MachineLogic
    {
        internal int Captures;
        internal MachineSaveCacheProbeLogic(MachineEntity node) : base(node) { }
        public override void Capture() => MachinePersistence.Write(Entity.Snapshot, "probe", ++Captures);
    }
    #endregion
}
