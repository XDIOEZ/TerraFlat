using UnityEngine;

/// <summary>
/// 由会抬升视觉节点的实体报告源贴图相对地面姿态的世界位移。
/// 阴影系统只读取位移，不修改实体、贴图或碰撞体的 Transform。
/// </summary>
public interface IVisualGroundOffset
{
    /// <summary>源视觉属于本提供者时返回地面到视觉的世界位移。</summary>
    bool TryGetVisualGroundOffset(Transform visual, out Vector3 worldOffset);
}

/// <summary>注册阴影时查找实体的视觉高度提供者，并统一解析当前帧的地面位移。</summary>
public static class VisualGroundOffsetResolver
{
    #region 提供者查找与位移读取
    /// <summary>从实体组件中寻找视觉高度提供者。</summary>
    public static IVisualGroundOffset FindProvider(Item item)
    {
        MonoBehaviour[] components = item.GetComponentsInChildren<MonoBehaviour>(true);
        IVisualGroundOffset first = null;
        System.Collections.Generic.List<IVisualGroundOffset> providers = null;
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is not IVisualGroundOffset provider)
                continue;
            if (first == null)
            {
                first = provider;
                continue;
            }
            providers ??= new System.Collections.Generic.List<IVisualGroundOffset> { first };
            providers.Add(provider);
        }
        // 鸟类起飞与单图小动作分别贡献位移，不能让新提供者遮掉已有飞行高度。
        return providers != null ? new CombinedGroundOffsetProvider(providers.ToArray()) : first;
    }

    /// <summary>读取指定贴图的位移；没有视觉抬升时地面姿态就是当前姿态。</summary>
    public static Vector3 Resolve(IVisualGroundOffset provider, Transform visual)
    {
        return provider != null && provider.TryGetVisualGroundOffset(visual, out Vector3 offset)
            ? offset
            : Vector3.zero;
    }

    private sealed class CombinedGroundOffsetProvider : IVisualGroundOffset
    {
        private readonly IVisualGroundOffset[] providers;

        public CombinedGroundOffsetProvider(IVisualGroundOffset[] providers)
        {
            this.providers = providers;
        }

        public bool TryGetVisualGroundOffset(Transform visual, out Vector3 worldOffset)
        {
            worldOffset = Vector3.zero;
            bool found = false;
            for (int i = 0; i < providers.Length; i++)
            {
                IVisualGroundOffset provider = providers[i];
                if (provider is Object unityProvider && unityProvider == null)
                    continue;
                if (!provider.TryGetVisualGroundOffset(visual, out Vector3 offset))
                    continue;
                worldOffset += offset;
                found = true;
            }
            return found;
        }
    }
    #endregion
}
