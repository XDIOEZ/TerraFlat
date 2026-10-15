using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>隔离玩法世界的 GM 机械工厂：真实放置、真实机械求解、真实库存和环境，不直接伪造 RPM。</summary>
public static class GameplayTestMechanicalPlant
{
    #region 工厂布局与生命周期

    private readonly struct Part
    {
        public readonly string Name;
        public readonly string Id;
        public readonly int X, Y, Rotation;
        public Part(string name, string id, int x, int y, int rotation = 0)
        { Name = name; Id = id; X = x; Y = y; Rotation = rotation; }
    }

    // 四个动力分支共处一间工厂；Layer1 跨轴器从竖轴上方通过横轴。
    private static readonly Part[] Layout =
    {
        new("wind", "Windmill", 0, 0),
        new("generator", "Generator_Basic", -1, 0),
        new("wire", "Wire_Copper", -2, 0),
        new("battery_wire", "Wire_Copper", -3, 0),
        new("battery", "Battery_Basic", -3, 0),
        new("wind_shaft", "Shaft_Wood", 1, 0),
        new("clutch", "Clutch", 2, 0),
        new("clutch_shaft", "Shaft_Wood", 3, 0),
        new("gearbox", "Gearbox_Wood", 4, 0),
        new("loom_shaft", "Shaft_Wood", 5, 0),
        new("loom", "MechanicalLoom", 6, 0),
        new("gear_in", "Gear_Wood", 0, 1, 1),
        new("gear_mid", "Gear_Wood", 0, 2),
        new("gear_out", "Gear_Wood", 1, 2, 2),
        new("gear_shaft", "Shaft_Wood", 2, 2),
        new("sawmill", "Sawmill", 3, 2),
        new("conveyor_wood", "Conveyor_Wood", 4, 2),
        new("conveyor_cloth", "Conveyor_Cloth", 5, 2),
        new("belt_crank", "HandCrank", 4, 3, 1),
        new("water", "WaterWheel", 0, 4),
        new("water_shaft", "Shaft_Wood", 1, 4),
        new("probe", "PressureProbe_Mechanical", 1, 4),
        new("probe_pipe", "FluidPipe", 1, 4),
        new("cross_under", "Shaft_Wood", 2, 4, 1),
        new("cross", "CrossShaft", 2, 4),
        new("pump", "GasPump_Mechanical", 3, 4),
        new("pump_pipe", "FluidPipe", 4, 4),
        new("pump_outlet", "FluidOutlet", 5, 4),
        new("pump_tank", "GasTankBlock_Iron", 6, 4),
        new("vertical_crank", "HandCrank", 2, 3, 1),
        new("millstone", "Millstone", 2, 5, 1),
        new("engine", "GasEngine", 0, 7),
        new("engine_shaft", "Shaft_Wood", 1, 7),
        new("bellows", "MechanicalBellows", 2, 7),
        new("hammer_crank", "HandCrank", 0, 9),
        new("hammer_shaft", "Shaft_Wood", 1, 9),
        new("hammer", "MechanicalHammer", 2, 9),
        new("compressor_crank", "HandCrank", 0, 11),
        new("compressor_shaft", "Shaft_Wood", 1, 11),
        new("compressor", "GasCompressor_Mechanical", 2, 11)
    };

    private static readonly Dictionary<string, MachineEntity> parts = new(StringComparer.Ordinal);
    private static readonly List<int> owned = new();
    private static Player owner;
    private static Vector3 ownerStart;
    private static IDisposable ownerWindShield;
    private static Vector2Int anchor;
    private static Vector2Int waterCell;
    private static string originalLiquid;
    private static float originalLiquidDepth;
    private static float[] originalRiver;
    private static bool environmentChanged;
    private static float originalWind;
    private static bool windChanged;
    private static bool built;
    private static string lastError;
    private static DroppedItemHandle conveyorDrop;
    private static Vector2 conveyorDropStart;
    private const decimal ProbeTestMoles = 0.195m;

    public static bool TryBuild(Player player, out string message)
    {
        message = string.Empty;
        if (player == null || !player.IsLocalProfile || GameManager.Instance?.IsGameplayReady != true)
        { message = "机械工厂要求已进入独立的真实测试世界。"; return false; }
        if (built) { message = "机械工厂已经创建。"; return true; }
        Reset();
        owner = player;
        ownerStart = player.transform.position;
        ownerWindShield = player.GetComponentInChildren<Mod_Mover>(true)?.SuppressWindMotion();
        Vector2 dimensions = ChunkMgr.GetChunkSize();
        int width = Mathf.RoundToInt(dimensions.x), height = Mathf.RoundToInt(dimensions.y);
        if (width < 14 || height < 16)
        { message = "机械工厂需要至少 14×16 的已加载区块。"; Reset(); return false; }
        // 工厂仅在远离主角的已加载区块搭建，避免机器碰撞与测试水流推走玩家。
        if (!TryFindSafeAnchor(player.transform.position, width, height, out anchor))
        { message = "附近没有远离主角的已加载区块，拒绝在主角身边搭建测试工厂。"; Reset(); return false; }
        waterCell = anchor + new Vector2Int(0, 4);
        try
        {
            PrepareRiver();
            WeatherMgr weather = WeatherMgr.ExistingInstance;
            if (weather != null)
            {
                originalWind = weather.CurrentWindStrength;
                // 风机接受完整真实风力，主角通过独立可释放防风租约避免被试验强风推走。
                weather.SetWindStrength(1f);
                windChanged = true;
            }
            foreach (Part part in Layout) Place(part);
            FuelEngine();
            AddInput("millstone", "Ore_Copper", 2);
            AddInput("loom", "Cotton", 4);
            AddInput("sawmill", "Log", 2);
            AddInput("hammer", "Ingot_Copper", 2);
            AddGas(parts["compressor"], "main", FluidIds.Nitrogen, 0.2m);
            built = true;
            message = $"GM 工厂建造完成：{owned.Count} 个真实节点，涵盖 {parts.Values.Select(n => n.Definition.Id).Distinct().Count()} 种机械与附属设备；水车使用真实液层河流，风机使用权威天气。";
            return true;
        }
        catch (Exception exception)
        {
            message = "机械工厂建造失败：" + exception;
            Reset();
            return false;
        }
    }

    private static bool TryFindSafeAnchor(Vector3 position, int width, int height, out Vector2Int result)
    {
        Vector2Int cell = MachineWorld.CellOf(position);
        int chunkX = Mathf.FloorToInt((float)cell.x / width);
        int chunkY = Mathf.FloorToInt((float)cell.y / height);
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        for (int radius = 1; radius <= 4; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;
                Vector2Int candidate = new((chunkX + dx) * width + 4, (chunkY + dy) * height + 2);
                Vector2 factoryCenter = new(candidate.x + 1.5f, candidate.y + 5.5f);
                if (WorldTopologyRuntime.Distance(position, factoryCenter) < 20f) continue;
                if (chunks == null || !chunks.TryGetRuntimeTerrainTile(
                        new Vector2(candidate.x + .5f, candidate.y + 4.5f), out _)) continue;
                result = candidate;
                return true;
            }
        }
        result = default;
        return false;
    }

    private static void Place(Part part)
    {
        if (parts.ContainsKey(part.Name)) throw new InvalidOperationException("布局代号重复：" + part.Name);
        if (GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(part.Id, out RuntimeItemDefinition definition))
            throw new InvalidOperationException("未登记机械物品：" + part.Id);
        ItemData data = definition.CreateItemData();
        data.Guid = ItemMgr.GetInstance().GenerateGuid();
        data.transform ??= new ItemTransform();
        data.transform.position = new Vector3(anchor.x + part.X + .5f, anchor.y + part.Y + .5f);
        data.transform.rotation = Quaternion.identity;
        data.transform.scale = Vector3.one;
        data.inHand = false;
        Mod_Building.SetInstalledDataState(data);
        MachineState state = MachineWorld.ReadMachineState(data);
        state.RotationQuarterTurns = part.Rotation;
        MachineWorld.WriteMachineState(data, state);
        MachineEntity node = MachineWorld.Place(data);
        owned.Add(node.Id);
        parts.Add(part.Name, node);
    }

    private static void PrepareRiver()
    {
        Vector2 target = new(waterCell.x + .5f, waterCell.y + .5f);
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(target, out RuntimeTerrainTileSample sample))
            throw new InvalidOperationException("水车位置的权威地块尚未加载。");
        originalLiquid = sample.LiquidId;
        originalLiquidDepth = sample.LiquidDepth;
        string[] names = { "riverKind", "riverFlowX", "riverFlowY", "riverFlow" };
        originalRiver = new float[names.Length];
        for (int i = 0; i < names.Length; i++)
            sample.Terrain.TryGetEnvironmentValue(names[i], sample.LocalCell.x, sample.LocalCell.y, out originalRiver[i]);
        if (!WorldLiquidSystem.TrySet(sample, "core:pure_water", 1f))
            throw new InvalidOperationException("GM 无法通过正式液体 API 建立测试水道。");
        environmentChanged = true;
        float[] values = { 1f, 1f, 0f, 3f };
        for (int i = 0; i < names.Length; i++)
            sample.Terrain.SetEnvironmentValue(names[i], sample.LocalCell.x, sample.LocalCell.y, values[i]);
        if (!manager.TryGetRuntimeWaterCurrent(target, out _) ||
            !MachineWorld.IsWaterCell(waterCell))
            throw new InvalidOperationException("测试水道未产生权威河流流速。");
    }

    private static void FuelEngine()
    {
        MachineEntity engine = parts["engine"];
        AddGas(engine, "main", "core:hydrogen", 2m);
        AddGas(engine, "oxygen", "core:oxygen", 1m);
        MachineWorld.StateChanged(engine);
    }

    private static void AddGas(MachineEntity node, string chamber, string fluidId, decimal moles)
    {
        FluidInventory inventory = MachineWorld.GetFluidInventory(node, chamber);
        var fluid = FluidCatalog.Default.Find(fluidId);
        if (!inventory.TryAdd(FluidInventory.CreateBatch(fluid, moles, 0m, 293.15),
                MachineWorld.GetFluidVolumeLiters(node), MachineWorld.GetFluidMinimumGasSpaceLiters(node)))
            throw new InvalidOperationException("GM 无法注入真实气体：" + fluidId);
    }

    private static void AddInput(string alias, string id, float amount)
    {
        MachineEntity node = parts[alias];
        if (node.Processor == null || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(id, out RuntimeItemDefinition definition))
            throw new InvalidOperationException(alias + " 缺少加工器或输入定义：" + id);
        ItemData material = definition.CreateItemData();
        material.Stack.Amount = amount;
        node.Processor.Input.Data.SetOne_ItemData(0, material);
    }

    public static void Reset()
    {
        if (conveyorDrop.IsValid) DroppedItemService.Remove(conveyorDrop);
        conveyorDrop = default;
        for (int index = owned.Count - 1; index >= 0; index--)
        {
            try { if (MachineWorld.GetById(owned[index]) != null) MachineWorld.Remove(owned[index]); }
            catch (Exception exception) { Debug.LogWarning("[GameplayTestMechanicalPlant] 清理节点失败：" + exception.Message); }
        }
        parts.Clear(); owned.Clear();
        if (environmentChanged && ChunkMgr.ExistingInstance != null &&
            ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(
                new Vector2(waterCell.x + .5f, waterCell.y + .5f), out RuntimeTerrainTileSample sample))
        {
            string[] names = { "riverKind", "riverFlowX", "riverFlowY", "riverFlow" };
            for (int i = 0; i < names.Length; i++)
                sample.Terrain.SetEnvironmentValue(names[i], sample.LocalCell.x, sample.LocalCell.y, originalRiver[i]);
            WorldLiquidSystem.TrySet(sample, originalLiquid, originalLiquidDepth);
        }
        if (windChanged && WeatherMgr.ExistingInstance != null)
            WeatherMgr.ExistingInstance.SetWindStrength(originalWind);
        ownerWindShield?.Dispose(); ownerWindShield = null;
        owner = null; ownerStart = default; built = false; windChanged = false; environmentChanged = false;
        lastError = string.Empty;
    }

    #endregion

    #region GM 动作

    public static bool TryOperate(string operation, out string message)
    {
        message = string.Empty;
        if (!built) { message = "机械工厂尚未创建。"; return false; }
        switch (operation)
        {
            case "mechanical_start":
                foreach (string alias in new[] { "vertical_crank", "hammer_crank", "compressor_crank", "belt_crank" })
                {
                    MachineEntity crank = parts[alias];
                    if (!MachineWorld.Execute(crank, "crank", null, owner))
                    { message = "手摇动力启动失败：" + alias; return false; }
                    // GM 延长按住时长；扭矩/RPM 仍由生产机械求解器计算。
                    crank.State.ManualSeconds = 85f;
                    MachineWorld.StateChanged(crank);
                }
                message = "GM 已启动四条真实手摇动力支路。";
                return true;
            case "mechanical_clutch_off":
            case "mechanical_clutch_on":
                MachineEntity clutch = parts["clutch"];
                bool engaged = operation == "mechanical_clutch_on";
                if (clutch.Engaged != engaged && !MachineWorld.Execute(clutch, "work", null, owner))
                { message = "离合器操作失败。"; return false; }
                message = engaged ? "离合器闭合。" : "离合器断开。";
                return true;
            case "mechanical_stone_disconnect":
                MachineEntity upstream = parts["vertical_crank"];
                upstream.State.ManualSeconds = 0f;
                MachineWorld.StateChanged(upstream);
                message = "已释放石磨的外部手摇传动。";
                return true;
            case "mechanical_stone_push":
                bool pushed = MachineWorld.Execute(parts["millstone"], "work", null, owner);
                message = pushed ? "已通过正式机械操作手动推动石磨。" : "石磨拒绝手推操作。";
                return pushed;
            case "mechanical_clear":
                Reset();
                message = "已拆除测试机械工厂并恢复水流与风力条件。";
                return true;
            case "mechanical_diagnose":
                string[] aliases = { "water", "water_shaft", "probe", "cross", "pump", "wind", "gear_mid", "sawmill", "conveyor_wood" };
                var diagnostics = new List<string>();
                foreach (string alias in aliases)
                {
                    MachineEntity node = parts[alias];
                    string links = string.Join(",", node.Links.Select(link => link.Target.Definition.Id + "@" + link.Target.Cell));
                    diagnostics.Add($"{alias}: RPM={node.Rpm:0.##}, source={node.SourceFactor:0.###}, load={node.LoadSatisfied}, availableTorque={node.AvailableTorque:0.#}, net={node.Network?.Status ?? "none"}, links=[{links}]");
                }
                message = string.Join(" | ", diagnostics);
                return true;
            case "mechanical_probe_high":
                FluidInventory pressurePipe = MachineWorld.GetFluidInventory(parts["probe_pipe"]);
                if (!pressurePipe.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Nitrogen),
                        ProbeTestMoles, 0m, 293.15),
                        MachineWorld.GetFluidVolumeLiters(parts["probe_pipe"]),
                        MachineWorld.GetFluidMinimumGasSpaceLiters(parts["probe_pipe"])))
                { message = "无法向真实测压管道注入试验气体。"; return false; }
                MachineWorld.StateChanged(parts["probe_pipe"]);
                message = "已向真实管道施加 450~500kPa 的试验气压。";
                return true;
            case "mechanical_probe_low":
                FluidInventory depressurized = MachineWorld.GetFluidInventory(parts["probe_pipe"]);
                if (!depressurized.TryTakeExact(FluidIds.Nitrogen, FluidPhase.Gas, ProbeTestMoles, out _))
                { message = "试验管道中的气体无法按量排出。"; return false; }
                MachineWorld.StateChanged(parts["probe_pipe"]);
                message = "已将测压管道泄压至零。";
                return true;
            case "mechanical_conveyor_drop":
                if (conveyorDrop.IsValid) DroppedItemService.Remove(conveyorDrop);
                MachineEntity conveyor = parts["conveyor_wood"];
                conveyorDropStart = conveyor.Cell + new Vector2(.5f, .5f);
                if (GameRes.ExistingInstance == null ||
                    !GameRes.ExistingInstance.TryGetItemDefinition("Ore_Copper", out RuntimeItemDefinition mineral))
                { message = "缺少输送带测试物品 Ore_Copper。"; return false; }
                ItemData itemData = mineral.CreateItemData();
                itemData.Guid = ItemMgr.GetInstance().GenerateGuid();
                itemData.Stack.Amount = 1f;
                conveyorDrop = DroppedItemService.Spawn(itemData, conveyorDropStart,
                    destination: conveyorDropStart, randomizeRotation: false);
                message = conveyorDrop.IsValid ? "GM 已向运行中的木制输送带放置真实掉落物。" : "输送带掉落物创建失败。";
                return conveyorDrop.IsValid;
            default:
                message = "未知机械 GM 操作：" + operation;
                return false;
        }
    }

    #endregion

    #region 权威状态检查

    public static bool TryCheck(string name, out bool passed)
    {
        passed = false;
        if (name == "mechanical_plant_cleared") { passed = !built && owned.Count == 0; return true; }
        if (!built) return name.StartsWith("mechanical_", StringComparison.Ordinal);
        switch (name)
        {
            case "mechanical_player_stationary":
                passed = owner != null && WorldTopologyRuntime.Distance(ownerStart, owner.transform.position) < .25f;
                return true;
            case "mechanical_catalog_coverage":
                TextAsset catalog = Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog");
                if (catalog == null) return true;
                var definitions = JObject.Parse(catalog.text)["Nodes"] as JArray;
                var ids = new HashSet<string>(parts.Values.Select(p => p.Definition.Id), StringComparer.Ordinal);
                passed = definitions != null && definitions.OfType<JObject>()
                    .Where(n => n.Value<string>("Ports") == "axis" || n.Value<string>("Ports") == "all")
                    .All(n => ids.Contains(n.Value<string>("Id")));
                return true;
            case "mechanical_plant_placed":
                passed = parts.Count == Layout.Length &&
                    parts.Values.All(n => ReferenceEquals(MachineWorld.GetById(n.Id), n));
                return true;
            case "mechanical_water_current":
                passed = MachineWorld.GetSourceFactor(parts["water"]) > 0f &&
                    MachineWorld.GetSourceRpm(parts["water"]) > 0f;
                return true;
            case "mechanical_wind_source":
                passed = MachineWorld.GetSourceFactor(parts["wind"]) > 0.01f;
                return true;
            case "mechanical_engine_fueled":
                passed = MachineWorld.GetFluidInventory(parts["engine"]).GetGasMoles("core:hydrogen") > 0 &&
                    MachineWorld.GetFluidInventory(parts["engine"], "oxygen").GetGasMoles("core:oxygen") > 0;
                return true;
            case "mechanical_clutch_open":
                passed = !parts["clutch"].Engaged && parts["loom"].SpeedRpm < .01f;
                return true;
            case "mechanical_clutch_closed":
                passed = parts["clutch"].Engaged && parts["loom"].SpeedRpm > .01f;
                return true;
            case "mechanical_gearbox_ratio":
                passed = parts["gearbox"].SpeedRpm > 0f && parts["loom"].SpeedRpm > 0f &&
                    Mathf.Abs(parts["loom"].SpeedRpm - parts["gearbox"].SpeedRpm * 2f) < .5f;
                return true;
            case "mechanical_gears_reverse":
                passed = parts["gear_mid"].Rpm != 0 && parts["gear_out"].Rpm != 0 &&
                    Mathf.Sign(parts["gear_mid"].Rpm) != Mathf.Sign(parts["gear_out"].Rpm);
                return true;
            case "mechanical_cross_isolated":
                passed = parts["cross"].Network != null && parts["cross_under"].Network != null &&
                    !ReferenceEquals(parts["cross"].Network, parts["cross_under"].Network);
                return true;
            case "mechanical_probe_disconnected":
                passed = !parts["probe"].FluidProbeConnected &&
                    MachineWorld.GetPressureProbeReading(parts["probe"]) > 450 &&
                    parts["pump"].SpeedRpm < .01f;
                return true;
            case "mechanical_probe_reconnected":
                passed = parts["probe"].FluidProbeConnected &&
                    MachineWorld.GetPressureProbeReading(parts["probe"]) < 400 &&
                    parts["pump"].SpeedRpm > 0f;
                return true;
            case "mechanical_conveyor_transports_item":
                passed = conveyorDrop.IsValid &&
                    DroppedItemService.TryGetPickablePosition(conveyorDrop, out Vector2 moved) &&
                    WorldTopologyRuntime.ShortestDelta(conveyorDropStart, moved).magnitude > .05f;
                return true;
            case "mechanical_compressor_output":
                passed = MachineWorld.GetFluidInventory(parts["compressor"], "output").GasMoles > 0m;
                return true;
            case "mechanical_pump_delivered":
                passed = MachineWorld.GetFluidInventory(parts["pump_tank"]).GasMoles > 0m;
                return true;
            case "mechanical_stone_external_off":
                passed = parts["millstone"].SpeedRpm < .01f;
                return true;
            case "mechanical_stone_selfpowered":
                passed = parts["millstone"].SpeedRpm > 0f &&
                    parts["millstone"].State.ManualSeconds > 0f &&
                    !parts["millstone"].IncomingPower;
                return true;
            case "mechanical_engine_reaction":
                passed = MachineWorld.GetFluidInventory(parts["engine"], "water").LiquidMoles > 0m &&
                    MachineWorld.GetFluidInventory(parts["engine"]).GetGasMoles(FluidIds.Hydrogen) < 2m;
                return true;
            case "mechanical_manual_millstone":
                passed = parts["millstone"].SpeedRpm > 0 && parts["millstone"].LoadSatisfied;
                return true;
            case "mechanical_bellows_airflow":
                passed = MachineWorld.GetBellowsBoost(new Vector3(anchor.x + 3.5f, anchor.y + 7.5f)) > 0;
                return true;
            case "mechanical_generator_charging":
                passed = parts["generator"].ElectricalNetwork != null &&
                    ReferenceEquals(parts["generator"].ElectricalNetwork, parts["battery"].ElectricalNetwork) &&
                    parts["generator"].ConversionMode == ElectricalConversionMode.Generator &&
                    parts["generator"].ElectricalGeneratedWatts > 0 &&
                    parts["battery"].ElectricalStoredJoules > 0;
                return true;
            case "mechanical_gas_engine_running":
                passed = parts["engine"].SpeedRpm > 0 && parts["bellows"].LoadSatisfied;
                return true;
        }
        const string running = "mechanical_running:";
        if (name.StartsWith(running, StringComparison.Ordinal))
        {
            string alias = name.Substring(running.Length);
            passed = parts.TryGetValue(alias, out MachineEntity node) &&
                node.SpeedRpm > 0f && (node.LoadTorque <= 0f || node.LoadSatisfied);
            return true;
        }
        const string produced = "mechanical_output:";
        if (name.StartsWith(produced, StringComparison.Ordinal))
        {
            string[] tokens = name.Substring(produced.Length).Split(':');
            passed = tokens.Length == 2 && parts.TryGetValue(tokens[0], out MachineEntity node) &&
                node.Processor?.Output.Data.GetItemSlot(0)?.itemData?.IDName == tokens[1];
            return true;
        }
        return false;
    }

    #endregion
}
