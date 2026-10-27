using FlatWorld.Networking;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 舱门普通交互
        public bool TryToggleDoor(string pieceId) => TryToggleDoor(pieceId, out _);
        public bool TryToggleDoor(string pieceId, out string reason)
        {
            reason = null;
            if (SubmitLocalIntent(SpaceNetworkIntentKind.ToggleDoor, pieceId)) return true;
            if (!GameNetwork.HasStateAuthority) return RejectShipOperation("当前端没有世界操作权限。", out reason);
            if (!FindPiece(pieceId, out _, out ShipPieceState door) || !door.IsAlive)
                return RejectShipOperation("舱门已失效。", out reason);
            if (door.Kind != ShipPieceKind.Door) return RejectShipOperation("操作目标不是舱门。", out reason);
            if (!TryGetShipInteriorItem(pieceId, out Item item) ||
                item.GetComponentInChildren<Mod_Door>(true) is not Mod_Door module)
                return RejectShipOperation("舱门实体尚未就绪。", out reason);
            bool wasOpen = module.Data.IsOpen;
            using (GameplayWorldRegistry.UseWorld(GameplayWorldRegistry.GetItemWorldKey(item)))
                module.OnInteractStart(null);
            return module.Data.IsOpen != wasOpen || RejectShipOperation("舱门通道被占用或舱门已失效。", out reason);
        }
        #endregion
    }
}
