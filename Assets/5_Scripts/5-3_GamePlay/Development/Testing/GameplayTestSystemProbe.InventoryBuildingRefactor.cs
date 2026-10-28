using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public static partial class GameplayTestSystemProbe
{
    #region 库存绑定、指针命中与建筑封包回归
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterInventoryBuildingRefactorProbes()
    {
        Register("inventory", "slot_binding_reuse", CaptureInventorySlotBindingReuse);
        Register("inventory", "pointer_query_reuse", CaptureInventoryPointerQueryReuse);
        Register("building", "snapshot_repacking", CaptureBuildingSnapshotRepacking);
    }

    private sealed class InventorySlotBindingProbe : Inventory
    {
        public int Clicks, Submits, Taps, DesktopTaps, LastIndex = -1;
        public override void OnLeftClick(int index) { Clicks++; LastIndex = index; }
        public override void OnGamepadSubmit(int index) { Submits++; LastIndex = index; }
        public override void OnTouchTap(int index) { Taps++; LastIndex = index; }
        public override void OnDesktopTap(int index) { DesktopTaps++; LastIndex = index; }
    }

    // 两条正式绑定链连续重绑后实际触发交互，避免只核对委托字段存在。
    private static JObject CaptureInventorySlotBindingReuse(JObject args)
    {
        var root = new GameObject("[LuaInventorySlotBinding]");
        root.SetActive(false);
        try
        {
            ItemSlot_UI slot = root.AddComponent<ItemSlot_UI>();
            var inventory = new InventorySlotBindingProbe
            {
                Data = new Inventory_Data(new List<ItemSlot> { new(0), new(1) }, "binding-probe"),
                itemSlot_UI = new List<ItemSlot_UI> { slot, slot }
            };
            inventory.SyncData();
            inventory.SyncData();
            inventory.BindSlotUI(slot, 1, registerPrimary: false);
            inventory.BindSlotUI(slot, 1, registerPrimary: false);
            Func<int, ItemSlot> getter = slot.GetSlotDataFunc;
            inventory.RebindVirtualSlotUI(slot, 0);
            slot.OnLeftClick.Invoke(slot.slotIndex);
            slot.OnGamepadSubmit.Invoke(slot.slotIndex);
            slot.OnTouchTap(slot.slotIndex);
            slot.OnDesktopTap(slot.slotIndex);
            return new JObject
            {
                ["clicks"] = inventory.Clicks, ["submits"] = inventory.Submits,
                ["taps"] = inventory.Taps, ["desktopTaps"] = inventory.DesktopTaps,
                ["lastIndex"] = inventory.LastIndex,
                ["correctData"] = ReferenceEquals(slot.GetBoundSlotData(), inventory.Data.itemSlots[0]),
                ["getterReused"] = ReferenceEquals(getter, slot.GetSlotDataFunc)
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private sealed class InventoryPointerQueryProbeRaycaster : BaseRaycaster
    {
        public readonly List<GameObject> Targets = new();
        public int Passes;
        public override Camera eventCamera => null;
        public override int sortOrderPriority => int.MaxValue;
        public override void Raycast(PointerEventData data, List<RaycastResult> results)
        {
            Passes++;
            foreach (GameObject target in Targets)
                results.Add(new RaycastResult { gameObject = target, module = this, index = results.Count });
        }
    }

    private sealed class InventoryPointerQueryProbeDropTarget : MonoBehaviour, IInventoryDragDropTarget
    {
        public bool ContainsPoint;
        public bool ContainsInventoryDropPoint(Vector2 screenPosition, Camera eventCamera) => ContainsPoint;
        public bool TryAcceptInventoryDrag(InventoryDragTransaction transaction, Vector2 screenPosition,
            Camera eventCamera) => false;
    }

    // 使用真实 EventSystem 统计射线次数，所有查询均调用正式缓冲对象。
    private static JObject CaptureInventoryPointerQueryReuse(JObject args)
    {
        EventSystem previous = EventSystem.current;
        var root = new GameObject("[LuaInventoryPointerQuery]");
        try
        {
            EventSystem system = previous != null ? previous : root.AddComponent<EventSystem>();
            var raycaster = root.AddComponent<InventoryPointerQueryProbeRaycaster>();
            var slotObject = new GameObject("Slot");
            slotObject.transform.SetParent(root.transform);
            ItemSlot_UI slot = slotObject.AddComponent<ItemSlot_UI>();
            var dropObject = new GameObject("GameplayDrop");
            dropObject.transform.SetParent(root.transform);
            var target = dropObject.AddComponent<InventoryPointerQueryProbeDropTarget>();
            raycaster.Targets.Add(slotObject);
            raycaster.Targets.Add(dropObject);
            var pointer = new PointerEventData(system) { position = new Vector2(-100000f, -100000f) };
            var query = new InventorySlotPointerQuery();
            query.Capture(pointer);
            target.ContainsPoint = true;
            bool slotMatched = query.FindSlot() == slot;
            bool dropMatched = ReferenceEquals(query.FindGameplayTarget(pointer), target);
            bool blocked = query.IsOverBlockingUI(pointer);
            target.enabled = false;
            bool disabledRejected = query.FindGameplayTarget(pointer) == null;
            target.enabled = true;
            target.ContainsPoint = false;
            bool outsideRejected = query.FindGameplayTarget(pointer) == null;
            int firstPasses = raycaster.Passes;
            raycaster.Targets.Clear();
            query.Capture(pointer);
            return new JObject
            {
                ["slotMatched"] = slotMatched, ["dropMatched"] = dropMatched, ["blocked"] = blocked,
                ["disabledRejected"] = disabledRejected, ["outsideRejected"] = outsideRejected,
                ["firstPasses"] = firstPasses, ["totalPasses"] = raycaster.Passes,
                ["cleared"] = query.FindSlot() == null && query.FindGameplayTarget(pointer) == null &&
                                !query.IsOverBlockingUI(pointer)
            };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            EventSystem.current = previous;
        }
    }

    // 同一建筑换世界位置和运行身份后仍得到同一封包，损伤及材料必须保留。
    private static JObject CaptureBuildingSnapshotRepacking(JObject args)
    {
        ItemData source = ProbeMaterialBuilding("Chest_Wood", BuildingRole.PlacedBuilding);
        source.Guid = 123;
        source.Durability = 37f;
        source.MaxDurability = 100f;
        source.transform.position = new Vector3(10f, 20f, 0f);
        source.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        Mod_Building.WriteBuildingData(source, state =>
        {
            state.SnapshotBase64 = "nested-snapshot";
            state.HorizontalMirrorX = true;
            state.CraftingMaterials = new SortedDictionary<string, float> { ["Plank"] = 6f };
        });
        ItemData first = FastCloner.FastCloner.DeepClone(source);
        ItemData second = FastCloner.FastCloner.DeepClone(source);
        second.Guid = 456;
        second.transform.position = new Vector3(-50f, 30f, 0f);
        second.transform.scale = new Vector3(2f, 3f, 1f);
        BuildingSnapshotRepacking.NormalizePlacementIndependentSnapshot(first);
        BuildingSnapshotRepacking.NormalizePlacementIndependentSnapshot(second);
        string encoded = BuildingSnapshotRepacking.EncodeSnapshot(first);
        string other = BuildingSnapshotRepacking.EncodeSnapshot(second);
        string identity = BuildingSnapshotRepacking.CreateStatefulSummonerIdentity(encoded);
        byte[] bytes = Convert.FromBase64String(encoded);
        Mod_Building.TryReadBuildingData(first, out _, out Mod_Building.Building_Data packed);
        Mod_Building.TryReadBuildingData(source, out _, out Mod_Building.Building_Data original);
        second.Durability--;
        string damaged = BuildingSnapshotRepacking.CreateStatefulSummonerIdentity(
            BuildingSnapshotRepacking.EncodeSnapshot(second));
        return new JObject
        {
            ["sameSnapshot"] = encoded == other, ["sameIdentity"] = identity ==
                BuildingSnapshotRepacking.CreateStatefulSummonerIdentity(other),
            ["differentDamageIdentity"] = identity != damaged,
            ["envelope"] = System.Text.Encoding.ASCII.GetString(bytes, 0, 4),
            ["normalized"] = first.Guid == 0 && !first.inHand && first.transform.position == Vector3.zero &&
                               first.transform.rotation == Quaternion.identity && first.transform.scale == Vector3.one,
            ["payloadPreserved"] = first.Durability == 37f && packed.CraftingMaterials["Plank"] == 6f &&
                                     packed.SnapshotBase64 == null && !packed.HorizontalMirrorX,
            ["sourcePreserved"] = source.Guid == 123 && source.transform.position.x == 10f &&
                                    original.SnapshotBase64 == "nested-snapshot" && original.HorizontalMirrorX
        };
    }
    #endregion
}
