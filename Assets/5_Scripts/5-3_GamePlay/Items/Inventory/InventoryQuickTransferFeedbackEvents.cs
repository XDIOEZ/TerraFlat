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
        public readonly Vector2? ScreenOrigin;

        public InventoryQuickTransferSignal(Inventory source, int sourceIndex, Inventory target,
            int targetIndex, ItemData item, float amount, bool allowLocalPresentation = true, Vector2? screenOrigin = null)
        {
            Source = source; SourceIndex = sourceIndex; Target = target; TargetIndex = targetIndex;
            Item = item; Amount = amount;
            AllowLocalPresentation = allowLocalPresentation;
            ScreenOrigin = screenOrigin;
        }
    }

    public static class InventoryQuickTransferFeedbackEvents
    {
        #region 已提交转移通知
        public static event Action<InventoryQuickTransferSignal> Committed;
        private static int suppressedPresentationScopes;
        private static ScreenOriginScope screenOriginScope;

        private sealed class ScreenOriginScope : IDisposable
        {
            public readonly Inventory Source;
            public readonly int Index;
            public readonly Vector2 Position;
            private readonly ScreenOriginScope previous;
            private bool disposed;
            public ScreenOriginScope(Inventory source, int index, Vector2 position)
            {
                Source = source; Index = index; Position = position;
                previous = screenOriginScope;
                screenOriginScope = this;
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                screenOriginScope = previous;
            }
        }

        // 甩动只在本次来源事务中携带松手位置，异步请求另存快照，不影响其他槽位。
        public static IDisposable UseScreenOrigin(Inventory source, int index, Vector2 position)
            => new ScreenOriginScope(source, index, position);

        public static Vector2? GetScreenOrigin(Inventory source, int index)
            => screenOriginScope != null && ReferenceEquals(screenOriginScope.Source, source) &&
               screenOriginScope.Index == index && float.IsFinite(screenOriginScope.Position.x) &&
               float.IsFinite(screenOriginScope.Position.y) ? screenOriginScope.Position : null;

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
        private static void ResetStatics() { Committed = null; suppressedPresentationScopes = 0; screenOriginScope = null; }

        // 表现异常只记日志，不能影响已经扣料成功的库存事务。
        public static void Publish(Inventory source, int sourceIndex, Inventory target, int targetIndex,
            ItemData item, float amount, Vector2? screenOrigin = null)
        {
            if (source == null || target == null || item == null || sourceIndex < 0 || targetIndex < 0 ||
                !float.IsFinite(amount) || amount <= 0f || Committed == null) return;
            var signal = new InventoryQuickTransferSignal(source, sourceIndex, target, targetIndex, item, amount,
                suppressedPresentationScopes == 0, screenOrigin ?? GetScreenOrigin(source, sourceIndex));
            foreach (Delegate callback in Committed.GetInvocationList())
            {
                try { ((Action<InventoryQuickTransferSignal>)callback)(signal); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
        #endregion
    }
}
