using System;
using UnityEngine;

namespace FlatWorld.Gameplay.InventoryFeedback
{
    /// <summary>玩家主动库存交互的听觉语义，不携带音频资源或播放状态。</summary>
    public enum InventoryAudioAction { Pickup, Backpack, Chest, Swap }

    public readonly struct InventoryAudioSignal
    {
        public readonly ItemData Item;
        public readonly float Amount;
        public readonly InventoryAudioAction Action;

        public InventoryAudioSignal(ItemData item, float amount, InventoryAudioAction action)
        {
            Item = item;
            Amount = amount;
            Action = action;
        }
    }

    public static class InventoryAudioFeedbackEvents
    {
        #region 事务通知
        public static event Action<InventoryAudioSignal> Committed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Committed = null;

        // 监听方只能播放反馈；即使音效异常也不能撤销已经成功的库存事务。
        public static void Publish(ItemData item, float amount, InventoryAudioAction action)
        {
            if (item?.Stack == null || !float.IsFinite(amount) || amount <= 0f || Committed == null)
                return;
            InventoryAudioSignal signal = new InventoryAudioSignal(item, amount, action);
            foreach (Delegate callback in Committed.GetInvocationList())
            {
                try { ((Action<InventoryAudioSignal>)callback)(signal); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
        #endregion
    }

    /// <summary>仅供已提交的装备槽变更发布穿戴或卸下结果。</summary>
    public static class EquipmentAudioFeedbackEvents
    {
        #region 装备通知
        public static event Action<Player, bool> Committed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Committed = null;

        public static void Publish(Player actor, bool equipped)
        {
            if (actor == null || Committed == null)
                return;
            foreach (Delegate callback in Committed.GetInvocationList())
            {
                try { ((Action<Player, bool>)callback)(actor, equipped); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
        #endregion
    }
}
