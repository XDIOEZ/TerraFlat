using System;
using UnityEngine;

namespace FlatWorld.Gameplay.InventoryFeedback
{
    /// <summary>快速转移已提交的单个收货槽，用于独立表现与权威回执。</summary>
    public readonly struct InventoryQuickTransferSignal
    {
        public readonly Inventory Source, Target;
        public readonly int SourceIndex, TargetIndex;
        public readonly ItemData Item;
        public readonly float Amount;
        public readonly bool AllowLocalPresentation;

        public InventoryQuickTransferSignal(Inventory source, int sourceIndex, Inventory target,
            int targetIndex, ItemData item, float amount, bool allowLocalPresentation = true)
        {
            Source = source; SourceIndex = sourceIndex; Target = target; TargetIndex = targetIndex;
            Item = item; Amount = amount;
            AllowLocalPresentation = allowLocalPresentation;
        }
    }

    public static class InventoryQuickTransferFeedbackEvents
    {
        #region 已提交转移通知
        public static event Action<InventoryQuickTransferSignal> Committed;
        private static int suppressedPresentationScopes;

        private sealed class PresentationScope : IDisposable
        {
            private bool disposed;
            public PresentationScope() => suppressedPresentationScopes++;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                suppressedPresentationScopes--;
            }
        }

        // 服务端执行远端玩家命令仍采集回执，但不在Host本机冒出动画。
        public static IDisposable SuppressLocalPresentation() => new PresentationScope();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Committed = null; suppressedPresentationScopes = 0; }

        // 表现异常只记日志，不能影响已经扣料成功的库存事务。
        public static void Publish(Inventory source, int sourceIndex, Inventory target, int targetIndex,
            ItemData item, float amount)
        {
            if (source == null || target == null || item == null || sourceIndex < 0 || targetIndex < 0 ||
                !float.IsFinite(amount) || amount <= 0f || Committed == null) return;
            var signal = new InventoryQuickTransferSignal(source, sourceIndex, target, targetIndex, item, amount,
                suppressedPresentationScopes == 0);
            foreach (Delegate callback in Committed.GetInvocationList())
            {
                try { ((Action<InventoryQuickTransferSignal>)callback)(signal); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
        #endregion
    }
}
