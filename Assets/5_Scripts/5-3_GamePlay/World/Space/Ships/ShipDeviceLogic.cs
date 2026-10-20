using FlatWorld.Spaceflight;

public sealed class ShipDeviceLogic : MachineLogic
{
    #region 飞船设备与整船规则协作
    private ShipPartConfiguration groundConfiguration;
    private GameRes configurationResources;
    private ulong configurationRevision;
    public ShipDeviceLogic(MachineEntity entity) : base(entity) { }
    public override string Status
    {
        get
        {
            SpaceSession session = SpaceSession.Current;
            string pieceId = Entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (session != null && session.FindPiece(pieceId, out _, out ShipPieceState piece) && !piece.IsOperational)
                return "已瘫痪";
            return Entity.ElectricalPowerRatio >= .999f ? "已供电" : "缺少电力";
        }
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
        string pieceId = Entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (operation == "drive") return session.EnterConsole(actor, pieceId);
        if (operation == "ignite") return session.ToggleIgnition(pieceId);
        return false;
    }
    #endregion
}
