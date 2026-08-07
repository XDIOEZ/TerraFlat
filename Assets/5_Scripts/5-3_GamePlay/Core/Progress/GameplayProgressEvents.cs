using System;

namespace FlatWorld.Gameplay.Progress
{
    /// <summary>
    /// 玩法系统在最终成功事务后发布的通用进度事件；订阅方不得反向进入玩法事务。
    /// </summary>
    public static class GameplayProgressEvents
    {
        #region 事件

        public static event Action<Player> InventoryOpened;
        public static event Action<Player, string> PickupSucceeded;
        public static event Action<Player, string> CraftSucceeded;
        public static event Action<Player, string> BuildingPlaced;
        public static event Action<Player, string> FireSeedCreated;
        public static event Action<Player, string> FurnaceIgnited;

        #endregion

        #region 发布

        public static void PublishInventoryOpened(Player actor)
        {
            if (actor != null)
                InventoryOpened?.Invoke(actor);
        }

        public static void PublishPickupSucceeded(Player actor, string itemId)
        {
            if (actor != null && !string.IsNullOrWhiteSpace(itemId))
                PickupSucceeded?.Invoke(actor, itemId);
        }

        public static void PublishCraftSucceeded(Player actor, string outputItemId)
        {
            if (actor != null && !string.IsNullOrWhiteSpace(outputItemId))
                CraftSucceeded?.Invoke(actor, outputItemId);
        }

        public static void PublishBuildingPlaced(Player actor, string buildingId)
        {
            if (actor != null && !string.IsNullOrWhiteSpace(buildingId))
                BuildingPlaced?.Invoke(actor, buildingId);
        }

        public static void PublishFireSeedCreated(Player actor, string itemId)
        {
            if (actor != null && !string.IsNullOrWhiteSpace(itemId))
                FireSeedCreated?.Invoke(actor, itemId);
        }

        public static void PublishFurnaceIgnited(Player actor, string furnaceId)
        {
            if (actor != null && !string.IsNullOrWhiteSpace(furnaceId))
                FurnaceIgnited?.Invoke(actor, furnaceId);
        }

        #endregion
    }
}
