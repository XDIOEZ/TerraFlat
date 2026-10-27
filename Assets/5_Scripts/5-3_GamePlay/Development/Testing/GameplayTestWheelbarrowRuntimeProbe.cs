using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Gameplay.Building;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

/// <summary>独轮车现场通过正式建造、输入、库存及快照接口采样。</summary>
public static class GameplayTestWheelbarrowRuntimeProbe
{
    #region 现场入口与生命周期
    private static Fixture current;
    private static JObject lastCleanup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.Register("player", "wheelbarrow_setup", Setup);
        GameplayTestRuntimeProbe.Register("player", "wheelbarrow_prepare", Prepare);
        GameplayTestRuntimeProbe.RegisterAsync("player", "wheelbarrow_sample", Sample);
        GameplayTestRuntimeProbe.Register("player", "wheelbarrow_open_cargo", OpenCargo);
        GameplayTestRuntimeProbe.Register("player", "wheelbarrow_open_player_bag", OpenPlayerBag);
        GameplayTestRuntimeProbe.Register("player", "wheelbarrow_roundtrip", Roundtrip);
        GameplayTestRuntimeProbe.RegisterAsync("player", "wheelbarrow_teleport", Teleport);
        GameplayTestRuntimeProbe.Register("player", "wheelbarrow_unload", Unload);
        GameplayTestRuntimeProbe.Register("player", "wheelbarrow_display", Display);
        GameplayTestRuntimeProbe.Register("player", "wheelbarrow_cleanup_check", CleanupCheck);
    }

    private static Fixture Require(Player player)
        => current != null && ReferenceEquals(current.Player, player) && !current.Disposed
            ? current : throw new InvalidOperationException("独轮车现场未准备或玩家已更换。");

    private static JObject Setup(Player player, JObject args)
    {
        if (current != null) throw new InvalidOperationException("独轮车现场尚未清理。");
        var fixture = new Fixture(player);
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        lastCleanup = null;
        current = fixture;
        fixture.Place(GameRes.ExistingInstance.CreateItemData("Wheelbarrow"));
        Inventory_Data cargo = fixture.Driver.CargoInventory.Data;
        ItemData Item(float weight, float volume, int guid) => new Data_GeneralItem
        {
            IDName = "test:wheelbarrow_capacity", Guid = guid,
            Stack = new ItemStack { Amount = 1f, Stackable = false, Weight = weight, Volume = volume }
        };
        bool exact = cargo.TryAddItem(Item(120f, 300f, 830001), false);
        bool heavy = cargo.TryAddItem(Item(120.1f, 1f, 830002), false);
        bool volume = cargo.TryAddItem(Item(1f, 300.1f, 830003), false);
        bool twelve = true;
        for (int index = 0; index < 12; index++) twelve &= cargo.TryAddItem(Item(1f, 1f, 830100 + index));
        bool thirteenth = cargo.TryAddItem(Item(1f, 1f, 830200), false);
        foreach (ItemSlot slot in cargo.itemSlots)
            if (slot.itemData != null && !cargo.TrySetSlotItemAmount(slot, slot.itemData, 0f))
                throw new InvalidOperationException("独轮车容量夹具不能清空货仓。");
        return new JObject
        {
            ["placed"] = fixture.Car != null, ["available"] = fixture.Driver.IsAvailable,
            ["slots"] = cargo.itemSlots.Count, ["weight"] = cargo.MaxCarryWeight, ["volume"] = cargo.MaxCarryVolume,
            ["exactCapacity"] = exact, ["overweight"] = heavy, ["overvolume"] = volume,
            ["twelveAccepted"] = twelve, ["thirteenthAccepted"] = thirteenth
        };
    }

    private static JObject Prepare(Player player, JObject args)
    {
        Fixture fixture = Require(player);
        string side = args.Value<string>("side") ?? "rear";
        fixture.RemoveObstacle();
        fixture.Carrier.StopMotion();
        fixture.Carrier.Body.position = WorldLocalPresentation.ProjectPosition(fixture.Site);
        fixture.Car.transform.position = fixture.Carrier.Body.position;
        ItemMgr.Instance.NotifyRuntimeItemMoved(fixture.Car);
        Vector2 forward = fixture.Driver.ForwardDirection;
        Vector2 offset = side switch
        {
            "front" => forward * .9f,
            "side" => Vector2.right * .9f,
            "turn" => new Vector2(.4f, .8f),
            _ => -forward * .9f
        };
        PlayerTeleportService.ApplySurfacePosition(player, WorldTopologyRuntime.NormalizePosition(fixture.Site + offset));
        if (side == "obstacle") fixture.CreateObstacle();
        Physics2D.SyncTransforms();
        fixture.Recorder.Begin(fixture.Carrier, fixture.Driver, fixture.Mover, fixture.Obstacle);
        return new JObject { ["ready"] = fixture.Driver.IsAvailable && fixture.Carrier.Body.simulated,
            ["rider"] = fixture.Carrier.Rider != null, ["carrierSource"] = fixture.Mover.CarrierSource != null };
    }

    private static IEnumerator Sample(Player player, JObject args, Action<JObject> completed)
    {
        Fixture fixture = Require(player);
        for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
        completed(fixture.Recorder.Capture());
    }

    private static JObject CleanupCheck(Player player, JObject args)
        => lastCleanup == null ? throw new InvalidOperationException("前一个独轮车现场尚未完成自动收尾。") : (JObject)lastCleanup.DeepClone();
    #endregion

    #region 正式 F 键和库存事务
    private static JObject OpenCargo(Player player, JObject args)
    {
        Fixture fixture = Require(player);
        Mod_GameController controller = player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        Mod_InteractSender sender = player.GetComponentInChildren<Mod_InteractSender>(true);
        if (controller == null || sender == null || !controller.TryGetMouseWorldPosition(out Vector3 pointer))
            throw new InvalidOperationException("独轮车 F 键回归缺少控制器、发送器或世界指针。");
        fixture.Carrier.StopMotion();
        fixture.Carrier.Body.position = WorldLocalPresentation.ProjectPosition(pointer);
        fixture.Car.transform.position = fixture.Carrier.Body.position;
        ItemMgr.Instance.NotifyRuntimeItemMoved(fixture.Car);
        PlayerTeleportService.ApplySurfacePosition(player, WorldTopologyRuntime.NormalizePosition(pointer + Vector3.up * .9f));
        Physics2D.SyncTransforms();
        var action = controller._inputActions.Win10.E;
        int bindingIndex = action.bindings.Count;
        Keyboard keyboard = null;
        bool bindingAdded = false;
        try
        {
            keyboard = UnityInputSystem.AddDevice<Keyboard>();
            UnityInputSystem.AddDeviceUsage(keyboard, Mod_GameController.ExternalGameplayInputDeviceUsage);
            UnityInputSystem.AddDeviceUsage(keyboard, "FlatWorldWheelbarrowRegression");
            action.AddBinding("<Keyboard>{FlatWorldWheelbarrowRegression}/f");
            bindingAdded = true;
            UnityInputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
            UnityInputSystem.Update();
            UnityInputSystem.QueueStateEvent(keyboard, default(KeyboardState));
            UnityInputSystem.Update();
        }
        finally
        {
            if (keyboard != null) UnityInputSystem.RemoveDevice(keyboard);
            if (bindingAdded) action.ChangeBinding(bindingIndex).Erase();
        }
        Inventory cargo = fixture.Driver.CargoInventory;
        bool opened = cargo.basePanel != null && cargo.basePanel.IsOpen();
        Inventory bag = fixture.Bag;
        int slot = bag.Data.itemSlots.FindIndex(value => value.itemData == null);
        if (slot < 0) throw new InvalidOperationException("独轮车回归需要一个空的玩家背包槽位。");
        ItemData twine = GameRes.ExistingInstance.CreateItemData("Twine");
        twine.Guid = ItemMgr.Instance.GenerateGuid();
        twine.Stack.Amount = 2f;
        bag.Data.SetOne_ItemData(slot, twine);
        bool deposited = bag.TryTransferQuantityTo(slot, cargo, 0, 2);
        bool withdrew = cargo.TryTransferQuantityTo(0, bag, slot, 1);
        bool depositedAgain = bag.TryTransferQuantityTo(slot, cargo, 0, 1);
        // 留一件真实货物用于拆装与存档测试，玩家背包由快照在收尾归还。
        withdrew &= cargo.TryTransferQuantityTo(0, bag, slot, 1);
        return new JObject
        {
            ["fOpened"] = opened && sender.CurrentInteractionTargetGuid == fixture.Car.itemData.Guid,
            ["cargoOpen"] = opened, ["rider"] = fixture.Carrier.Rider != null,
            ["deposited"] = deposited, ["withdrew"] = withdrew, ["restoredDeposit"] = depositedAgain,
            ["remaining"] = cargo.Data.GetItemSlot(0).itemData?.Stack.Amount ?? 0f
        };
    }

    private static JObject OpenPlayerBag(Player player, JObject args)
    {
        Fixture fixture = Require(player);
        fixture.Driver.CargoInventory.basePanel?.Close();
        if (fixture.Bag.basePanel?.IsOpen() != true) fixture.Bag.SwitchUI();
        return new JObject { ["cargoClosed"] = fixture.Driver.CargoInventory.basePanel?.IsOpen() != true,
            ["bagOpen"] = fixture.Bag.basePanel?.IsOpen() == true };
    }
    #endregion

    #region 拆装、保存、传送和卸载
    private static JObject Roundtrip(Player player, JObject args)
    {
        Fixture fixture = Require(player);
        fixture.Bag.basePanel?.Close();
        fixture.Driver.CargoInventory.basePanel?.Close();
        Mod_Building building = fixture.Car.itemMods.GetMod_ByID<Mod_Building>(ModText.Building);
        if (!building.TryCreateDismantledSummoner(out Item summoner, out string reason))
            throw new InvalidOperationException("独轮车拆回失败：" + reason);
        try
        {
            Inventory packed = summoner.itemMods.GetMod_ByID<Mod_WheelbarrowInventory>(Mod_WheelbarrowInventory.ModuleId)?.CargoInventory;
            float packedAmount = packed?.Data.GetItemSlot(0).itemData?.Stack.Amount ?? 0f;
            byte[] bytes = ItemSnapshotSerialization.SerializePayload(ItemInstanceSnapshot.Capture(summoner.itemData));
            ItemData restored = GameRes.ExistingInstance.CreateItemData("Wheelbarrow");
            MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(bytes).RestoreTo(restored);
            fixture.DespawnCar();
            ItemMgr.Instance.DespawnItem(summoner, false);
            summoner = null;
            fixture.Place(restored);
            ItemData content = fixture.Driver.CargoInventory.Data.GetItemSlot(0).itemData;
            return new JObject
            {
                ["repacked"] = true, ["summonerId"] = restored.IDName, ["repackedAmount"] = packedAmount,
                ["serialized"] = bytes.Length > 0, ["placedAgain"] = fixture.Driver.IsAvailable,
                ["restoredAmount"] = content?.Stack.Amount ?? 0f, ["restoredId"] = content?.IDName ?? ""
            };
        }
        finally { if (summoner != null) ItemMgr.Instance.DespawnItem(summoner, false); }
    }

    private static IEnumerator Teleport(Player player, JObject args, Action<JObject> completed)
    {
        Fixture fixture = Require(player);
        bool pushed = fixture.Recorder.EverPushed;
        bool accepted = PlayerTeleportService.TryTeleportToSurface(player,
            WorldTopologyRuntime.NormalizePosition(fixture.Site + Vector2.up * 2f));
        for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
        completed(new JObject { ["everPushed"] = pushed, ["teleported"] = accepted,
            ["stopped"] = fixture.Carrier.Body.velocity.sqrMagnitude < .0001f,
            ["pushAfterRelease"] = fixture.Carrier.PushSource != null });
    }

    private static JObject Unload(Player player, JObject args)
    {
        Fixture fixture = Require(player);
        bool pushed = fixture.Recorder.EverPushed;
        fixture.Driver.Unload();
        return new JObject { ["everPushed"] = pushed, ["unloaded"] = !fixture.Driver.IsRuntimeLoaded,
            ["stopped"] = fixture.Carrier.Body.velocity.sqrMagnitude < .0001f,
            ["pushAfterRelease"] = fixture.Carrier.PushSource != null };
    }

    private static JObject Display(Player player, JObject args)
    {
        Fixture fixture = Require(player);
        fixture.Driver.Load();
        Prepare(player, new JObject { ["side"] = "rear" });
        Mod_InteractSender sender = player.GetComponentInChildren<Mod_InteractSender>(true);
        bool opened = sender != null && sender.TryInteractTarget(fixture.Carrier);
        return new JObject { ["opened"] = opened && fixture.Driver.CargoInventory.basePanel?.IsOpen() == true,
            ["amount"] = fixture.Driver.CargoInventory.Data.GetItemSlot(0).itemData?.Stack.Amount ?? 0f,
            ["rider"] = fixture.Carrier.Rider != null };
    }
    #endregion

    #region 独立场地、真实放置与收尾
    private sealed class Fixture : IDisposable
    {
        public readonly Player Player;
        public readonly Mod_Mover Mover;
        public readonly Inventory Bag;
        public readonly Vector2 Site;
        private readonly Vector3 originalPosition;
        private readonly InventoryInstanceSnapshot bagSnapshot;
        private readonly byte[] originalBagSnapshot;
        private readonly int originalBagSlots;
        public Item Car { get; private set; }
        public Mod_Wheelbarrow Driver { get; private set; }
        public Mod_Carrier Carrier { get; private set; }
        public GameplayTestWheelbarrowMotionRecorder Recorder { get; private set; }
        public BoxCollider2D Obstacle { get; private set; }
        public bool Disposed { get; private set; }
        public bool Removed { get; private set; }
        public bool PlayerRestored { get; private set; }
        public bool BagRestored { get; private set; }

        public Fixture(Player player)
        {
            Player = player;
            Mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover) ?? throw new InvalidOperationException("缺少玩家移动模块。");
            Bag = player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag)?.inventory ?? throw new InvalidOperationException("缺少玩家背包。");
            if (Mover.CarrierSource != null || Bag.basePanel?.IsOpen() == true)
                throw new InvalidOperationException("独轮车现场要求玩家未乘坐且背包已关闭。");
            originalPosition = WorldLocalPresentation.ToLogical(player.transform.position);
            bagSnapshot = InventoryInstanceSnapshot.Capture(Bag.Data);
            originalBagSnapshot = ItemSnapshotSerialization.SerializePayload(bagSnapshot);
            originalBagSlots = Bag.Data.itemSlots.Count;
            Site = FindClearSite(player);
        }

        private static Vector2 FindClearSite(Player player)
        {
            ChunkMgr chunks = ChunkMgr.ExistingInstance ?? throw new InvalidOperationException("区块管理器未就绪。");
            WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
            if (navigation == null || !navigation.IsNavigationReady) throw new InvalidOperationException("正式导航未就绪。");
            Vector2Int origin = Vector2Int.FloorToInt(WorldLocalPresentation.ToLogical(player.transform.position));
            for (int radius = 2; radius <= 8; radius++)
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius) continue;
                Vector2 center = (Vector2)WorldTopologyRuntime.NormalizeCell(origin + new Vector2Int(x, y)) + Vector2.one * .5f;
                bool clear = true;
                for (int dy = -3; dy <= 3 && clear; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    Vector2 point = WorldTopologyRuntime.NormalizePosition(center + new Vector2(dx, dy));
                    if (!chunks.TryGetRuntimeTerrainTile(point, out var tile) || !FarmlandSystem.IsOpen(tile) ||
                        FarmlandSystem.HasWorldPlant(tile.WorldCell) || tile.LiquidDepth > 0f ||
                        !navigation.TryGetCell(point, out _, out bool walkable) || !walkable)
                    { clear = false; break; }
                }
                if (clear) return center;
            }
            throw new InvalidOperationException("附近没有足够宽的已加载干燥空地供独轮车物理测试。");
        }

        public void Place(ItemData source)
        {
            PlayerTeleportService.ApplySurfacePosition(Player, WorldTopologyRuntime.NormalizePosition(Site + Vector2.up * .9f));
            source.Guid = ItemMgr.Instance.GenerateGuid();
            var slot = new ItemSlot(0) { itemData = source };
            var inventory = new Inventory_Data(new List<ItemSlot> { slot }, "独轮车测试材料");
            var request = new BuildingPlacementRequest { SourceData = source, SourceInventory = inventory,
                SourceSlot = slot, Actor = Player, Position = Site, AuthorityPosition = Site, MaximumDistance = 3f };
            if (!BuildingPlacementService.TryPlace(request, out BuildingPlacementResult placed, out string reason) || placed.Item == null)
                throw new InvalidOperationException("独轮车正式放置失败：" + reason);
            Car = placed.Item;
            Driver = Car.itemMods.GetMod_ByID<Mod_Wheelbarrow>(Mod_Wheelbarrow.ModuleId)
                ?? throw new InvalidOperationException("独轮车缺少驱动模块。");
            Carrier = Driver.Carrier ?? throw new InvalidOperationException("独轮车缺少载具模块。");
            Recorder = Car.gameObject.AddComponent<GameplayTestWheelbarrowMotionRecorder>();
        }

        public void DespawnCar()
        {
            RemoveObstacle();
            if (Car != null)
            {
                Car.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.ReleasePlacementOccupancy();
                ItemMgr.Instance.DespawnItem(Car, false);
            }
            Car = null; Driver = null; Carrier = null; Recorder = null;
        }

        public void CreateObstacle()
        {
            var wall = new GameObject("[WheelbarrowObstacle]");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, Player.gameObject.scene);
            int mask = Physics2D.GetLayerCollisionMask(Car.gameObject.layer);
            for (int layer = 0; layer < 32; layer++)
                if ((mask & (1 << layer)) != 0) { wall.layer = layer; break; }
            wall.transform.position = WorldLocalPresentation.ProjectPosition(Site + Vector2.down * 1.1f);
            Obstacle = wall.AddComponent<BoxCollider2D>();
            Obstacle.size = new Vector2(3f, .2f);
        }

        public void RemoveObstacle()
        {
            if (Obstacle != null) UnityEngine.Object.DestroyImmediate(Obstacle.gameObject);
            Obstacle = null;
        }

        public void Dispose()
        {
            if (Disposed) return;
            int guid = Car != null ? Car.itemData.Guid : 0;
            var errors = new List<Exception>();
            try
            {
                Bag.basePanel?.Close();
                DespawnCar();
                Removed = guid == 0 || ItemMgr.Instance.GetItemByGuid(guid) == null;
            }
            catch (Exception error) { errors.Add(error); }
            try
            {
                bagSnapshot.RestoreTo(Bag.Data, data => data.SharedConfiguration == null
                    ? ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, data) : data);
                if (Bag.Data.itemSlots.Count > originalBagSlots)
                    Bag.Data.itemSlots.RemoveRange(originalBagSlots, Bag.Data.itemSlots.Count - originalBagSlots);
                Bag.Data.NotifySlotsChanged();
                Bag.RefreshUI();
                BagRestored = originalBagSnapshot.SequenceEqual(ItemSnapshotSerialization.SerializePayload(InventoryInstanceSnapshot.Capture(Bag.Data)));
            }
            catch (Exception error) { errors.Add(error); }
            try
            {
                PlayerTeleportService.ApplySurfacePosition(Player, originalPosition);
                PlayerRestored = WorldTopologyRuntime.Distance(Player.transform.position,
                    WorldLocalPresentation.ProjectPosition(originalPosition)) < .01f;
            }
            catch (Exception error) { errors.Add(error); }
            Disposed = true;
            lastCleanup = new JObject { ["removed"] = Removed, ["playerRestored"] = PlayerRestored, ["bagRestored"] = BagRestored };
            if (ReferenceEquals(current, this)) current = null;
            if (errors.Count > 0) throw new AggregateException("独轮车现场自动清理失败。", errors);
        }
    }
    #endregion
}

/// <summary>在真实物理帧记录独轮车速度与推动来源。</summary>
public sealed class GameplayTestWheelbarrowMotionRecorder : MonoBehaviour
{
    #region 推动采样
    private Mod_Carrier carrier;
    private Mod_Wheelbarrow driver;
    private Mod_Mover mover;
    private BoxCollider2D obstacle;
    private Vector2 start;
    private int frames;
    private float peakSpeed, ratio;
    public bool EverPushed { get; private set; }

    public void Begin(Mod_Carrier source, Mod_Wheelbarrow motion, Mod_Mover player, BoxCollider2D wall)
    {
        carrier = source; driver = motion; mover = player; obstacle = wall; start = source.Body.position;
        frames = 0; peakSpeed = ratio = 0f; EverPushed = false;
    }

    private void FixedUpdate()
    {
        if (carrier == null || carrier.Body == null || driver == null || mover == null) return;
        frames++;
        peakSpeed = Mathf.Max(peakSpeed, carrier.Body.velocity.magnitude);
        if (carrier.PushSource != mover || carrier.PushedVelocity.sqrMagnitude < .0001f ||
            mover.CarrierPushIntentVelocity.sqrMagnitude < .0001f) return;
        EverPushed = true;
        ratio = carrier.PushedVelocity.magnitude / mover.CarrierPushIntentVelocity.magnitude;
    }

    public JObject Capture() => new()
    {
        ["frames"] = frames, ["everPushed"] = EverPushed,
        ["distance"] = WorldTopologyRuntime.Distance(start, carrier.Body.position), ["peakSpeed"] = peakSpeed,
        ["ratio"] = ratio, ["forwardX"] = driver.ForwardDirection.x, ["forwardY"] = driver.ForwardDirection.y,
        ["wallBlocked"] = obstacle != null && carrier.Body.GetComponent<BoxCollider2D>().bounds.min.y >= obstacle.bounds.max.y - .02f,
        ["stopped"] = carrier.Body.velocity.sqrMagnitude < .0001f, ["pushAfterRelease"] = carrier.PushSource != null,
        ["rider"] = carrier.Rider != null, ["carrierSource"] = mover.CarrierSource != null
    };
    #endregion
}
