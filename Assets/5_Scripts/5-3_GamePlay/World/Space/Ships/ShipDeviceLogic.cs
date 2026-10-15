using FlatWorld.Spaceflight;

public sealed class ShipDeviceLogic : MachineLogic
{
    #region 飞船设备与整船规则协作
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
