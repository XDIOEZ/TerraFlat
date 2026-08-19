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
    private readonly List<Renderer> externalRenderers = new List<Renderer>();
    private readonly List<ActorRenderEffectModule> modules = new List<ActorRenderEffectModule>();
    private readonly Dictionary<Renderer, Material[]> externalOriginalMaterials =
        new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Renderer, MaterialPropertyBlock> externalOriginalPropertyBlocks =
        new Dictionary<Renderer, MaterialPropertyBlock>();
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

                module.ApplyFrame(
                    renderer,
                    propertyBlock,
                    deltaTime,
                    externalRenderers.Contains(renderer));
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

    private void OnDestroy()
    {
        RestoreAllExternalMaterials();
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

        for (int i = externalRenderers.Count - 1; i >= 0; i--)
        {
            Renderer externalRenderer = externalRenderers[i];
            if (externalRenderer == null)
            {
                externalRenderers.RemoveAt(i);
                continue;
            }

            AddRenderer(externalRenderer);
        }

        modules.Sort(CompareModules);
        bindingsDirty = false;
    }

    /// <summary>注册手持物等不在角色表现节点下的附属 Renderer，并临时继承角色效果材质。</summary>
    public void RegisterExternalRenderers(Transform root)
    {
        if (root == null)
            return;

        if (bindingsDirty)
            RefreshBindings();

        Material effectMaterial = FindEffectSpriteMaterial();
        Renderer[] foundRenderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < foundRenderers.Length; i++)
        {
            Renderer renderer = foundRenderers[i];
            if (renderer == null || renderer.GetComponent<ActorRenderEffectExclude>() != null)
                continue;

            if (!externalRenderers.Contains(renderer))
                externalRenderers.Add(renderer);

            CaptureExternalPropertyBlock(renderer);
            AddRenderer(renderer);
            ApplyEffectMaterial(renderer, effectMaterial);
        }
    }

    /// <summary>移除附属 Renderer，并恢复其进入手持状态前使用的材质。</summary>
    public void UnregisterExternalRenderers(Transform root)
    {
        if (root == null)
            return;

        Renderer[] foundRenderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < foundRenderers.Length; i++)
        {
            Renderer renderer = foundRenderers[i];
            if (renderer == null)
                continue;

            externalRenderers.Remove(renderer);
            renderers.Remove(renderer);
            RestoreExternalRenderer(renderer);
        }
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

    /// <summary>查找支持角色效果参数的主体 Sprite 材质。</summary>
    private Material FindEffectSpriteMaterial()
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (!(renderer is SpriteRenderer) || externalRenderers.Contains(renderer))
                continue;

            Material material = renderer.sharedMaterial;
            if (material != null && material.HasProperty(BodyMinVId))
                return material;
        }

        return null;
    }

    /// <summary>仅替换 SpriteRenderer 材质；粒子和独立特效保持原表现。</summary>
    private void ApplyEffectMaterial(Renderer renderer, Material effectMaterial)
    {
        if (!(renderer is SpriteRenderer) || effectMaterial == null ||
            renderer.sharedMaterial == effectMaterial)
        {
            return;
        }

        if (!externalOriginalMaterials.ContainsKey(renderer))
            externalOriginalMaterials.Add(renderer, renderer.sharedMaterials);

        renderer.sharedMaterial = effectMaterial;
    }

    /// <summary>保存附属 Renderer 原有属性，避免退出手持或返回对象池后残留水下参数。</summary>
    private void CaptureExternalPropertyBlock(Renderer renderer)
    {
        if (externalOriginalPropertyBlocks.ContainsKey(renderer))
            return;

        MaterialPropertyBlock originalBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(originalBlock);
        externalOriginalPropertyBlocks.Add(renderer, originalBlock);
    }

    private void RestoreExternalMaterial(Renderer renderer)
    {
        if (renderer == null || !externalOriginalMaterials.TryGetValue(renderer, out Material[] materials))
            return;

        renderer.sharedMaterials = materials;
        externalOriginalMaterials.Remove(renderer);
    }

    private void RestoreExternalRenderer(Renderer renderer)
    {
        if (renderer == null)
            return;

        RestoreExternalMaterial(renderer);
        if (externalOriginalPropertyBlocks.TryGetValue(renderer, out MaterialPropertyBlock block))
        {
            renderer.SetPropertyBlock(block);
            externalOriginalPropertyBlocks.Remove(renderer);
        }
    }

    private void RestoreAllExternalMaterials()
    {
        Renderer[] registeredRenderers = externalRenderers.ToArray();
        for (int i = 0; i < registeredRenderers.Length; i++)
            RestoreExternalRenderer(registeredRenderers[i]);

        externalRenderers.Clear();
        externalOriginalMaterials.Clear();
        externalOriginalPropertyBlocks.Clear();
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
    internal void ApplyFrame(
        Renderer renderer,
        MaterialPropertyBlock block,
        float deltaTime,
        bool isExternalRenderer)
    {
        if (isActiveAndEnabled &&
            (!isExternalRenderer || AppliesToExternalRenderer()) &&
            AppliesTo(renderer))
            ApplyEffect(renderer, block, deltaTime);
    }

    /// <summary>声明本模块是否应把效果扩展到角色外部注册的手持物 Renderer。</summary>
    protected virtual bool AppliesToExternalRenderer()
    {
        return true;
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
