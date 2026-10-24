using System.Globalization;
using FlatWorld.Spaceflight;
using Mirror;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkSpaceStateCoordinator
    {
        #region 建造事务发布
        // 船上建筑由正式船体快照接管，不能错误退回普通 Item 或格子发布器。
        public bool TryPublishBuildingPlacement(string shipId, int itemGuid)
        {
            if (!serverStarted || !NetworkServer.active || SpaceSession.Current == null ||
                !SpaceSession.Current.FindPiece(itemGuid.ToString(CultureInfo.InvariantCulture),
                    out ShipState ship, out ShipPieceState piece) || ship.ShipId != shipId || !piece.IsAlive)
                return false;
            nextServerSnapshot = 0f;
            return true;
        }
        #endregion
    }
}
