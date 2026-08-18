using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Gameplay.Progress
{
    /// <summary>
    /// 一条成功玩法事务产生的统一进度信号；Amount 表示本次增量，Payload 用于扩展非核心字段。
    /// 信号是只读值对象，订阅方不得借此反向进入原玩法事务。
    /// </summary>
    public readonly struct GameplayProgressSignal
    {
        public GameplayProgressSignal(Player actor, string type, string targetId, float amount, JObject payload = null)
        {
            Actor = actor;
            Type = type;
            TargetId = targetId;
            Amount = amount;
            Payload = payload;
        }

        public Player Actor { get; }
        public string Type { get; }
        public string TargetId { get; }
        public float Amount { get; }
        public JObject Payload { get; }
    }

    /// <summary>内建玩法进度信号类型；扩展系统可直接发布自己的命名空间类型。</summary>
    public static class GameplayProgressTypes
    {
        public const string InventoryOpened = "inventory.opened";
        public const string ItemPickedUp = "item.picked_up";
        public const string CraftSucceeded = "craft.succeeded";
        public const string SmeltSucceeded = "smelt.succeeded";
        public const string BuildingPlaced = "building.placed";
        public const string FireSeedCreated = "fire_seed.created";
        public const string FurnaceIgnited = "furnace.ignited";
    }

    /// <summary>
    /// 玩法系统在最终成功事务后发布进度事件；保留强类型事件兼容现有引导，并提供统一信号给任务等扩展系统。
    /// 每个订阅者独立捕获异常，确保进度监听失败不会撤销已经成功的玩法事务。
    /// </summary>
    public static class GameplayProgressEvents
    {
        public const string DimensionIdPayloadKey = "dimensionId";

        #region 事件

        public static event Action<Player> InventoryOpened;
        public static event Action<Player, string> PickupSucceeded;
        public static event Action<Player, string> CraftSucceeded;
        public static event Action<Player, string> SmeltSucceeded;
        public static event Action<Player, string> BuildingPlaced;
        public static event Action<Player, string> FireSeedCreated;
        public static event Action<Player, string> FurnaceIgnited;
        public static event Action<GameplayProgressSignal> SignalPublished;

        #endregion

        #region 静态生命周期

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            InventoryOpened = null;
            PickupSucceeded = null;
            CraftSucceeded = null;
            SmeltSucceeded = null;
            BuildingPlaced = null;
            FireSeedCreated = null;
            FurnaceIgnited = null;
            SignalPublished = null;
        }

        #endregion

        #region 发布

        public static void PublishInventoryOpened(Player actor)
        {
            if (actor == null)
                return;

            InvokeSafely(InventoryOpened, actor);
            PublishSignal(new GameplayProgressSignal(actor, GameplayProgressTypes.InventoryOpened, null, 1f));
        }

        public static void PublishPickupSucceeded(
            Player actor,
            string itemId,
            float amount = 1f,
            string dimensionId = null)
        {
            if (actor == null || string.IsNullOrWhiteSpace(itemId))
                return;

            InvokeSafely(PickupSucceeded, actor, itemId);
            JObject payload = string.IsNullOrWhiteSpace(dimensionId)
                ? null
                : new JObject { [DimensionIdPayloadKey] = dimensionId.Trim() };
            PublishSignal(new GameplayProgressSignal(
                actor,
                GameplayProgressTypes.ItemPickedUp,
                itemId,
                amount,
                payload));
        }

        public static void PublishCraftSucceeded(Player actor, string outputItemId, float amount = 1f)
        {
            if (actor == null || string.IsNullOrWhiteSpace(outputItemId))
                return;

            InvokeSafely(CraftSucceeded, actor, outputItemId);
            PublishSignal(new GameplayProgressSignal(actor, GameplayProgressTypes.CraftSucceeded, outputItemId, amount));
        }

        /// <summary>仅在熔炼产物已写入输出库存后发布，失败、烧焦和预览都不得调用。</summary>
        public static void PublishSmeltSucceeded(Player actor, string outputItemId, float amount = 1f)
        {
            if (actor == null || string.IsNullOrWhiteSpace(outputItemId))
                return;

            InvokeSafely(SmeltSucceeded, actor, outputItemId);
            PublishSignal(new GameplayProgressSignal(actor, GameplayProgressTypes.SmeltSucceeded, outputItemId, amount));
        }

        public static void PublishBuildingPlaced(Player actor, string buildingId, float amount = 1f)
        {
            if (actor == null || string.IsNullOrWhiteSpace(buildingId))
                return;

            InvokeSafely(BuildingPlaced, actor, buildingId);
            PublishSignal(new GameplayProgressSignal(actor, GameplayProgressTypes.BuildingPlaced, buildingId, amount));
        }

        public static void PublishFireSeedCreated(Player actor, string itemId, float amount = 1f)
        {
            if (actor == null || string.IsNullOrWhiteSpace(itemId))
                return;

            InvokeSafely(FireSeedCreated, actor, itemId);
            PublishSignal(new GameplayProgressSignal(actor, GameplayProgressTypes.FireSeedCreated, itemId, amount));
        }

        public static void PublishFurnaceIgnited(Player actor, string furnaceId, float amount = 1f)
        {
            if (actor == null || string.IsNullOrWhiteSpace(furnaceId))
                return;

            InvokeSafely(FurnaceIgnited, actor, furnaceId);
            PublishSignal(new GameplayProgressSignal(actor, GameplayProgressTypes.FurnaceIgnited, furnaceId, amount));
        }

        /// <summary>扩展玩法发布统一信号的公共入口。</summary>
        public static void PublishSignal(GameplayProgressSignal signal)
        {
            if (signal.Actor == null || string.IsNullOrWhiteSpace(signal.Type) || signal.Amount <= 0f)
                return;

            InvokeSafely(SignalPublished, signal);
        }

        #endregion

        #region 安全分发

        private static void InvokeSafely<T>(Action<T> handlers, T argument)
        {
            if (handlers == null)
                return;

            foreach (Delegate callback in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<T>)callback)(argument);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private static void InvokeSafely<TFirst, TSecond>(
            Action<TFirst, TSecond> handlers,
            TFirst first,
            TSecond second)
        {
            if (handlers == null)
                return;

            foreach (Delegate callback in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<TFirst, TSecond>)callback)(first, second);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        #endregion
    }
}
