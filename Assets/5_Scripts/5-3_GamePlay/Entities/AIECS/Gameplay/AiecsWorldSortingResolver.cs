using System;
using UnityEngine.SceneManagement;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>把 GamePlay 世界排序类别注入无 GamePlay 依赖的 ECS 批量表现。</summary>
    internal static class AiecsWorldSortingResolver
    {
        #region 类别桥接
        /// <summary>创建表现并在资源发布时刷新物种排序，释放时撤销订阅。</summary>
        public static AiecsWorldRenderer CreateRenderer(AiecsAnimationCatalog catalog, string[] actorIds, Scene scene)
        {
            GameRes resources = GameRes.ExistingInstance ?? throw new InvalidOperationException("AIECS 表现缺少生物定义目录。");
            WorldRenderingManager manager = WorldRenderingManager.Instance;
            if (manager == null)
                throw new InvalidOperationException("AIECS 表现缺少世界排序管理器。");
            manager.GetSortingKey(WorldSortingManager.GroundShadowCategory,
                out int shadowLayer, out int shadowOrder);
            var renderer = new AiecsWorldRenderer(catalog, actorIds, scene,
                new AiecsWorldSortingKeys(ResolveActorSortingKeys(resources, actorIds), shadowLayer, shadowOrder));
            void Refresh() => renderer.SetActorSortingKeys(ResolveActorSortingKeys(resources, actorIds));
            resources.ResourcesReloaded += Refresh;
            renderer.Disposed += () => resources.ResourcesReloaded -= Refresh;
            return renderer;
        }

        /// <summary>按最终 Actor 定义逐物种解析，未声明的字段使用生物类别默认值。</summary>
        internal static AiecsActorSortingKey[] ResolveActorSortingKeys(GameRes resources, string[] actorIds)
        {
            WorldRenderingManager.Instance.GetSortingKey(WorldSortingManager.CreatureCategory, out int layer, out int order);
            var keys = new AiecsActorSortingKey[actorIds.Length];
            for (int i = 0; i < actorIds.Length; i++)
            {
                if (!resources.TryGetItemDefinition(actorIds[i], out RuntimeItemDefinition definition) || !definition.IsActor)
                    throw new InvalidOperationException("AIECS 找不到当前 Actor 定义：" + actorIds[i]);
                WorldSortingManager.ResolveItemVisualSortingKey(definition.Visual, layer, order, out int actualLayer, out int actualOrder);
                keys[i] = new AiecsActorSortingKey(actualLayer, actualOrder);
            }
            return keys;
        }
        #endregion
    }
}
