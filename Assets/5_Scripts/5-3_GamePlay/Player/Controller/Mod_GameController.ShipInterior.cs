using UnityEngine;
using FlatWorld.Spaceflight;

public partial class Mod_GameController
{
    #region 船内指针坐标
    private Vector3 ConvertDisplayedPointerToItemWorld(Vector3 displayedPosition)
    {
        if (item is Player player && SpaceSession.Current != null &&
            SpaceSession.Current.IsPlayerInShipInteriorWorld(player, out ShipState ship, out _))
        {
            Vector2 local = SpaceSession.Current.UnprojectShipInteriorPosition(ship, displayedPosition);
            return new Vector3(local.x, local.y, 0f);
        }
        return SpaceSession.Current?.IsSpaceView == true
            ? displayedPosition : WorldTopologyRuntime.NormalizePosition(displayedPosition);
    }
    #endregion
}
