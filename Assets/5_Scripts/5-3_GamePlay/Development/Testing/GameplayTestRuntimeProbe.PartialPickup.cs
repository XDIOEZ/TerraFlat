using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestRuntimeProbe
{
    #region 真实轻量掉落物部分拾取采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterPartialPickupRuntime()
        => Register("inventory", "dropped_partial_pickup", CaptureDroppedPartialPickup);

    private static Vector2 FindPartialPickupSite(Player player)
    {
        Vector2Int origin = Vector2Int.FloorToInt(WorldLocalPresentation.ToLogical(player.transform.position));
        for (int radius = 0; radius <= 6; radius++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius) continue;
            Vector2 point = (Vector2)WorldTopologyRuntime.NormalizeCell(origin + new Vector2Int(x, y)) + Vector2.one * .5f;
            bool clear = true;
            for (int dy = -1; clear && dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Vector2 neighbor = WorldTopologyRuntime.NormalizePosition(point + new Vector2(dx, dy));
                if (ChunkMgr.ExistingInstance == null || !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(neighbor, out var sample) ||
                    sample.Cell.BlockingTileId != 0 || sample.LiquidDepth > 0f || MachineWorld.GetTransportAt(neighbor) != null)
                { clear = false; break; }
            }
            if (clear) return point;
        }
        throw new InvalidOperationException("部分拾取采样需要附近已加载、无传送带的干燥空地。");
    }

    // 只用自有拾取器、冷库存与真实掉落运行时，断言和预期数量由 Lua 持有。
    private static JObject CaptureDroppedPartialPickup(Player player, JObject args)
    {
        if (!GameRes.ExistingInstance.TryGetItemDefinition("Log", out RuntimeItemDefinition definition))
            throw new InvalidOperationException("部分拾取采样缺少正式原木定义。");
        var root = new GameObject("[LuaPartialPickup]");
        // 部分拾取运行时需要有效场景，临时拾取器与掉落物统一归属隔离玩家的世界。
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, player.gameObject.scene);
        root.SetActive(false);
        var originalSceneRoots = new HashSet<GameObject>(root.scene.GetRootGameObjects());
        DroppedItemRuntime runtime = null;
        Mod_ItemPicker picker = null;
        try
        {
            Vector2 position = FindPartialPickupSite(player);
            root.transform.position = WorldLocalPresentation.ProjectPosition(position);
            var owner = root.AddComponent<GameItem>();
            owner.BindData(new Data_GeneralItem
            {
                IDName = "test:partial_pickup_owner",
                Stack = new ItemStack { CanBePickedUp = false }
            });
            var collider = root.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one * 8f;
            Inventory hotbar = new()
            {
                Data = new Inventory_Data(new List<ItemSlot> { new(0) { SlotMaxVolume = 2f } }, ModText.Hotbar)
            };
            Inventory bag = new()
            {
                Data = new Inventory_Data(new List<ItemSlot> { new(0) }, ModText.Bag)
            };
            bag.Data.SetCarryCapacity(1000f, (float)GameplayTestSystemProbe.Number(args, "maxVolume", 36f));
            var targets = root.AddComponent<Mod_Inventory>();
            targets.InventoryInstances = new List<Inventory> { hotbar };
            var bagTargets = root.AddComponent<Mod_Inventory>();
            bagTargets.InventoryInstances = new List<Inventory> { bag };
            picker = root.AddComponent<Mod_ItemPicker>();
            picker.ModuleInit(owner, new Ex_ModData_MemoryPackable { Name = "test.partial.picker", ID = ModText.Picker, Enabled = true });
            picker.AddTargetInventoryModules.Add(targets);
            picker.AddTargetInventoryModules.Add(bagTargets);
            root.SetActive(true);
            picker.Load();
            picker.PickupAmountMultiplier = 1;
            Physics2D.SyncTransforms();

            ItemData payload = definition.CreateItemData();
            payload.Guid = Guid.NewGuid().GetHashCode();
            if (payload.Guid == 0) payload.Guid = 1;
            payload.Stack.Amount = 10f;
            payload.Stack.CanBePickedUp = true;
            int id = payload.Guid;
            runtime = new DroppedItemRuntime(root.scene, null);
            runtime.Add(payload, new LightweightDroppedBody
            {
                Id = id, Amount = payload.Stack.Amount, Position = position,
                Scale = Vector2.one, Pickable = 1
            });
            var pickers = new[] { picker };
            float Count() => (hotbar.Data.GetItemSlot(0).itemData?.Stack.Amount ?? 0f) +
                (bag.Data.GetItemSlot(0).itemData?.Stack.Amount ?? 0f);
            JObject Snapshot()
            {
                List<DroppedItemSaveRecord> records = runtime.Capture();
                DroppedItemSaveRecord record = records.Find(entry => entry.Data.Guid == id);
                return new JObject
                {
                    ["inventoryAmount"] = Count(), ["remaining"] = record?.Data.Stack.Amount ?? 0f,
                    ["worldRecords"] = records.Count, ["sameGuid"] = record != null && record.Data.Guid == id,
                    ["unitWeight"] = payload.Stack.Weight, ["unitVolume"] = payload.Stack.Volume,
                    ["hotbarAmount"] = hotbar.Data.GetItemSlot(0).itemData?.Stack.Amount ?? 0f,
                    ["bagAmount"] = bag.Data.GetItemSlot(0).itemData?.Stack.Amount ?? 0f
                };
            }

            runtime.Tick(.11f, pickers);
            var result = new JObject { ["first"] = Snapshot() };
            runtime.Tick(1.1f, pickers);
            result["afterMatter"] = Snapshot();
            if (GameplayTestSystemProbe.Flag(args, "retry", false))
            {
                ItemSlot hotbarSlot = hotbar.Data.GetItemSlot(0);
                ItemSlot bagSlot = bag.Data.GetItemSlot(0);
                float originalHotbar = hotbarSlot.itemData?.Stack.Amount ?? 0f;
                float originalBag = bagSlot.itemData?.Stack.Amount ?? 0f;
                bag.Data.TrySetSlotItemAmount(bagSlot, bagSlot.itemData, 0f);
                ulong before = picker.PickupCapacityRevision;
                hotbarSlot.itemData.Stack.Amount = 1f;
                result["inPlaceInvalidated"] = picker.PickupCapacityRevision != before;
                result["removed"] = originalBag + originalHotbar - 1f;
                runtime.Tick(.11f, pickers);
                result["afterFreeCapacity"] = Snapshot();
            }
            return result;
        }
        finally
        {
            try { picker?.Unload(); }
            finally
            {
                try { runtime?.Dispose(); }
                finally
                {
                    // 同步夹具结束会停掉自有协程，只清理本次新建的反馈根，保留原场景已有对象。
                    foreach (GameObject candidate in root.scene.GetRootGameObjects())
                        if (candidate != null && !originalSceneRoots.Contains(candidate) && candidate.name == "轻量掉落物_拾取反馈")
                            UnityEngine.Object.DestroyImmediate(candidate);
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }
    }

    #endregion
}
