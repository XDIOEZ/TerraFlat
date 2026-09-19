using System;
using UnityEngine;

namespace FlatWorld.Gameplay.Building
{
    /// <summary>放置拒绝的稳定语义；玩法不引用气泡或其它表现程序集。</summary>
    public enum BuildingPlacementFailureReason { InvalidPosition, OutOfRange }
    /// <summary>
    /// 发布玩家主动提交的建筑放置反馈；玩法层只描述结果，不依赖具体提示表现。
    /// </summary>
    public static class BuildingPlacementFeedbackEvents
    {
        /// <summary>玩家主动提交的位置未通过建筑放置校验。</summary>
        public static event Action<Player, BuildingPlacementFailureReason> PlacementRejected;

        /// <summary>进入新的运行时会话前清理静态订阅。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PlacementRejected = null;
        }

        /// <summary>向表现层发布一次玩家建筑放置失败反馈。</summary>
        public static void PublishPlacementRejected(Player actor,
            BuildingPlacementFailureReason reason = BuildingPlacementFailureReason.InvalidPosition)
        {
            if (actor == null || PlacementRejected == null)
                return;

            foreach (Delegate callback in PlacementRejected.GetInvocationList())
            {
                try
                {
                    ((Action<Player, BuildingPlacementFailureReason>)callback)(actor, reason);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
