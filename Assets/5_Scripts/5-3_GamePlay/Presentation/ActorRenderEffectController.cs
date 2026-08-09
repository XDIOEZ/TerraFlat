using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通用角色渲染效果控制器。
/// 负责收集角色身体、装备和其他表现 Renderer，并在每帧统一合并效果模块的材质参数。
/// 所有效果共用一次 MaterialPropertyBlock 写入，避免水体、受击、溶解等效果互相覆盖。
/// </summary>
[DisallowMultipleComponent]
public sealed class ActorRenderEffectController : MonoBehaviour
{
    #region Inspector

    [Header("渲染目标")]
    [Tooltip("是否自动收集当前物体及子物体上的 Renderer。")]
    [SerializeField] private bool includeChildRenderers = true;

    [Tooltip("是否包含暂时未激活的子物体 Renderer。")]
    [SerializeField] private bool includeInactiveRenderers = true;

    [Tooltip("填写后只使用这里指定的 Renderer；留空则按上面的规则自动收集。")]
    [SerializeField] private Renderer[] explicitRenderers = new Renderer[0];

    #endregion

    #region Runtime

    private static readonly int BodyMinVId = Shader.PropertyToID("_BodyMinV");
    private static readonly int BodyMaxVId = Shader.PropertyToID("_BodyMaxV");

    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<ActorRenderEffectModule> modules = new List<ActorRenderEffectModule>();
    private MaterialPropertyBlock propertyBlock;
    private bool bindingsDirty = true;

    public IReadOnlyList<Renderer> Renderers => renderers;

    #endregion

    #region Lifecycle

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        RefreshBindings();
    }

    private void OnEnable()
    {
        bindingsDirty = true;
    }

    private void LateUpdate()
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        if (bindingsDirty)
            RefreshBindings();

        float deltaTime = Application.isPlaying ? Time.deltaTime : 0f;
        for (int i = 0; i < modules.Count; i++)
        {
            if (modules[i] != null)
                modules[i].UpdateFrame(deltaTime);
        }

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock);
            ApplySpriteBodyRange(renderer, propertyBlock);

            for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
            {
                ActorRenderEffectModule module = modules[moduleIndex];
                if (module == null)
                    continue;

                module.ApplyFrame(renderer, propertyBlock, deltaTime);
            }

            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    private void OnValidate()
    {
        bindingsDirty = true;
    }

    private void OnTransformChildrenChanged()
    {
        bindingsDirty = true;
    }

    #endregion

    #region Binding

    /// <summary>重新收集 Renderer 和效果模块，适用于运行时挂载装备或特效后的刷新。</summary>
    public void RefreshBindings()
    {
        renderers.Clear();
        modules.Clear();

        if (explicitRenderers != null && explicitRenderers.Length > 0)
        {
            for (int i = 0; i < explicitRenderers.Length; i++)
                AddRenderer(explicitRenderers[i]);
        }
        else if (includeChildRenderers)
        {
            Renderer[] foundRenderers = GetComponentsInChildren<Renderer>(includeInactiveRenderers);
            for (int i = 0; i < foundRenderers.Length; i++)
                AddRenderer(foundRenderers[i]);
        }
        else
        {
            Renderer[] foundRenderers = GetComponents<Renderer>();
            for (int i = 0; i < foundRenderers.Length; i++)
                AddRenderer(foundRenderers[i]);
        }

        ActorRenderEffectModule[] foundModules = includeChildRenderers
            ? GetComponentsInChildren<ActorRenderEffectModule>(includeInactiveRenderers)
            : GetComponents<ActorRenderEffectModule>();

        for (int i = 0; i < foundModules.Length; i++)
        {
            ActorRenderEffectModule module = foundModules[i];
            if (module != null && !modules.Contains(module))
                modules.Add(module);
        }

        modules.Sort(CompareModules);
        bindingsDirty = false;
    }

    private void AddRenderer(Renderer renderer)
    {
        if (renderer == null ||
            renderer.GetComponent<ActorRenderEffectExclude>() != null ||
            renderers.Contains(renderer))
        {
            return;
        }

        renderers.Add(renderer);
    }

    private static int CompareModules(ActorRenderEffectModule left, ActorRenderEffectModule right)
    {
        if (left == null)
            return 1;
        if (right == null)
            return -1;
        return left.Priority.CompareTo(right.Priority);
    }

    #endregion

    #region Sprite Metadata

    /// <summary>把当前 Sprite 的本地可见高度同步给 Shader，动画换帧时仍能正确计算水面位置。</summary>
    private static void ApplySpriteBodyRange(Renderer renderer, MaterialPropertyBlock block)
    {
        if (!(renderer is SpriteRenderer spriteRenderer) || spriteRenderer.sprite == null)
            return;

        Bounds bounds = spriteRenderer.sprite.bounds;
        float minY = bounds.min.y;
        float maxY = Mathf.Max(bounds.max.y, minY + 0.0001f);
        block.SetFloat(BodyMinVId, minY);
        block.SetFloat(BodyMaxVId, maxY);
    }

    #endregion
}

/// <summary>
/// 角色渲染效果模块基类。
/// 子类只负责自己的状态和 Shader 参数，控制器负责 Renderer 遍历、模块排序与最终 MPB 提交。
/// </summary>
public abstract class ActorRenderEffectModule : MonoBehaviour
{
    #region Inspector

    [Tooltip("数值越小越先应用；用于约定多个模块写入同一属性时的覆盖顺序。")]
    [SerializeField] private int priority;

    public int Priority => priority;

    #endregion

    #region Module Lifecycle

    /// <summary>更新模块的平滑状态，不直接操作 Renderer。</summary>
    internal void UpdateFrame(float deltaTime)
    {
        if (isActiveAndEnabled)
            PrepareFrame(deltaTime);
    }

    /// <summary>向指定 Renderer 合并本模块的材质参数。</summary>
    internal void ApplyFrame(Renderer renderer, MaterialPropertyBlock block, float deltaTime)
    {
        if (isActiveAndEnabled && AppliesTo(renderer))
            ApplyEffect(renderer, block, deltaTime);
    }

    /// <summary>判断本模块是否作用于指定 Renderer。</summary>
    protected virtual bool AppliesTo(Renderer renderer)
    {
        return renderer != null;
    }

    /// <summary>更新运行时状态，供子类实现平滑过渡。</summary>
    protected virtual void PrepareFrame(float deltaTime)
    {
    }

    /// <summary>把本模块的效果参数写入控制器提供的 MaterialPropertyBlock。</summary>
    protected abstract void ApplyEffect(Renderer renderer, MaterialPropertyBlock block, float deltaTime);

    #endregion
}

/// <summary>
/// 标记不参与角色主体 MPB 合成的附属 Renderer。
/// 状态火焰、头顶图标等独立表现可使用此标记，避免受到角色受击闪红、水下染色或透明度控制。
/// </summary>
[DisallowMultipleComponent]
public sealed class ActorRenderEffectExclude : MonoBehaviour
{
}
