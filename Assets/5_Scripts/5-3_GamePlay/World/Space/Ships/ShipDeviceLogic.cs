using FlatWorld.Spaceflight;

public sealed class ShipDeviceLogic : MachineLogic
{
    #region 飞船设备与整船规则协作
    private ShipPartConfiguration groundConfiguration;
    private GameRes configurationResources;
    private ulong configurationRevision;
    private readonly string pieceId;
    public ShipDeviceLogic(MachineEntity entity) : base(entity)
        => pieceId = entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public float RatedPowerWatts => Entity.Definition.Electrical?.PowerWatts ?? 0f;
    public bool HasChemicalPropellantStorage => Entity.Definition.Fluid?.PullFromNetwork == true &&
        System.Array.Exists(Entity.Definition.Fluid.Ports, port => port.Phase == "liquid" && port.FluidId == ShipFuel.LiquidFuelId);
    public float CurrentPowerWatts => IsDeviceRunning && Entity.ElectricalPowerRatio > 0f
        ? UnityEngine.Mathf.Clamp(UnityEngine.Mathf.Min(Entity.ElectricalSuppliedWatts, Entity.ElectricalRequestedWatts), 0f, RatedPowerWatts) : 0f;
    public bool IsDeviceRunning
    {
        get
        {
            if (!(Entity.State?.Engaged ?? Entity.Engaged)) return false;
            SpaceSession session = SpaceSession.Current;
            return session == null || !session.FindPiece(pieceId, out _, out ShipPieceState piece) ||
                piece.IsOperational && session.GetDevice(pieceId)?.Enabled != false;
        }
    }
    public override string Status
    {
        get
        {
            SpaceSession session = SpaceSession.Current;
            if (session != null && session.FindPiece(pieceId, out _, out ShipPieceState piece) && !piece.IsOperational)
                return DescribePower("已瘫痪", false);
            bool running = IsDeviceRunning;
            string power = DescribePower(!running ? "已停机" : Entity.ElectricalPowerRatio >= .999f ? "已供电" : "缺少电力", running);
            if (!HasChemicalPropellantStorage) return power;
            using var nodeScope = MachineWorld.UseNodeScope(Entity);
            FluidInventory contents = MachineWorld.GetFluidInventory(Entity);
            return power + "\n" + FlatWorld.Localization.FlatWorldLocalizationService.GetUiFormat(
                "氢气 {0:0.###} mol / 氧气 {1:0.###} mol\n燃油 {2:0.###} L\n氢氧按 2:1 消耗，或使用燃油和氧气；缺少反应物即停机。",
                contents.GetAvailableMoles(FluidIds.Hydrogen, FluidPhase.Gas),
                contents.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas), contents.GetLiquidLiters());
        }
    }
    private string DescribePower(string state, bool operational)
    {
        // 面板读取电网本轮的实际供电，停机或瘫痪不显示虚假的耗电。
        float rated = RatedPowerWatts;
        float actual = operational ? CurrentPowerWatts : 0f;
        return FlatWorld.Localization.FlatWorldLocalizationService.GetUiFormat(
            "{0}\n额定功率 {1:0.##} W\n当前功率 {2:0.##} W",
            FlatWorld.Localization.FlatWorldLocalizationService.GetUiText(state), rated, actual);
    }
    public override void Capture() { }
    public override void Tick(float seconds)
    {
        // 未装船设备由机器世界正常调度，船上设备继续由整船环境循环推进。
        if (Entity.ScopeKey?.StartsWith("ship:", System.StringComparison.Ordinal) == true) return;
        GameRes resources = GameRes.ExistingInstance;
        ulong revision = resources != null ? resources.ItemDefinitionRevision : 0;
        if (!ReferenceEquals(configurationResources, resources) || configurationRevision != revision || groundConfiguration == null)
        {
            groundConfiguration = SpaceSession.ResolvePartConfiguration(Entity.Snapshot);
            configurationResources = resources; configurationRevision = revision;
        }
        if (groundConfiguration?.SupplyMolesPerSecond > 0f)
            SpaceSession.Current?.SupplyGroundAtmosphere(Entity, groundConfiguration, seconds);
    }
    public override bool Execute(string operation, string argument, Player actor)
    {
        if (Entity.ScopeKey?.StartsWith("ship:", System.StringComparison.Ordinal) != true)
            return operation == "ignite" && ShipGroundAssembly.TryLaunch(actor, Entity.Id, out _, out _);
        SpaceSession session = SpaceSession.Current;
        if (session == null) return false;
        if (operation == "drive") return session.EnterConsole(actor, pieceId);
        if (operation == "ignite") return session.ToggleIgnition(pieceId);
        return false;
    }
    #endregion
}
