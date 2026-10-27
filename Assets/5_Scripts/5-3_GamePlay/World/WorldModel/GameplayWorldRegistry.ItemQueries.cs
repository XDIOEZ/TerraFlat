using System.Collections.Generic;

public static partial class GameplayWorldRegistry
{
    #region 所属世界物品快照查询
    public static void CopyWorldItems(string worldKey, ICollection<Item> destination)
        => worldItems.CopyWorldItems(worldKey, destination);
    #endregion
}
