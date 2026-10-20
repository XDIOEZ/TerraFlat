using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>真实流程采样仅在正式 Lua 场景的隔离存档世界内执行。</summary>
public static partial class GameplayTestRuntimeProbe
{
    #region 真实流程操作登记

    private static readonly Dictionary<string, Func<Player, JObject, JObject>> operations = new(StringComparer.Ordinal)
    {
        ["testing/resource_cache"] = (_, _) => GameplayTestResourceCache.Sample(),
        ["core_lifecycle/live_entry"] = CaptureLiveEntry,
        ["player/interaction_input_reenable"] = CaptureInteractionInputReenable,
        ["inventory/creative_snapshot"] = CaptureCreativeInventory,
        ["inventory/slot_bounds"] = CaptureInventorySlotBounds,
        ["inventory/creative_begin"] = BeginCreativeInventoryScope,
        ["inventory/creative_restore"] = RestoreCreativeInventoryScope,
        ["inventory/direct_drag"] = CaptureInventoryDirectDrag,
        ["inventory/flick_transfer"] = CaptureInventoryFlickTransfer,
        ["inventory/directional_flick"] = CaptureInventoryDirectionalFlick,
        ["inventory/quick_transfer_feedback"] = CaptureInventoryQuickTransferFeedback,
        ["inventory/equipment_drag"] = CaptureInventoryEquipmentDrag,
        ["inventory/equipment_configuration"] = CaptureEquipmentConfiguration,
        ["containers/whole_pour"] = CaptureVesselWholePour,
        ["world_streaming/live_window"] = CaptureLiveWindow,
        ["save/isolated_roundtrip"] = CaptureIsolatedRoundtrip,
        ["industrial_fluids/selector_routes"] = GameplayTestFluidNetworks.CaptureSelectorRoutes,
        ["industrial_fluids/phase_preference"] = GameplayTestFluidNetworks.CapturePhasePreference,
        ["industrial_fluids/tank_ownership"] = GameplayTestFluidNetworks.CaptureTankOwnership,
        ["industrial_fluids/ground_scope_roundtrip"] = GameplayTestFluidNetworks.CaptureGroundScopeRoundtrip,
        ["industrial_fluids/reaction_capacity"] = GameplayTestFluidDevices.CaptureReactionCapacity,
        ["industrial_fluids/residue_transfer"] = GameplayTestFluidDevices.CaptureResidueTransfer,
        ["industrial_fluids/liquid_pump_flow"] = GameplayTestFluidDevices.CaptureLiquidPump,
        ["industrial_fluids/surface_pump_flow"] = GameplayTestFluidSurface.CapturePumpFlow,
        ["industrial_fluids/surface_outlet_flow"] = GameplayTestFluidSurface.CaptureOutletFlow,
        ["industrial_fluids/surface_outlet_rate"] = GameplayTestFluidSurface.CaptureOutletRate
    };

    public static void Register(string systemId, string operationId, Func<Player, JObject, JObject> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        string key = Key(systemId, operationId);
        if (asyncOperations.ContainsKey(key)) throw new InvalidOperationException("同步与跨帧操作重复：" + key);
        if (operations.TryGetValue(key, out var previous))
        {
            if (previous.Method == handler.Method && Equals(previous.Target, handler.Target)) return;
            throw new InvalidOperationException("真实流程测试操作重复登记：" + key);
        }
        operations.Add(key, handler);
    }

    public static bool Supports(string systemId, string operationId) => operations.ContainsKey(Key(systemId, operationId)) ||
        asyncOperations.ContainsKey(Key(systemId, operationId));

    public static JObject Capture(Player player, string systemId, string operationId, JObject arguments)
    {
        if (!operations.TryGetValue(Key(systemId, operationId), out var operation))
            throw new ArgumentException("未登记的真实流程测试操作：" + systemId + "/" + operationId);
        RequireIsolatedSaveRoot(player);
        JObject result = operation(player, arguments == null ? new JObject() : (JObject)arguments.DeepClone());
        if (result == null) throw new InvalidOperationException("真实流程测试未返回采样。");
        result["_system"] = systemId;
        result["_operation"] = operationId;
        result["scope"] = "live-isolated-world";
        return result;
    }

    private static string Key(string system, string operation)
    {
        foreach (string value in new[] { system, operation })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80) throw new ArgumentException("测试操作 ID 无效。");
            foreach (char character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= '0' && character <= '9') && character != '_')
                    throw new ArgumentException("测试操作 ID 含非法字符。");
        }
        return system + "/" + operation;
    }

    #endregion

    #region 交互按键停用恢复采样

    // 用独立虚拟键盘走真实 F 回调，检查临时停用后的恢复与重复订阅。
    private static JObject CaptureInteractionInputReenable(Player player, JObject args)
    {
        Mod_InteractSender sender = player.GetComponentInChildren<Mod_InteractSender>(true);
        Mod_GameController controller = player.GetComponentInChildren<Mod_GameController>(true);
        if (sender == null || !sender.isActiveAndEnabled || !sender.IsRuntimeLoaded ||
            controller == null || controller.IsGameplayInputLocked)
            throw new InvalidOperationException("交互按键回归需要已加载且未锁定的正式交互模块。");
        var action = controller._inputActions.Win10.E;
        var host = new GameObject("[InteractionInputReenable]") { hideFlags = HideFlags.HideAndDontSave };
        GameplayTestSpatialInteractionTarget target = host.AddComponent<GameplayTestSpatialInteractionTarget>();
        UnityEngine.InputSystem.Keyboard keyboard = null;
        int bindingIndex = action.bindings.Count;
        bool bindingAdded = false;
        try
        {
            host.transform.position = player.transform.position;
            target.Allowed = true;
            SpatialInteractionRegistry.Register(target, float.MaxValue);
            keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            UnityEngine.InputSystem.InputSystem.AddDeviceUsage(keyboard, Mod_GameController.ExternalGameplayInputDeviceUsage);
            UnityEngine.InputSystem.InputSystem.AddDeviceUsage(keyboard, "FlatWorldInteractionRegression");
            action.AddBinding("<Keyboard>{FlatWorldInteractionRegression}/f");
            bindingAdded = true;

            void PressAndRelease()
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
                    new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.F));
                UnityEngine.InputSystem.InputSystem.Update();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
                    default(UnityEngine.InputSystem.LowLevel.KeyboardState));
                UnityEngine.InputSystem.InputSystem.Update();
            }

            PressAndRelease();
            int initialStarts = target.StartCount, initialEnds = target.EndCount;
            sender.enabled = false;
            PressAndRelease();
            int disabledStarts = target.StartCount;
            var rounds = new JArray();
            for (int i = 0; i < 3; i++)
            {
                sender.enabled = true;
                int beforeStart = target.StartCount, beforeEnd = target.EndCount;
                PressAndRelease();
                rounds.Add(new JObject
                {
                    ["starts"] = target.StartCount - beforeStart, ["ends"] = target.EndCount - beforeEnd,
                    ["held"] = sender.HasHeldInteraction
                });
                sender.enabled = false;
            }
            return new JObject
            {
                ["initialStarts"] = initialStarts, ["initialEnds"] = initialEnds,
                ["disabledStarts"] = disabledStarts, ["rounds"] = rounds
            };
        }
        finally
        {
            if (keyboard != null) UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            if (bindingAdded) action.ChangeBinding(bindingIndex).Erase();
            sender.CancelCurrentInteraction();
            sender.enabled = true;
            SpatialInteractionRegistry.Unregister(target);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    #endregion

    #region 正式玩家背包采样

    // 只读取隔离玩家的正式背包状态，由 Lua 检查动态槽位及重复填充后的物品守恒。
    private static JObject CaptureCreativeInventory(Player player, JObject args)
    {
        Inventory_Data data = player.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag)
            ?.GetDefaultTargetInventory()?.Data;
        if (data?.itemSlots == null)
            throw new InvalidOperationException("正式玩家背包尚未加载，无法采样创造背包。");

        var items = new JArray();
        for (int i = 0; i < data.itemSlots.Count; i++)
        {
            ItemData item = data.itemSlots[i]?.itemData;
            if (item == null) continue;
            items.Add(new JObject
            {
                ["slot"] = i,
                ["id"] = item.IDName,
                ["guid"] = item.Guid,
                ["hasStack"] = item.Stack != null,
                ["amount"] = item.Stack?.Amount ?? 0f,
                ["canBePickedUp"] = item.Stack?.CanBePickedUp ?? false
            });
        }

        return new JObject
        {
            ["slotCount"] = data.itemSlots.Count,
            ["unlimitedSlots"] = data.HasUnlimitedSlots,
            ["unlimitedCarry"] = data.HasUnlimitedCarryCapacity,
            ["unlimitedStack"] = data.HasUnlimitedStackSize,
            ["items"] = items
        };
    }

    private static JObject CaptureInventorySlotBounds(Player player, JObject args)
    {
        Inventory_Data data = player.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag)
            ?.GetDefaultTargetInventory()?.Data;
        if (data?.itemSlots == null || data.itemSlots.Count == 0)
            throw new InvalidOperationException("缺少已加载的正式玩家背包槽位。");

        var empty = new Inventory_Data(new List<ItemSlot>(), "lua-inventory-empty");
        return new JObject
        {
            ["validIndexPreserved"] = data.itemSlots[0] != null &&
                ReferenceEquals(data.GetItemSlot(0), data.itemSlots[0]),
            ["negativeRejected"] = data.GetItemSlot(-1) == null,
            ["beyondRejected"] = data.GetItemSlot(data.itemSlots.Count) == null,
            ["emptyRejected"] = empty.GetItemSlot(0) == null
        };
    }

    private static CreativeInventoryScope activeCreativeInventoryScope;

    // 创造背包会给角色增加数百种物品，必须在本 Lua 结束前归还原库存，避免虚增后续飞船载荷。
    private sealed class CreativeInventoryScope : IDisposable
    {
        private readonly Player player;
        private readonly Inventory inventory;
        private readonly Inventory_Data data;
        private readonly InventoryInstanceSnapshot snapshot;
        private readonly string playerSpecialData;
        private readonly bool originalSlots, originalCarry, originalStack;
        public readonly int OriginalSlotCount;
        public readonly float OriginalWeight;
        public bool Restored { get; private set; }

        public CreativeInventoryScope(Player player, Inventory inventory)
        {
            this.player = player;
            this.inventory = inventory;
            data = inventory.Data ?? throw new InvalidOperationException("创造背包现场缺少原始库存数据。");
            snapshot = InventoryInstanceSnapshot.Capture(data);
            playerSpecialData = player.Data.ItemSpecialData;
            originalSlots = data.HasUnlimitedSlots;
            originalCarry = data.HasUnlimitedCarryCapacity;
            originalStack = data.HasUnlimitedStackSize;
            OriginalSlotCount = data.itemSlots.Count;
            OriginalWeight = data.CurrentCarryWeight;
        }

        public void Dispose()
        {
            if (Restored) return;
            try
            {
                if (player == null || inventory == null || !ReferenceEquals(inventory.Data, data))
                    throw new InvalidOperationException("创造背包测试期间原库存引用被替换。");
                data.SetUnlimitedSlots(false);
                snapshot.RestoreTo(data, item => item.SharedConfiguration == null
                    ? ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, item) : item);
                if (data.itemSlots.Count > OriginalSlotCount)
                    data.itemSlots.RemoveRange(OriginalSlotCount, data.itemSlots.Count - OriginalSlotCount);
                player.Data.ItemSpecialData = playerSpecialData;
                data.SetUnlimitedCarryCapacity(originalCarry);
                data.SetUnlimitedStackSize(originalStack);
                data.SetUnlimitedSlots(originalSlots);
                data.NotifySlotsChanged();
                inventory.RefreshUI();
                Restored = true;
            }
            finally
            {
                if (ReferenceEquals(activeCreativeInventoryScope, this)) activeCreativeInventoryScope = null;
            }
        }

        public JObject Result() => new()
        {
            ["restored"] = Restored,
            ["slotCount"] = data.itemSlots?.Count ?? 0,
            ["originalSlotCount"] = OriginalSlotCount,
            ["weight"] = data.CurrentCarryWeight,
            ["originalWeight"] = OriginalWeight,
            ["capacityRestored"] = data.HasUnlimitedSlots == originalSlots &&
                data.HasUnlimitedCarryCapacity == originalCarry && data.HasUnlimitedStackSize == originalStack,
            ["playerStateRestored"] = player.Data.ItemSpecialData == playerSpecialData
        };
    }

    private static JObject BeginCreativeInventoryScope(Player player, JObject args)
    {
        if (activeCreativeInventoryScope != null)
            throw new InvalidOperationException("同一 Lua 用例重复登记创造背包现场。");
        Inventory inventory = player.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag)?.GetDefaultTargetInventory();
        if (inventory?.Data == null || inventory.Data.itemSlots == null)
            throw new InvalidOperationException("正式玩家背包尚未加载。");
        var scope = new CreativeInventoryScope(player, inventory);
        RegisterCleanup(scope);
        activeCreativeInventoryScope = scope;
        return new JObject { ["registered"] = true, ["originalSlotCount"] = scope.OriginalSlotCount,
            ["originalWeight"] = scope.OriginalWeight };
    }

    private static JObject RestoreCreativeInventoryScope(Player player, JObject args)
    {
        CreativeInventoryScope scope = activeCreativeInventoryScope ??
            throw new InvalidOperationException("创造背包测试没有登记恢复现场。");
        scope.Dispose();
        return scope.Result();
    }

    #endregion

    #region 正式库存拖拽与装备接收现场

    // 独立测试库存使用正式物品定义和正式拖拽入口，不改动隔离玩家的背包及装备。
    private static Inventory CreateDragInventory(string name, params ItemData[] items)
    {
        var slots = new List<ItemSlot>(items.Length);
        for (int i = 0; i < items.Length; i++)
            slots.Add(new ItemSlot(i) { itemData = items[i] });
        return new Inventory { Data = new Inventory_Data(slots, name) };
    }

    private static ItemData CreateDragItem(string id, float amount = 1f)
    {
        ItemData item = GameRes.ExistingInstance.CreateItemData(id);
        if (item?.Stack == null) throw new InvalidOperationException("拖拽测试缺少正式物品堆叠：" + id);
        item.Stack.Amount = amount;
        return item;
    }

    private static JObject CaptureDragSlot(Inventory inventory, int index)
    {
        ItemData item = inventory.Data.GetItemSlot(index)?.itemData;
        return new JObject
        {
            ["id"] = item?.IDName ?? string.Empty,
            ["guid"] = item?.Guid ?? 0,
            ["amount"] = item?.Stack?.Amount ?? 0f
        };
    }

    private static JObject CaptureInventoryDirectDrag(Player player, JObject args)
    {
        Inventory source = CreateDragInventory("lua.drag.source", CreateDragItem("Twine", 6),
            CreateDragItem("Rope", 2), CreateDragItem("Twine", 3), CreateDragItem("Twine", 2));
        Inventory target = CreateDragInventory("lua.drag.target", null, CreateDragItem("Twine", 4), null, null);
        target.Data.itemSlots[2].CanAcceptTags.Add(EquipmentSlotCatalog.HeadTag);
        target.Data.itemSlots[3].SlotMaxVolume = 1f;

        var direct = new JObject { ["sourceBefore"] = CaptureDragSlot(source, 0) };
        InventoryDragTransaction move = source.OnMouseDragBegin(0);
        direct["dragStarted"] = move != null;
        direct["lockedBefore"] = source.IsSlotBeingDragged(0);
        direct["negativeAccepted"] = target.OnMouseDragDrop(-1, move);
        direct["afterNegative"] = CaptureDragSlot(source, 0);
        direct["moved"] = target.OnMouseDragDrop(0, move);
        direct["sourceAfter"] = CaptureDragSlot(source, 0);
        direct["targetAfter"] = CaptureDragSlot(target, 0);
        direct["reusedAccepted"] = target.OnMouseDragDrop(1, move);
        move?.Complete(false);
        direct["lockedAfter"] = source.HasActiveSlotDrag;

        InventoryDragTransaction merge = source.OnMouseDragBegin(2);
        var merged = new JObject { ["targetBefore"] = CaptureDragSlot(target, 1),
            ["sourceBefore"] = CaptureDragSlot(source, 2),
            ["accepted"] = target.OnMouseDragDrop(1, merge) };
        merge?.Complete(false);
        merged["targetAfter"] = CaptureDragSlot(target, 1);
        merged["sourceAfter"] = CaptureDragSlot(source, 2);

        InventoryDragTransaction rejected = source.OnMouseDragBegin(1);
        var blocked = new JObject { ["sourceBefore"] = CaptureDragSlot(source, 1),
            ["accepted"] = target.OnMouseDragDrop(2, rejected),
            ["lockedDuring"] = source.IsSlotBeingDragged(1) };
        rejected?.Complete(false);
        blocked["sourceAfter"] = CaptureDragSlot(source, 1);
        blocked["targetAfter"] = CaptureDragSlot(target, 2);
        blocked["lockedAfter"] = source.HasActiveSlotDrag;

        InventoryDragTransaction partialDrag = source.OnMouseDragBegin(3);
        var partial = new JObject { ["sourceBefore"] = CaptureDragSlot(source, 3),
            ["accepted"] = target.OnMouseDragDrop(3, partialDrag) };
        partialDrag?.Complete(false);
        partial["sourceAfter"] = CaptureDragSlot(source, 3);
        partial["targetAfter"] = CaptureDragSlot(target, 3);

        InventoryDragTransaction staleDrag = source.OnMouseDragBegin(1);
        source.Data.SetOne_ItemData(1, CreateDragItem("Ore_Stone", 1));
        var stale = new JObject { ["accepted"] = target.OnMouseDragDrop(0, staleDrag) };
        staleDrag?.Complete(false);
        stale["sourceAfter"] = CaptureDragSlot(source, 1);
        stale["targetAfter"] = CaptureDragSlot(target, 0);
        stale["lockedAfter"] = source.HasActiveSlotDrag;

        return new JObject { ["direct"] = direct, ["merged"] = merged,
            ["blocked"] = blocked, ["partial"] = partial, ["stale"] = stale };
    }

    // 使用自有库存验证甩动的来源校验和正式快速转移，不改隔离玩家物品。
    private static JObject CaptureInventoryFlickTransfer(Player player, JObject args)
    {
        Inventory source = CreateDragInventory("lua.flick.source", CreateDragItem("Twine", 6));
        Inventory target = CreateDragInventory("lua.flick.target", new ItemData[1]);
        target.Data.itemSlots[0].SlotMaxVolume = (float)GameplayTestSystemProbe.Number(args, "capacity", 10d);
        InventoryDragTransaction drag = source.OnMouseDragBegin(0);
        int attempts = 0;
        bool moved = false;
        try
        {
            if (GameplayTestSystemProbe.Flag(args, "stale", false))
                source.Data.SetOne_ItemData(0, CreateDragItem("Ore_Stone"));
            bool consumed = drag.TryConsumeSourceItem(item =>
            {
                if (!ReferenceEquals(source.Data.itemSlots[0].itemData, item)) return false;
                attempts++;
                moved = source.ExecuteMachineTransfer(0, target, -1, "quick", 0);
                return true;
            });
            bool reused = drag.TryConsumeSourceItem(_ => { attempts++; return true; });
            drag.Complete(false);
            return new JObject { ["consumed"] = consumed, ["moved"] = moved, ["attempts"] = attempts,
                ["source"] = CaptureDragSlot(source, 0), ["target"] = CaptureDragSlot(target, 0),
                ["reused"] = reused, ["dragLocked"] = source.HasActiveSlotDrag };
        }
        finally { drag?.Complete(false); }
    }

    // 只采集正式快速转移的提交反馈，Lua检查分堆、失败和数据守恒。
    private static JObject CaptureInventoryQuickTransferFeedback(Player player, JObject args)
    {
        Inventory source = CreateDragInventory("lua.quick.feedback.source", CreateDragItem("Twine", 6));
        Inventory target = CreateDragInventory("lua.quick.feedback.target", CreateDragItem("Twine", 3), null, null);
        target.Data.itemSlots[0].SlotMaxVolume = 4;
        target.Data.itemSlots[1].SlotMaxVolume = 2;
        target.Data.itemSlots[2].SlotMaxVolume = 0;
        var receipts = new JArray();
        var suppressedReceipts = new JArray();
        Inventory remoteSource = CreateDragInventory("lua.quick.feedback.remote", CreateDragItem("Twine", 2));
        Inventory remoteTarget = CreateDragInventory("lua.quick.feedback.remote-target", new ItemData[1]);
        void Capture(FlatWorld.Gameplay.InventoryFeedback.InventoryQuickTransferSignal signal)
        {
            if (ReferenceEquals(signal.Source, remoteSource))
            {
                suppressedReceipts.Add(new JObject { ["allowLocalPresentation"] = signal.AllowLocalPresentation,
                    ["amount"] = signal.Amount });
                return;
            }
            if (!ReferenceEquals(signal.Source, source)) return;
            receipts.Add(new JObject { ["sourceIndex"] = signal.SourceIndex, ["targetIndex"] = signal.TargetIndex,
                ["amount"] = signal.Amount, ["targetMatched"] = ReferenceEquals(signal.Target, target),
                ["itemId"] = signal.Item.IDName, ["allowLocalPresentation"] = signal.AllowLocalPresentation,
                ["sourceAtEvent"] = CaptureDragSlot(source, 0),
                ["targetAtEvent"] = CaptureDragSlot(target, signal.TargetIndex) });
        }
        FlatWorld.Gameplay.InventoryFeedback.InventoryQuickTransferFeedbackEvents.Committed += Capture;
        try
        {
            bool moved = source.ExecuteMachineTransfer(0, target, -1, "quick", 0);
            int countAfterSuccess = receipts.Count;
            bool fullAccepted = source.ExecuteMachineTransfer(0, target, -1, "quick", 0);
            int countAfterFull = receipts.Count;
            bool invalidAccepted = source.ExecuteMachineTransfer(-1, target, -1, "quick", 0);
            bool remoteMoved;
            using (FlatWorld.Gameplay.InventoryFeedback.InventoryQuickTransferFeedbackEvents.SuppressLocalPresentation())
                remoteMoved = remoteSource.ExecuteMachineTransfer(0, remoteTarget, -1, "quick", 0);
            return new JObject { ["moved"] = moved, ["fullAccepted"] = fullAccepted,
                ["remoteMoved"] = remoteMoved, ["suppressedReceipts"] = suppressedReceipts,
                ["invalidAccepted"] = invalidAccepted, ["receipts"] = receipts,
                ["countAfterSuccess"] = countAfterSuccess, ["countAfterFull"] = countAfterFull,
                ["source"] = CaptureDragSlot(source, 0), ["first"] = CaptureDragSlot(target, 0),
                ["second"] = CaptureDragSlot(target, 1), ["third"] = CaptureDragSlot(target, 2) };
        }
        finally { FlatWorld.Gameplay.InventoryFeedback.InventoryQuickTransferFeedbackEvents.Committed -= Capture; }
    }

    private static JObject CaptureInventoryEquipmentDrag(Player player, JObject args)
    {
        Inventory source = CreateDragInventory("lua.gear.source", CreateDragItem("Spacesuit_Head"),
            CreateDragItem("Spacesuit_Torso"), CreateDragItem("Spacesuit_Head"));
        var equipment = new Inventory_Equipment();
        equipment.EnsureSlotSchema();
        int head = equipment.GetSlotIndex(EquipmentSlotCatalog.Head);
        int torso = equipment.GetSlotIndex(EquipmentSlotCatalog.TorsoOuter);
        var result = new JObject
        {
            ["headIndex"] = head, ["torsoIndex"] = torso,
            ["headAllowed"] = equipment.CanEquipAt(head, source.Data.GetItemSlot(0)?.itemData),
            ["torsoAllowedAtHead"] = equipment.CanEquipAt(head, source.Data.GetItemSlot(1)?.itemData),
            ["headAllowedAtTorso"] = equipment.CanEquipAt(torso, source.Data.GetItemSlot(0)?.itemData),
            ["headBefore"] = CaptureDragSlot(source, 0)
        };

        InventoryDragTransaction wrong = source.OnMouseDragBegin(1);
        result["wrongAccepted"] = equipment.OnMouseDragDrop(head, wrong);
        wrong?.Complete(false);
        result["wrongSourceAfter"] = CaptureDragSlot(source, 1);
        result["wrongTargetAfter"] = CaptureDragSlot(equipment, head);

        InventoryDragTransaction valid = source.OnMouseDragBegin(0);
        result["equipAccepted"] = equipment.OnMouseDragDrop(head, valid);
        valid?.Complete(false);
        result["headAfterEquip"] = CaptureDragSlot(equipment, head);
        result["sourceAfterEquip"] = CaptureDragSlot(source, 0);

        source.Data.itemSlots[2].CanAcceptTags.Add(EquipmentSlotCatalog.TorsoOuterTag);
        InventoryDragTransaction swap = source.OnMouseDragBegin(2);
        result["reverseSwapAccepted"] = equipment.OnMouseDragDrop(head, swap);
        swap?.Complete(false);
        result["headAfterSwap"] = CaptureDragSlot(equipment, head);
        result["swapSourceAfter"] = CaptureDragSlot(source, 2);

        result["invalidIndexAccepted"] = source.TryMoveSlotTo(1, equipment, -1);
        InventoryDragTransaction unequip = equipment.OnMouseDragBegin(head);
        result["unequipAccepted"] = source.OnMouseDragDrop(0, unequip);
        unequip?.Complete(false);
        result["sourceAfterUnequip"] = CaptureDragSlot(source, 0);
        result["headAfterUnequip"] = CaptureDragSlot(equipment, head);
        result["sourceDragReleased"] = !source.HasActiveSlotDrag;
        result["equipmentDragReleased"] = !equipment.HasActiveSlotDrag;
        return result;
    }

    #endregion

    #region 正式装备配置入口现场

    // 夹具只暂存隔离玩家原装备，配置目标始终由正式装备效果和公开入口生成。
    private static JObject CaptureEquipmentConfiguration(Player player, JObject args)
    {
        Mod_Equipment equipment = player.itemMods?.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
        Inventory_Equipment inventory = equipment?.EquipmentInventory;
        if (inventory?.Data?.itemSlots == null)
            throw new InvalidOperationException("装备配置测试缺少正式装备模块。");

        var original = new List<(string Slot, ItemData Item)>();
        foreach (EquipmentSlotRule rule in inventory.SlotRules)
            original.Add((rule.Id, equipment.GetEquippedItem(rule.Id)));
        ItemData head = CreateDragItem("Spacesuit_Head");
        ItemData first = CreateDragItem("Spacesuit_Torso");
        ItemData second = CreateDragItem("Spacesuit_Torso");
        second.Tags = new List<string>(second.Tags) { EquipmentSlotCatalog.AnyTag };
        Inventory holding = CreateDragInventory("lua.gear.configuration.holding", new ItemData[original.Count + 3]);
        Inventory returns = CreateDragInventory("lua.gear.configuration.returns", new ItemData[original.Count]);
        int headSource = original.Count, firstSource = headSource + 1, secondSource = headSource + 2;
        holding.Data.SetOne_ItemData(headSource, head);
        holding.Data.SetOne_ItemData(firstSource, first);
        holding.Data.SetOne_ItemData(secondSource, second);
        int headSlot = inventory.GetSlotIndex(EquipmentSlotCatalog.Head);
        int torsoSlot = inventory.GetSlotIndex(EquipmentSlotCatalog.TorsoOuter);
        int secondSlot = inventory.GetSlotIndex(EquipmentSlotCatalog.Hands);
        var targets = new List<EquipmentInstance> { new EquipmentInstance_SpacesuitSupport() };
        var result = new JObject();
        try
        {
            for (int index = 0; index < original.Count; index++)
                if (original[index].Item != null && !inventory.TryMoveSlotTo(
                    inventory.GetSlotIndex(original[index].Slot), holding, index))
                    throw new InvalidOperationException("装备配置测试无法暂存原装备：" + original[index].Slot);
            equipment.CollectConfigurationTargets(targets);
            result["emptyCount"] = targets.Count;
            GameObject equipmentPrefab = GameRes.ExistingInstance.GetPrefab("UI_Equipment");
            UnityEngine.UI.Button[] buttons = equipmentPrefab.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            result["genericConfigurationButtons"] = buttons.Count(button => button.name == "FWUI_ConfigureEquipment");
            result["specializedConfigurationButtons"] = buttons.Count(button => button.name == "FWUI_ConfigureSpacesuit");
            result["nullRejected"] = !equipment.TryOpenConfiguration(null);
            result["foreignRejected"] = !equipment.TryOpenConfiguration(new EquipmentInstance_SpacesuitSupport());

            if (!holding.TryMoveSlotTo(headSource, inventory, headSlot))
                throw new InvalidOperationException("装备配置测试无法穿戴正式头盔。");
            equipment.CollectConfigurationTargets(targets);
            result["ordinaryCount"] = targets.Count;
            if (!holding.TryMoveSlotTo(firstSource, inventory, torsoSlot))
                throw new InvalidOperationException("装备配置测试无法穿戴正式宇航服。");
            equipment.CollectConfigurationTargets(targets);
            result["singleCount"] = targets.Count;
            EquipmentInstance originalTarget = targets.Find(target => ReferenceEquals(target.EquippedItem, first));
            result["singleItemMatched"] = originalTarget != null;
            result["singleOpened"] = equipment.TryOpenConfiguration(originalTarget);
            SpacesuitPanelSession.CloseFor(first);

            result["unequipped"] = inventory.TryMoveSlotTo(torsoSlot, holding, firstSource) &&
                ReferenceEquals(holding.Data.GetItemSlot(firstSource)?.itemData, first);
            result["staleRejected"] = !equipment.TryOpenConfiguration(originalTarget);
            equipment.CollectConfigurationTargets(targets);
            result["afterUnequipCount"] = targets.Count;

            // 为同一件夹具装备增加第二个配置效果，验证通用收集器按物品引用去重。
            Ex_ModData_MemoryPackable store = FluidTankStorage.FindBinary(first, ModText.Equipment_Store) ??
                throw new InvalidOperationException("正式宇航服缺少装备效果存储。");
            List<EquipmentInstance> effects = null;
            store.ReadData(ref effects);
            if (effects == null || !effects.Any(effect => effect is IEquipmentConfiguration))
                throw new InvalidOperationException("正式宇航服没有保存可配置装备效果。");
            effects.Add(new EquipmentInstance_SpacesuitSupport());
            store.WriteData(effects);
            if (!holding.TryMoveSlotTo(firstSource, inventory, torsoSlot) ||
                !holding.TryMoveSlotTo(secondSource, inventory, secondSlot))
                throw new InvalidOperationException("装备配置测试无法准备两个独立配置目标。");
            equipment.CollectConfigurationTargets(targets);
            result["multipleCount"] = targets.Count;
            result["firstItemCount"] = targets.Count(target => ReferenceEquals(target.EquippedItem, first));
            result["secondItemCount"] = targets.Count(target => ReferenceEquals(target.EquippedItem, second));
            result["firstConfigurationEffects"] = equipment.EnumerateEquippedInstances().Count(effect =>
                ReferenceEquals(effect.EquippedItem, first) && effect is IEquipmentConfiguration);
            result["reequippedOldTargetRejected"] = !equipment.TryOpenConfiguration(originalTarget);
            EquipmentInstance secondTarget = targets.Find(target => ReferenceEquals(target.EquippedItem, second));
            result["secondOpened"] = equipment.TryOpenConfiguration(secondTarget);
            SpacesuitPanelSession.CloseFor(second);
        }
        finally
        {
            SpacesuitPanelSession.CloseFor(first);
            SpacesuitPanelSession.CloseFor(second);
            bool restored = true;
            for (int index = 0; index < original.Count; index++)
            {
                var entry = original[index];
                ItemData current = equipment.GetEquippedItem(entry.Slot);
                if (ReferenceEquals(current, entry.Item)) continue;
                int slot = inventory.GetSlotIndex(entry.Slot);
                if (current != null && !inventory.TryMoveSlotTo(slot, returns, index))
                { restored = false; continue; }
                if (entry.Item != null && (!ReferenceEquals(holding.Data.GetItemSlot(index)?.itemData, entry.Item) ||
                    !holding.TryMoveSlotTo(index, inventory, slot))) restored = false;
            }
            if (!restored)
                throw new InvalidOperationException("装备配置测试无法归还原装备。");
        }
        result["originalEquipmentRestored"] = original.All(entry =>
            ReferenceEquals(equipment.GetEquippedItem(entry.Slot), entry.Item));
        return result;
    }

    #endregion

    #region 隔离边界

    public static string RequireIsolatedSaveRoot(Player player)
    {
        if (!Application.isEditor || !Application.isPlaying || player == null || !player.IsLocalProfile)
            throw new InvalidOperationException("真实流程测试只允许 Editor PlayMode 中已进入隔离世界的本地测试角色。");
        SaveDataMgr saves = SaveDataMgr.TestExistingInstance;
        if (saves == null || saves.SaveData == null || string.IsNullOrWhiteSpace(saves.UserSavePath))
            throw new InvalidOperationException("测试存档管理器或目录未就绪。");
        string projectRoot = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
        string boundary = Path.GetFullPath(Path.Combine(projectRoot,
            "Library", "FlatWorldGameplayTests", "Saves")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = Path.GetFullPath(saves.UserSavePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!root.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("拒绝访问正式存档：当前目录不属于 Library 测试隔离目录。");
        string runId = Path.GetRelativePath(boundary, root);
        GameplayTestSelection.ValidateRunId(runId);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("本轮测试隔离目录不存在。");
        string cursor = root;
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("测试写盘目录不允许符号链接或目录联接。");
            if (string.Equals(cursor, projectRoot, StringComparison.OrdinalIgnoreCase)) break;
            cursor = Path.GetDirectoryName(cursor);
        }
        if (!saves.SaveData.PlayerData_Dict.TryGetValue(player.ProfileName, out Data_Player stored) ||
            !ReferenceEquals(stored, player.Data))
            throw new InvalidOperationException("当前角色不属于本轮测试存档。");
        return root;
    }

    #endregion

    #region 正式入世与流送观察

    private static JObject CaptureLiveEntry(Player player, JObject args)
    {
        GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
        GameRes resources = GameRes.ExistingInstance;
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        bool footReady = chunks != null && chunks.TryGetRuntimeChunkView(player.transform.position, out var foot) &&
            foot != null && foot.gameObject.activeInHierarchy;
        RectTransform uiRoot = UIManager.ExistingInstance?.SafeAreaRoot;
        BasePanel[] runtimePanels = uiRoot != null ? uiRoot.GetComponentsInChildren<BasePanel>(true) : Array.Empty<BasePanel>();
        int hotbarCount = runtimePanels.Count(panel => panel != null &&
            (panel.PanelName == "UI_HotBar" || panel.PanelName?.StartsWith("UI_HotBar_", StringComparison.Ordinal) == true));
        int handCount = runtimePanels.Count(panel => panel != null &&
            (panel.PanelName == "UI_Hand" || panel.PanelName?.StartsWith("UI_Hand_", StringComparison.Ordinal) == true));
        Mod_HotBar hotbar = player.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        Mod_Hand hand = player.GetComponentInChildren<Mod_Hand>(true);
        return new JObject
        {
            ["editorSpriteTextures"] = CaptureEditorSpriteTextureIdentifiers(resources),
            ["resourceState"] = resources?.LoadState.ToString(), ["startupReady"] = resources != null && resources.IsStartupReady,
            ["inWorld"] = manager != null && manager.IsInGameWorld, ["gameplayReady"] = manager != null && manager.IsGameplayReady,
            ["entryInProgress"] = manager != null && manager.IsWorldEntryInProgress, ["localProfile"] = player.IsLocalProfile,
            ["profile"] = player.ProfileName, ["playerInitialized"] = player.IsInitialized,
            ["playerActive"] = player.gameObject.activeInHierarchy, ["footViewReady"] = footReady,
            ["windowReady"] = chunks != null && chunks.AreRuntimeWindowPresentationsReady,
            ["hotbarCount"] = hotbarCount, ["handCount"] = handCount,
            ["currentHotbarRegistered"] = hotbar?.RuntimeInventory?.basePanel != null &&
                runtimePanels.Contains(hotbar.RuntimeInventory.basePanel),
            ["currentHandRegistered"] = hand?.HandInventory?.basePanel != null &&
                runtimePanels.Contains(hand.HandInventory.basePanel),
            ["guid"] = player.itemData.Guid, ["isolationRoot"] = RequireIsolatedSaveRoot(player)
        };
    }

    // 读取正式目录已加载的纹理身份，检查异步预热使用的 TextureImporter 主对象 ID。
    private static JObject CaptureEditorSpriteTextureIdentifiers(GameRes resources)
    {
#if UNITY_EDITOR
        var localIds = new JArray();
        var visited = new HashSet<int>();
        if (resources != null)
            foreach (RuntimeItemDefinition definition in resources.ItemDefinitions.Values)
            {
                Texture2D texture = definition.Sprite != null ? definition.Sprite.texture : null;
                if (texture == null || !visited.Add(texture.GetInstanceID())) continue;
                string path = UnityEditor.AssetDatabase.GetAssetPath(texture);
                if (UnityEditor.AssetImporter.GetAtPath(path) is not UnityEditor.TextureImporter) continue;
                bool found = UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string _, out long localId);
                localIds.Add(new JObject { ["path"] = path, ["found"] = found, ["localId"] = localId });
            }
        return new JObject { ["textures"] = localIds };
#else
        return null;
#endif
    }

    private static JObject CaptureLiveWindow(Player player, JObject args)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || manager.WorldRuntime == null) throw new InvalidOperationException("真实世界流送未建立。");
        var result = new JObject
        {
            ["runtimeActive"] = manager.IsWorldModelRuntimeActive,
            ["windowReady"] = manager.AreRuntimeWindowPresentationsReady,
            ["world"] = manager.WorldRuntime.WorldId, ["epoch"] = manager.WorldRuntime.Epoch,
            ["pendingPresentation"] = manager.PendingRuntimeChunkPresentationCount,
            ["pendingData"] = manager.HasPendingChunkDataLoads,
            ["retainedChunks"] = manager.Chunks.Count
        };
        var address = manager.ResolveWorldAddress(player.transform.position);
        bool found = manager.TryGetChunkRuntime(address, out var chunk);
        result["footFound"] = found;
        result["footData"] = chunk?.DataStatus.ToString();
        result["footPresentation"] = chunk?.PresentationStatus.ToString();
        result["footSimulationLeases"] = chunk?.SimulationLeaseCount;
        result["footPresentationLeases"] = chunk?.PresentationLeaseCount;
        result["footView"] = manager.TryGetRuntimeChunkView(address, out var view) && view != null && view.gameObject.activeInHierarchy;
        return result;
    }

    #endregion

    #region 正式隔离写盘与非发布恢复

    private static JObject CaptureIsolatedRoundtrip(Player player, JObject args)
    {
        string root = RequireIsolatedSaveRoot(player);
        SaveDataMgr saves = SaveDataMgr.TestExistingInstance;
        GameSaveData original = saves.SaveData;
        string originalName = original.saveName, originalSeed = original.SaveSeed;
        string originalDirectory = saves.UserSavePath;
        int originalPlayers = original.PlayerData_Dict.Count, originalPlanets = original.PlanetData_Dict.Count;
        Data_Player originalPlayer = player.Data;
        int originalGuid = originalPlayer.Guid;
        string originalSpecial = originalPlayer.ItemSpecialData;
        var copy = MemoryPackSerializer.Deserialize<GameSaveData>(MemoryPackSerializer.Serialize(original));
        copy.Mechanical = MemoryPackSerializer.Deserialize<MachineArchive>(MemoryPackSerializer.Serialize(original.Mechanical));
        copy.DroppedItems = MemoryPackSerializer.Deserialize<DroppedItemArchive>(MemoryPackSerializer.Serialize(original.DroppedItems));
        string markerProfile = "player:lua-roundtrip-" + Guid.NewGuid().ToString("N");
        float amount = (float)GameplayTestSystemProbe.Number(args, "amount", 3);
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0)
            throw new ArgumentException("保存夹具的物品数量必须是非负有限单精度数。");
        var item = new Data_GeneralItem
        {
            IDName = "Stick_Wood", code = "lua-save-probe", Guid = 910143,
            Stack = new ItemStack { Amount = amount }, ItemSpecialData = "{\"roundtrip\":true}"
        };
        var marker = new Data_Player { IDName = "Player", Guid = 910144, Name_User = "Lua隔离恢复", ItemSpecialData = "{\"probe\":1}" };
        Vector3 savedPosition = new(101.423f, -460.583f, 0f);
        marker.transform.position = savedPosition;
        marker._inventoryData["probe"] = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = item } }, "probe");
        copy.PlayerData_Dict.Add(markerProfile, marker);
        string directory = Path.Combine(root, "Roundtrips", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "roundtrip.bytes");
        string parent = Path.GetDirectoryName(directory);
        if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("测试归档子目录不允许目录联接。");
        RequireIsolatedSaveRoot(player);
        bool firstWritten = saves.SaveToDisk(copy, directory, "roundtrip");
        if (!firstWritten || !File.Exists(file)) throw new IOException("正式隔离写盘没有生成测试文件。");
        byte[] firstBytes = File.ReadAllBytes(file);
        item.Stack.Amount = amount + 1;
        copy.PlayerData_Dict[markerProfile].ItemSpecialData = "{\"probe\":2}";
        RequireIsolatedSaveRoot(player);
        bool secondWritten = saves.SaveToDisk(copy, directory, "roundtrip");
        if (!secondWritten) throw new IOException("正式隔离替换写盘失败。");
        byte[] secondBytes = File.ReadAllBytes(file);
        string backup = file + ".bak";
        if (!File.Exists(backup)) throw new IOException("原子替换未保留上一版测试存档备份。");
        byte[] backupBytes = File.ReadAllBytes(backup);
        GameSaveData first, second;
        var shell = new GameObject("[LuaSaveReadProbe]");
        shell.SetActive(false);
        shell.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var reader = shell.AddComponent<SaveDataMgr>();
            if (!ReferenceEquals(SaveDataMgr.TestExistingInstance, saves) || !string.IsNullOrEmpty(reader.UserSavePath))
                throw new InvalidOperationException("恢复外壳意外触发了正式存档初始化。");
            first = reader.ReadTestArchiveWithoutPublishing(backupBytes);
            second = reader.ReadTestArchiveWithoutPublishing(secondBytes);
        }
        finally { UnityEngine.Object.DestroyImmediate(shell); }
        var firstItem = first.PlayerData_Dict[markerProfile]._inventoryData["probe"].itemSlots[0].itemData;
        var secondItem = second.PlayerData_Dict[markerProfile]._inventoryData["probe"].itemSlots[0].itemData;
        return new JObject
        {
            ["firstWritten"] = firstWritten, ["secondWritten"] = secondWritten, ["path"] = file,
            ["firstBytes"] = firstBytes.Length, ["secondBytes"] = secondBytes.Length,
            ["temporaryRemaining"] = File.Exists(file + ".tmp"), ["backupExists"] = File.Exists(file + ".bak"),
            ["backupMatchesFirst"] = backupBytes.SequenceEqual(firstBytes),
            ["firstAmount"] = firstItem.Stack.Amount, ["secondAmount"] = secondItem.Stack.Amount,
            ["firstGuid"] = firstItem.Guid, ["secondGuid"] = secondItem.Guid,
            ["firstId"] = firstItem.IDName, ["secondId"] = secondItem.IDName,
            ["firstSpecial"] = first.PlayerData_Dict[markerProfile].ItemSpecialData,
            ["secondSpecial"] = second.PlayerData_Dict[markerProfile].ItemSpecialData,
            ["firstPositionPreserved"] = Vector3.Distance(first.PlayerData_Dict[markerProfile].transform.position, savedPosition) < 0.001f,
            ["secondPositionPreserved"] = Vector3.Distance(second.PlayerData_Dict[markerProfile].transform.position, savedPosition) < 0.001f,
            ["firstPlayers"] = first.PlayerData_Dict.Count, ["secondPlayers"] = second.PlayerData_Dict.Count,
            ["expectedPlayers"] = originalPlayers + 1,
            ["activeSaveReferencePreserved"] = ReferenceEquals(saves.SaveData, original),
            ["activePlayerReferencePreserved"] = ReferenceEquals(originalPlayer, player.Data),
            ["activeManagerPreserved"] = ReferenceEquals(saves, SaveDataMgr.TestExistingInstance),
            ["activeNamePreserved"] = original.saveName == originalName,
            ["activeSeedPreserved"] = original.SaveSeed == originalSeed,
            ["activeCountsPreserved"] = original.PlayerData_Dict.Count == originalPlayers && original.PlanetData_Dict.Count == originalPlanets,
            ["activePlayerStatePreserved"] = originalPlayer.Guid == originalGuid && originalPlayer.ItemSpecialData == originalSpecial,
            ["activeDirectoryPreserved"] = string.Equals(saves.UserSavePath, originalDirectory, StringComparison.Ordinal) &&
                string.Equals(RequireIsolatedSaveRoot(player), root, StringComparison.OrdinalIgnoreCase)
        };
    }

    #endregion
}
