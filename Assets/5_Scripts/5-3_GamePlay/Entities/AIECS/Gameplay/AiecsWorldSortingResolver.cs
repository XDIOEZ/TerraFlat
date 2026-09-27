using System;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>把 GamePlay 世界排序类别注入无 GamePlay 依赖的 ECS 批量表现。</summary>
    internal static class AiecsWorldSortingResolver
    {
        #region 类别桥接
        /// <summary>正式世界与开发入口共用同一生物和地表阴影排序键。</summary>
        public static AiecsWorldSortingKeys Resolve()
        {
            WorldSortingManager manager = WorldSortingManager.GetInstance();
            if (manager == null)
                throw new InvalidOperationException("AIECS 表现缺少世界排序管理器。");
            manager.GetSortingKey(WorldSortingManager.CreatureCategory,
                out int actorLayer, out int actorOrder);
            manager.GetSortingKey(WorldSortingManager.GroundShadowCategory,
                out int shadowLayer, out int shadowOrder);
            return new AiecsWorldSortingKeys(actorLayer, actorOrder, shadowLayer, shadowOrder);
        }
        #endregion
    }
}
