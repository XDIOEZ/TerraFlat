using UnityEngine;

public sealed partial class GMReflectionConsole
{
    #region 批量火场调试
    public int IgniteWorldFireAroundPlayer(int sideLength = 10)
    {
        Player player = ItemMgr.GetExistingInstance()?.User_Player;
        if (player == null) return 0;
        Vector2Int center = Vector2Int.FloorToInt(WorldLocalPresentation.ToLogical(player.transform.position));
        return WorldFireSystem.IgniteWorldFireArea(GameplayWorldRegistry.GetItemWorldKey(player),
            center - Vector2Int.one * (sideLength / 2), Vector2Int.one * sideLength);
    }

    public int ExtinguishWorldFireAroundPlayer(int sideLength = 100)
    {
        Player player = ItemMgr.GetExistingInstance()?.User_Player;
        if (player == null) return 0;
        string key = GameplayWorldRegistry.GetItemWorldKey(player);
        Vector2Int center = Vector2Int.FloorToInt(WorldLocalPresentation.ToLogical(player.transform.position));
        return WorldFireSystem.ExtinguishWorldFireArea(key, center - Vector2Int.one * (sideLength / 2), Vector2Int.one * sideLength);
    }
    #endregion
}
