using System;
using FlatWorld.DroppedItems;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed partial class DroppedItemRuntime
{
    #region 轻量实物输出端口
    private IItemTransferPort GetConveyorOutput(int id) => new LightweightOutput(this, id);
    private sealed class LightweightOutput : ContainerPortBase, IItemTransferPort
    {
        private readonly DroppedItemRuntime runtime; private readonly int id;
        public LightweightOutput(DroppedItemRuntime runtime, int id)
            : base($"drop:{DroppedItemService.Epoch}:{id}", "dropped-output", new ContainerPortConfiguration
            { Id = "dropped-output", Type = "core:dropped_item", Direction = ContainerPortDirection.Output, Access = ContainerAccessKind.Machine, Reach = 2f },
                () => runtime.simulation.IsCreated && runtime.simulation.Contains(id), position: runtime.simulation.Get(id).Position,
                sceneHandle: SceneManager.GetSceneByName(MachineWorld.WorldKey).handle)
        { this.runtime = runtime; this.id = id; }
        public override object StorageIdentity => runtime.payloads[id];
        public override long StateVersion => runtime.simulation.Get(id).Amount.GetHashCode();
        public ItemData PeekItem(string itemId = null, string tag = null)
        {
            if (!IsValid) return null;
            LightweightDroppedBody body = runtime.simulation.Get(id);
            ItemData payload = runtime.payloads[id];
            if (body.Pickable == 0 || body.Amount < 1f || itemId != null && payload.IDName != itemId || tag != null && payload.Tags?.Contains(tag) != true) return null;
            ItemData copy = ItemInstanceDataFactory.CloneRuntime(payload); copy.Stack.Amount = body.Amount; return copy;
        }
        public int GetReceivableItems(ItemData item, int requested) => 0;
        public bool InsertItems(ItemData item, int amount) => false;
        public bool ExtractItems(ItemData expected, int amount, out ItemData extracted)
        {
            extracted = null;
            if (!IsValid || runtime.pickupReservations.Contains(id)) return false;
            LightweightDroppedBody body = runtime.simulation.Get(id);
            if (body.Pickable == 0 || body.Amount < amount || amount <= 0 || expected.IDName != runtime.payloads[id].IDName || expected.Guid != runtime.payloads[id].Guid) return false;
            runtime.pickupReservations.Add(id); extracted = ItemInstanceDataFactory.CloneRuntime(runtime.payloads[id]); extracted.Stack.Amount = amount;
            if (body.Amount > amount) { int guid; do guid = Guid.NewGuid().GetHashCode(); while (guid == 0 || guid == extracted.Guid); extracted.Guid = guid; }
            body.Amount -= amount; runtime.simulation.Set(body); return true;
        }
        public override object CaptureState() => runtime.simulation.Get(id);
        public override void RestoreState(object snapshot) { runtime.simulation.Set((LightweightDroppedBody)snapshot); runtime.pickupReservations.Remove(id); runtime.UpdatePlacement(id); }
        public override void PublishState()
        {
            if (runtime.simulation.Get(id).Amount <= 0f) runtime.Remove(id); else runtime.UpdatePlacement(id);
            runtime.pickupReservations.Remove(id);
        }
    }
    #endregion
}
