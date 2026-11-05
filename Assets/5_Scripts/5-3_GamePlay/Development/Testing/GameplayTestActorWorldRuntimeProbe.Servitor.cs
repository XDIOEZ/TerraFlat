using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestActorWorldRuntimeProbe
{
    #region 机仆规则与正式工作现场
    private static void RegisterServitor()
    {
        GameplayTestSystemProbe.Register("ai", "servitor_rules", CaptureServitorRules);
        GameplayTestRuntimeProbe.RegisterAsync("ai", "servitor_work", CaptureServitorWork);
        GameplayTestRuntimeProbe.RegisterAsync("ai", "servitor_cooperation", CaptureServitorCooperation);
    }

    // 规则采样直接调用生产电池，不复写电量计算或行为树优先级。
    private static JObject CaptureServitorRules(JObject args)
    {
        var battery = new ServitorBatteryState { StoredJoules = 2001f };
        battery.Consume(2f, 10000f, .2f);
        var result = new JObject { ["lowRequested"] = battery.RechargeRequested, ["lowStored"] = battery.StoredJoules };
        float watts = battery.RequestChargingWatts(.5f, 10000f);
        battery.ReceiveEnergy(watts * .5f, 10000f);
        result["requestedWatts"] = watts; result["halfSecondStored"] = battery.StoredJoules;
        result["rechargeLatched"] = battery.RechargeRequested;
        var restored = JsonConvert.DeserializeObject<ServitorBatteryState>(JsonConvert.SerializeObject(battery));
        result["restoredStored"] = restored.StoredJoules; result["restoredIntent"] = restored.RechargeRequested;
        restored.StoredJoules = 9999f;
        result["tailWatts"] = restored.RequestChargingWatts(1f, 10000f);
        restored.ReceiveEnergy(restored.RequestChargingWatts(1f, 10000f), 10000f);
        result["fullStored"] = restored.StoredJoules; result["fullRequested"] = restored.RechargeRequested;
        restored.Consume(20000f, 10000f, .2f);
        result["emptyStored"] = restored.StoredJoules; result["emptyRequested"] = restored.RechargeRequested;
#if UNITY_EDITOR
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/Gameplay/Player/UI_ServitorTerminal.prefab");
        var terminal = prefab != null ? prefab.GetComponent<ServitorTerminalPanel>() : null;
        result["terminalBound"] = terminal != null && terminal.StatusText != null && terminal.OrderText != null &&
            terminal.HintText != null && terminal.BatteryGauge != null && terminal.StandbyButton != null &&
            terminal.HaulButton != null && terminal.StorageButton != null && terminal.RadiusLessButton != null &&
            terminal.RadiusMoreButton != null && terminal.RechargeButton != null && terminal.CloseButton != null;
        result["modulePrefab"] = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/Gameplay/Modules/AI/Module_ServitorController.prefab")?.GetComponent<Mod_ServitorController>() != null;
        JObject actor = (JObject)JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.streamingAssetsPath,
            "GameConfig/Actors/definitions/servitor-actors.json")))["actors"][0];
        result["actorModuleIdentityDistinct"] = (string)actor["id"] == "Servitor" &&
            (string)actor["modules"]["servitor"]["id"] == Mod_ServitorController.ModuleId && Mod_ServitorController.ModuleId != "Servitor";
        result["footprintSeparateFromHitbox"] = (string)actor["visual"]["collider"]["type"] == "CircleCollider2D" &&
            (float)actor["visual"]["collider"]["radius"] <= .35f &&
            (float)actor["modules"]["health"]["parameters"]["$collider2D"]["size"]["y"] >= 1f;
        result["sprite"] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/6_Art/Generated/Actors/Servitor/Servitor_Idle_Right.png") != null;
        var shell = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>((string)actor["sourcePrefab"]);
        var shape = shell != null ? shell.GetComponent<ItemPhysicsTemplate2D>() : null;
        result["shellIdentityMatchesDefinition"] = shell != null && shell.GetComponent<Item>()?.itemData.IDName == "Actor_Servitor";
        result["shellShapeMatchesDefinition"] = shape != null && shape.colliderType == (string)actor["visual"]["collider"]["type"] &&
            Mathf.Approximately(shape.radius, (float)actor["visual"]["collider"]["radius"]) &&
            shape.offset == Vector2.zero && shape.colliderEnabled && shape.isTrigger;
#endif
        return result;
    }

    // 通过正式出生、终端按钮、电网和库存事务验证，不在采样器中直接改机仆的私有状态。
    private static IEnumerator CaptureServitorWork(Item player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var site = FindOpenCell(player, true);
        Vector3 start = new(site.WorldCell.x + .5f, site.WorldCell.y + .5f);
        Vector3 originalPlayerPosition = player.transform.position;
        ActorFixture own = null;
        var machines = new List<int>();
        DroppedItemHandle drop = default;
        ServitorTerminalPanel terminal = null;
        var result = new JObject();
        try
        {
            MachineEntity storage = PlaceServitorTestMachine("Chest_Wood", start + Vector3.right * 3f, machines);
            MachineEntity wire = PlaceServitorTestMachine("Wire_Copper", start + Vector3.right * 2f, machines);
            MachineEntity battery = PlaceServitorTestMachine("Battery_Basic", start + Vector3.right * 2f, machines);
            battery.ElectricalStoredJoules = battery.Definition.Electrical.CapacityJoules;
            if (battery.State != null) battery.State.ElectricalStoredJoules = battery.ElectricalStoredJoules;
            MachineWorld.StateChanged(battery);
            own = new ActorFixture(start, "Servitor");
            var robot = own.Actor.itemMods.RequireSingleModById<Mod_ServitorController>(Mod_ServitorController.ModuleId);
            var tree = own.Actor.itemMods.RequireSingleModById<Mod_AnimalBehaviorTree>(ModText.AI);
            result["loaded"] = robot.Ready && tree.IsRuntimeLoaded;
            player.transform.position = own.Actor.transform.position + Vector3.left * .5f;
            robot.OnInteractStart(player);
            foreach (ServitorTerminalPanel candidate in UnityEngine.Object.FindObjectsOfType<ServitorTerminalPanel>())
                if (candidate.GetComponent<BasePanel>().IsOpen()) { terminal = candidate; break; }
            result["terminalOpened"] = terminal != null;
            if (terminal == null) throw new InvalidOperationException("机仆交互未打开正式终端。");
            // 保持正式终端可见一个截图周期，供统一测试报告复核布局。
            double previewEnd = Time.realtimeSinceStartupAsDouble + 11f;
            while (Time.realtimeSinceStartupAsDouble < previewEnd) yield return null;
            for (int attempt = 0; attempt < 128 && robot.DestinationGuid != storage.Id; attempt++)
                terminal.StorageButton.onClick.Invoke();
            terminal.RadiusLessButton.onClick.Invoke();
            result["destinationAssigned"] = robot.DestinationGuid == storage.Id;
            result["radiusButtonWorked"] = robot.WorkRadius == 8f;
            terminal.HaulButton.onClick.Invoke();
            result["haulButtonWorked"] = robot.WorkMode == ServitorWorkMode.Haul;
            terminal.StandbyButton.onClick.Invoke();
            result["standbyButtonWorked"] = robot.WorkMode == ServitorWorkMode.Standby;
            // 只提高公开耗电配置准备接近满电的回充现场，随后恢复正式功率。
            robot.StandbyWatts = 500f;
            yield return AwaitServitor(() => robot.BatteryPercentage < 99f, 5f, "机仆未通过真实 Tick 消耗电量。");
            robot.StandbyWatts = 2f;
            terminal.RechargeButton.onClick.Invoke();
            result["rechargeButtonWorked"] = robot.NeedsRecharge;
            terminal.CloseButton.onClick.Invoke();
            player.transform.position = originalPlayerPosition;
            yield return AwaitServitor(() => !robot.NeedsRecharge, 15f, "机仆未找到正式通电电线并充满。", () =>
                $"status={robot.Status}, battery={robot.BatteryPercentage}, action={tree.CurrentAction}, actor={own.Actor.transform.position}, wire={wire.Position}, " +
                $"path={own.Actor.itemMods.RequireSingleModById<Mod_Mover_AI>(ModText.Mod_Mover).DestinationResult}, " +
                $"voltage={wire.ElectricalNetwork?.Voltage}, power={wire.ElectricalNetwork?.PowerRatio}, source={battery.ElectricalStoredJoules}, " +
                $"actorWorld={GameplayWorldRegistry.GetItemWorldKey(own.Actor)}, wireWorld={wire.ScopeKey}");
            result["wireCharged"] = robot.BatteryPercentage > 99f;
            result["movedToWire"] = WorldTopologyRuntime.SqrDistance(own.Actor.transform.position,
                WorldLocalPresentation.ProjectPosition(start)) > .1f;
            result["gridEnergySpent"] = battery.ElectricalStoredJoules < battery.Definition.Electrical.CapacityJoules;

            player.transform.position = own.Actor.transform.position + Vector3.left * .5f;
            robot.OnInteractStart(player);
            terminal.HaulButton.onClick.Invoke(); terminal.CloseButton.onClick.Invoke();
            player.transform.position = originalPlayerPosition;
            ItemData wood = CreateServitorTestItem("Log");
            wood.Stack.Amount = 7f;
            Vector2 dropPosition = WorldLocalPresentation.ProjectPosition(start);
            drop = DroppedItemService.Spawn(wood, dropPosition, dropPosition, randomizeRotation: false);
            yield return AwaitServitor(() => CountServitorStorageItems(storage, "Log") >= 7, 20f,
                "机仆未通过正式搬运事务把木材放入目标容器。", () =>
                    $"status={robot.Status}, cargo={robot.CargoAmount}, delivered={CountServitorStorageItems(storage, "Log")}, actor={own.Actor.transform.position}, storage={storage.Position}, " +
                    $"path={own.Actor.GetComponent<WorldNavigationAgent>()?.DestinationResult}, goal={own.Actor.GetComponent<WorldNavigationAgent>()?.ResolvedDestination}, velocity={own.Actor.GetComponent<WorldNavigationAgent>()?.Velocity}");
            result["deliveredWood"] = CountServitorStorageItems(storage, "Log");
            result["cargoAfterDelivery"] = robot.CargoAmount;
            result["workContinuesWhenClosed"] = robot.WorkMode == ServitorWorkMode.Haul;

            // 新货物抵达货仓后触发真实低电量抢占，货物不能在中断时丢失。
            wood = CreateServitorTestItem("Log"); wood.Stack.Amount = 3f;
            dropPosition = own.Actor.transform.position;
            DroppedItemService.Remove(drop);
            drop = DroppedItemService.Spawn(wood, dropPosition, dropPosition, randomizeRotation: false);
            yield return AwaitServitor(() => robot.CargoAmount > 0, 8f, "未建立带货中断现场。");
            int cargoBefore = robot.CargoAmount;
            // 临时提高自有机仆的回充阈值，短时验证带货回充与恢复；20%定量规则另测。
            robot.LowBatteryFraction = 1f;
            yield return AwaitServitor(() => robot.NeedsRecharge, 5f, "低电量未触发自动充电。");
            robot.LowBatteryFraction = .2f;
            yield return null;
            result["lowBatteryAction"] = tree.CurrentAction;
            result["interruptedCargoBefore"] = cargoBefore;
            result["interruptedCargoAfter"] = robot.CargoAmount;
            own.Actor.Save();
            ItemData snapshot = FastCloner.FastCloner.DeepClone(own.Actor.itemData);
            float savedBattery = robot.BatteryPercentage;
            own.Reload(snapshot, WorldLocalPresentation.ToLogical(own.Actor.transform.position));
            robot = own.Actor.itemMods.RequireSingleModById<Mod_ServitorController>(Mod_ServitorController.ModuleId);
            result["restoredCargo"] = robot.CargoAmount;
            result["restoredBattery"] = robot.BatteryPercentage;
            result["savedBattery"] = savedBattery;
            result["restoredDestination"] = robot.DestinationGuid == storage.Id;
            result["restoredRechargeIntent"] = robot.NeedsRecharge;
            // 只更改隔离存档的来源世界，检验正式装载拒绝旧世界订单而保留货物和电量。
            ItemData foreignSnapshot = FastCloner.FastCloner.DeepClone(snapshot);
            var foreignModule = (Ex_ModData)foreignSnapshot.ModuleDataDic["servitor"];
            JObject foreignState = foreignModule.GetData<JObject>();
            foreignState["Order"]["WorldKey"] = "test:servitor-other-world";
            foreignModule.WriteData(foreignState);
            Vector3 restorePosition = WorldLocalPresentation.ToLogical(own.Actor.transform.position);
            own.Reload(foreignSnapshot, restorePosition);
            robot = own.Actor.itemMods.RequireSingleModById<Mod_ServitorController>(Mod_ServitorController.ModuleId);
            result["foreignOrderCleared"] = robot.DestinationGuid == 0 && robot.WorkMode == ServitorWorkMode.Standby;
            result["foreignCargoRetained"] = robot.CargoAmount;
            result["foreignBatteryRetained"] = robot.BatteryPercentage;
            own.Reload(snapshot, restorePosition);
            robot = own.Actor.itemMods.RequireSingleModById<Mod_ServitorController>(Mod_ServitorController.ModuleId);
            player.transform.position = own.Actor.transform.position + Vector3.left * 10f;
            result["remoteCommandRejected"] = !robot.SetWorkMode(player, ServitorWorkMode.Standby);
            player.transform.position = originalPlayerPosition;
            yield return AwaitServitor(() => !robot.NeedsRecharge && CountServitorStorageItems(storage, "Log") == 10, 25f,
                "恢复后未自动完成充电并继续交付货物。", () => $"status={robot.Status}, cargo={robot.CargoAmount}, battery={robot.BatteryPercentage}");
            result["automaticallyResumed"] = robot.WorkMode == ServitorWorkMode.Haul && robot.CargoAmount == 0;
            completed(result);
        }
        finally
        {
            if (terminal != null) terminal.GetComponent<BasePanel>().Close();
            player.transform.position = originalPlayerPosition;
            DroppedItemService.Remove(drop);
            own?.Dispose();
            for (int index = machines.Count - 1; index >= 0; index--)
                if (MachineWorld.GetById(machines[index]) != null) MachineWorld.Remove(machines[index]);
        }
    }

    // 两只正式机仆争用同一份实物，并通过真实耗电和现场补线验证停机与唤醒。
    private static IEnumerator CaptureServitorCooperation(Item player, JObject args, Action<JObject> completed)
    {
        var site = FindOpenCell(player, true);
        Vector3 start = new(site.WorldCell.x + .5f, site.WorldCell.y + .5f);
        Vector3 originalPlayerPosition = player.transform.position;
        ActorFixture first = null, second = null;
        var machines = new List<int>();
        DroppedItemHandle drop = default;
        try
        {
            MachineEntity storage = PlaceServitorTestMachine("Chest_Wood", start + Vector3.right * 3f, machines);
            first = new ActorFixture(start + Vector3.right * 2f, "Servitor");
            second = new ActorFixture(start + Vector3.right, "Servitor");
            var primary = first.Actor.itemMods.RequireSingleModById<Mod_ServitorController>(Mod_ServitorController.ModuleId);
            var partner = second.Actor.itemMods.RequireSingleModById<Mod_ServitorController>(Mod_ServitorController.ModuleId);
            ConfigureServitorTestOrder(primary, player, storage, ServitorWorkMode.Haul);
            ConfigureServitorTestOrder(partner, player, storage, ServitorWorkMode.Standby);
            player.transform.position = originalPlayerPosition;
            ItemData wood = CreateServitorTestItem("Log"); wood.Stack.Amount = 7;
            Vector2 point = WorldLocalPresentation.ProjectPosition(start);
            drop = DroppedItemService.Spawn(wood, point, point, randomizeRotation: false);
            yield return AwaitServitor(() => primary.Status == "前往拾取物品" && primary.CargoAmount == 0, 8f,
                "第一只机仆未预约尚未拾取的物品。");
            ConfigureServitorTestOrder(primary, player, storage, ServitorWorkMode.Standby);
            ConfigureServitorTestOrder(partner, player, storage, ServitorWorkMode.Haul);
            player.transform.position = originalPlayerPosition;
            yield return AwaitServitor(() => CountServitorStorageItems(storage, "Log") == 7, 15f,
                "取消工作后旧预约未释放给另一只机仆。");
            var result = new JObject { ["interruptedReservationReleased"] = primary.WorkMode == ServitorWorkMode.Standby &&
                primary.CargoAmount == 0 && partner.CargoAmount == 0 };

            ConfigureServitorTestOrder(primary, player, storage, ServitorWorkMode.Haul);
            player.transform.position = originalPlayerPosition;
            wood = CreateServitorTestItem("Log"); wood.Stack.Amount = 23;
            DroppedItemService.Remove(drop);
            drop = DroppedItemService.Spawn(wood, point, point, randomizeRotation: false);
            yield return AwaitServitor(() => CountServitorStorageItems(storage, "Log") >= 30, 25f,
                "两只机仆未完成同一堆物品的搬运。", () =>
                $"first={primary.Status}/{primary.CargoAmount}, second={partner.Status}/{partner.CargoAmount}, stored={CountServitorStorageItems(storage, "Log")}");
            result["cooperativeStored"] = CountServitorStorageItems(storage, "Log");
            result["cooperativeCargo"] = primary.CargoAmount + partner.CargoAmount;

            ConfigureServitorTestOrder(partner, player, storage, ServitorWorkMode.Standby);
            player.transform.position = originalPlayerPosition;
            primary.WorkingWatts = primary.StandbyWatts = 1000000f;
            yield return AwaitServitor(() => primary.BatteryPercentage == 0f, 5f, "真实机仆未耗尽电量。");
            primary.WorkingWatts = 10f; primary.StandbyWatts = 2f;
            yield return null;
            result["emptyStopped"] = !first.Actor.itemMods.RequireSingleModById<Mod_Mover_AI>(ModText.Mod_Mover).HasActiveDestination;
            result["emptyStillAlive"] = primary.Ready;
            result["emptyAction"] = first.Actor.itemMods.RequireSingleModById<Mod_AnimalBehaviorTree>(ModText.AI).CurrentAction;

            Vector2 logical = WorldLocalPresentation.ToLogical(first.Actor.transform.position);
            Vector3 rescuePosition = new(Mathf.Floor(logical.x) + .5f, Mathf.Floor(logical.y) + .5f);
            MachineEntity wire = PlaceServitorTestMachine("Wire_Copper", rescuePosition, machines);
            MachineEntity battery = PlaceServitorTestMachine("Battery_Basic", rescuePosition, machines);
            battery.ElectricalStoredJoules = battery.Definition.Electrical.CapacityJoules;
            battery.State.ElectricalStoredJoules = battery.ElectricalStoredJoules;
            MachineWorld.StateChanged(battery);
            yield return AwaitServitor(() => primary.BatteryPercentage > .1f, 8f,
                "耗尽机仆未能从身边新接入的通电电线恢复。", () => primary.Status);
            result["emptyRecharged"] = primary.BatteryPercentage;
            result["rescueGridSpentEnergy"] = battery.ElectricalStoredJoules < battery.Definition.Electrical.CapacityJoules;
            completed(result);
        }
        finally
        {
            player.transform.position = originalPlayerPosition;
            DroppedItemService.Remove(drop);
            second?.Dispose(); first?.Dispose();
            for (int index = machines.Count - 1; index >= 0; index--)
                if (MachineWorld.GetById(machines[index]) != null) MachineWorld.Remove(machines[index]);
        }
    }

    private static void ConfigureServitorTestOrder(Mod_ServitorController robot, Item player,
        MachineEntity storage, ServitorWorkMode mode)
    {
        player.transform.position = robot.item.transform.position + Vector3.left * .5f;
        for (int attempt = 0; attempt < 128 && robot.DestinationGuid != storage.Snapshot.Guid; attempt++)
            if (!robot.SelectNextNearbyStorage(player)) break;
        if (robot.DestinationGuid != storage.Snapshot.Guid || !robot.SetWorkMode(player, mode))
            throw new InvalidOperationException("无法通过正式近距离命令设置自有机仆。");
    }

    private static IEnumerator AwaitServitor(Func<bool> condition, float seconds, string failure, Func<string> details = null)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + seconds;
        while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        if (!condition()) throw new TimeoutException(failure + (details == null ? string.Empty : " " + details()));
    }

    private static MachineEntity PlaceServitorTestMachine(string id, Vector3 position, List<int> owned)
    {
        ItemData data = CreateServitorTestItem(id);
        data.Guid = ItemMgr.GetInstance().GenerateGuid();
        data.transform ??= new ItemTransform(); data.transform.position = position;
        data.transform.rotation = Quaternion.identity; data.transform.scale = Vector3.one; data.inHand = false;
        Mod_Building.SetInstalledDataState(data);
        MachineEntity machine = MachineWorld.Place(data);
        owned.Add(machine.Id); return machine;
    }

    private static int CountServitorStorageItems(MachineEntity storage, string id)
    {
        int count = 0;
        if (storage.Logic is StorageLogic logic)
            foreach (Inventory inventory in logic.Inventories)
                foreach (ItemSlot slot in inventory.Data.itemSlots)
                    if (slot?.itemData?.IDName == id) count += Inventory_Data.GetWholeStackAmount(slot.itemData.Stack.Amount);
        return count;
    }

    private static ItemData CreateServitorTestItem(string id)
    {
        if (GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetItemDefinition(id, out var definition))
            throw new InvalidOperationException("机仆测试物品未登记：" + id);
        return definition.CreateItemData();
    }
    #endregion
}
