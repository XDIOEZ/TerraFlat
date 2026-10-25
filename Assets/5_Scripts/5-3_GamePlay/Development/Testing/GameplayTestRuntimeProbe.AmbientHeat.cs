using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestRuntimeProbe
{
    #region 自然传热真实入口采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterAmbientHeatRuntimeProbes()
    {
        Register("containers", "ambient_heat", CaptureVesselAmbientHeat);
        Register("industrial_fluids", "tank_ambient_heat", (_, args) => MachineWorld.CaptureGameplayTankAmbientHeat(args));
        Register("processing", "furnace_ambient_heat", (_, _) => MachineWorld.CaptureGameplayFurnaceAmbientHeat());
    }

    // 只提供独立容器的初态和环境，实际液温、坩埚更新与炉内加热全部走正式生产入口。
    private static JObject CaptureVesselAmbientHeat(Player player, JObject args)
    {
        float current = (float)GameplayTestSystemProbe.Number(args, "current", 20d);
        float ambient = (float)GameplayTestSystemProbe.Number(args, "ambient", 70d);
        float seconds = (float)GameplayTestSystemProbe.Number(args, "seconds", 1d);
        string water = FluidCatalog.Default.Find(FluidIds.PureWater).LiquidId;
        LiquidContainerState State()
        {
            var state = new LiquidContainerState();
            MixedLiquidContents.Add(state, water, .5f, current);
            return state;
        }
        LiquidContainerState whole = State(), split = State();
        long revision = whole.Revision;
        bool changed = InventoryVesselHeating.ProcessWorldHeat(whole, ambient, seconds);
        for (int i = 0; i < 10; i++)
            InventoryVesselHeating.ProcessWorldHeat(split, ambient, seconds / 10f);

        ItemData heatedItem = CreateDragItem("IronBucket");
        if (!Mod_WaterVessel.TryRead(heatedItem, out var storage, out _))
            throw new InvalidOperationException("真实铁桶缺少液体模块。");
        storage.WriteData(State());
        Inventory heatingInventory = CreateDragInventory("lua.ambient.active-heat", heatedItem);
        bool heatHandled = InventoryVesselHeating.ProcessHeat(heatingInventory, heatingInventory, ambient, seconds);
        if (!Mod_WaterVessel.TryRead(heatingInventory.Data.itemSlots[0].itemData, out _, out var activeState))
            throw new InvalidOperationException("主动加热后真实液体载荷丢失。");

        var root = new GameObject("Lua Ambient Crucible") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        GameItem item = root.AddComponent<GameItem>();
        Mod_WaterVessel vessel = root.AddComponent<Mod_WaterVessel>();
        Mod_Mortar mortar = root.AddComponent<Mod_Mortar>();
        var source = new GameplayAmbientItemSource(item, ambient);
        try
        {
            item.BindData(CreateDragItem("Crucible"));
            var vesselData = (Ex_ModData_MemoryPackable)item.itemData.ModuleDataDic["vessel"];
            var mortarData = (Ex_ModData_MemoryPackable)item.itemData.ModuleDataDic["mortar"];
            vesselData.WriteData(State());
            vessel.ModuleInit(item, vesselData);
            mortar.ModuleInit(item, mortarData);
            item.itemMods.AddMod(vessel);
            item.itemMods.AddMod(mortar);
            vessel.Load();
            mortar.Load();
            ItemEnvironmentSources.Register(source);
            mortar.ModUpdate(seconds);
            float afterMortar = vessel.Data.Temperature;
            long afterMortarRevision = vessel.Data.Revision;
            vessel.GameplayTestTickWorldHeat();
            return new JObject
            {
                ["whole"] = whole.Temperature, ["split"] = split.Temperature,
                ["changed"] = changed, ["revisionChanged"] = whole.Revision != revision,
                ["amount"] = whole.Amount, ["activeTemperature"] = activeState.Temperature,
                ["heatHandled"] = heatHandled, ["crucible"] = afterMortar,
                ["afterWorldTick"] = vessel.Data.Temperature,
                ["worldTickRevisionStable"] = vessel.Data.Revision == afterMortarRevision,
                ["crucibleAmount"] = vessel.Data.Amount,
                ["loaded"] = mortar.IsRuntimeLoaded && vessel.IsRuntimeLoaded && mortar.IsCrucible
            };
        }
        finally
        {
            ItemEnvironmentSources.Unregister(source);
            try { mortar.Unload(); }
            finally
            {
                try { vessel.Unload(); }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
    }

    private sealed class GameplayAmbientItemSource : IItemEnvironmentSource
    {
        private readonly Item item;
        private readonly float temperature;
        public GameplayAmbientItemSource(Item item, float temperature)
        { this.item = item; this.temperature = temperature; }
        public bool TrySample(Item candidate, out ItemEnvironmentSample sample)
        {
            sample = new ItemEnvironmentSample(100d, temperature, true, false, true);
            return ReferenceEquals(candidate, item);
        }
        public float SupplyAmbientOxygen(Item candidate, float maximumAmount) => 0f;
        public bool TrySampleCombustion(string worldKey, Vector2 position, out bool canBurn)
        { canBurn = false; return false; }
    }

    #endregion
}

public sealed partial class Mod_WaterVessel
{
    #region 容器自然调度的测试入口
    // 在 Testing 内驱动正式落地调度，验证坩埚已经更新后不会再叠加一轮液体传热。
    internal void GameplayTestTickWorldHeat()
    {
        worldHeatClock = WorldHeatTickInterval;
        TickWorldHeat();
    }
    #endregion
}

public static partial class MachineWorld
{
    #region 机器自然传热的生产阶段采样
    internal static JObject CaptureGameplayTankAmbientHeat(JObject args)
    {
        using var fixture = new GameplayFluidScope();
        var tank = fixture.Add("GasTankBlock_Iron");
        GameplayTestFluidNetworks.AddGas(tank, FluidIds.Oxygen, 20);
        var inventory = GetFluidInventory(tank);
        double before = inventory.State.TotalInternalEnergyJoules;
        fixture.Step();
        return new JObject { ["beforeEnergy"] = before, ["afterEnergy"] = inventory.State.TotalInternalEnergyJoules, ["gasLiters"] = GameplayTestFluidNetworks.Gas(tank) };
    }

    internal static JObject CaptureGameplayFurnaceAmbientHeat()
    {
        using var fixture = new GameplayFluidScope(surface: true);
        MachineEntity node = fixture.Add("BlastFurnace");
        FurnaceLogic furnace = node.Logic as FurnaceLogic ?? throw new InvalidOperationException("正式高炉领域尚未建立。");
        TemperatureMgr temperature = TemperatureMgr.Instance ?? throw new InvalidOperationException("当地环境温度尚未就绪。");
        object owner = new();
        try
        {
            if (!temperature.SetLocalTemperatureSource(owner, node.Position, .75f, 500f))
                throw new InvalidOperationException("独立炉体当地环境来源登记失败。");
            furnace.Data.IsSmelting = false;
            var samples = new JArray();
            foreach (float current in new[] { 1000f, 0f, 500.5f })
            {
                furnace.Data.Temperature = current;
                float ambient = SampleAmbientTemperature(node);
                furnace.Tick(0f);
                float zero = furnace.Data.Temperature;
                furnace.Tick(1f);
                samples.Add(new JObject { ["before"] = current, ["ambient"] = ambient,
                    ["zero"] = zero, ["after"] = furnace.Data.Temperature, ["burning"] = furnace.IsBurning });
            }
            return new JObject { ["samples"] = samples };
        }
        finally { temperature.RemoveLocalTemperatureSource(owner); }
    }
    #endregion
}
