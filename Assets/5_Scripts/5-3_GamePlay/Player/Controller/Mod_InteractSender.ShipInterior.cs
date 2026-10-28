using UnityEngine;

public partial class Mod_InteractSender
{
    #region 船内交互隔离
    private System.IDisposable UseActorInteractionWorld()
    {
        string worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
        return string.IsNullOrEmpty(worldKey) ? null : SpaceMgr.IsSpaceScene(worldKey)
            ? GameplayWorldRegistry.UseSpaceScene(worldKey) : GameplayWorldRegistry.UseWorld(worldKey);
    }

    private bool IsShipInteriorInteractionOwner
        => item is Player player && FlatWorld.Spaceflight.SpaceSession.Current?.IsPlayerInShipInteriorWorld(player) == true;

    private float MeasureActorInteractionDistance(Vector3 origin, Vector3 target)
        => IsShipInteriorInteractionOwner
            ? Vector2.Distance(origin, target)
            : WorldTopologyRuntime.Distance(origin, target);
    #endregion
}
