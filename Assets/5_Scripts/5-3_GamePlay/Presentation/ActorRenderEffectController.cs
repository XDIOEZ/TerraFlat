using System.Collections.Generic;
using FlatWorld.Diagnostics;
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
    private static readonly int SpriteLocalBoundsId = Shader.PropertyToID("_SpriteLocalBounds");

    #region 性能诊断
    // 固定点位只记数字，区分模块更新、空闲跳过和实际 Renderer 提交。
    private static readonly PerformanceDebugPoint FrameDebug = new("FlatWorld.ActorRender.Frame");
    private static readonly PerformanceDebugPoint ModulesDebug = new("FlatWorld.ActorRender.UpdateModules");
    private static readonly PerformanceDebugPoint SubmitDebug = new("FlatWorld.ActorRender.SubmitRenderers");
    private static readonly PerformanceDebugPoint BindingsDebug = new("FlatWorld.ActorRender.RefreshBindings");
    private static readonly PerformanceDebugPoint IdleSkipDebug = new("FlatWorld.ActorRender.IdleSkippedFrames");
    private static readonly PerformanceDebugPoint SpriteSkipDebug = new("FlatWorld.ActorRender.UnchangedSpriteRenderers");
    private static readonly PerformanceDebugPoint SubmittedDebug = new("FlatWorld.ActorRender.SubmittedRenderers");
    #endregion

    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Renderer> externalRenderers = new List<Renderer>();
    private readonly List<ActorRenderEffectModule> modules = new List<ActorRenderEffectModule>();
    private readonly Dictionary<Renderer, Material[]> externalOriginalMaterials =
        new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Renderer, MaterialPropertyBlock> externalOriginalPropertyBlocks =
        new Dictionary<Renderer, MaterialPropertyBlock>();
    private MaterialPropertyBlock propertyBlock;
    private Material effectSpriteMaterialOverride;
    private bool bindingsDirty = true;
    private bool renderStateDirty = true;
    private readonly List<uint> moduleVersions = new();
    private readonly List<bool> moduleActiveStates = new();
    private readonly Dictionary<Renderer, (Sprite Sprite, bool FlipX, bool FlipY)> submittedSprites = new();

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
        renderStateDirty = true;
    }

    private void LateUpdate()
    {
        RenderFrame(Application.isPlaying ? Time.deltaTime : 0f);
    }

    // 正式更新与独立规则共用合成入口，不推进游戏或更改控制器启停状态。
    internal void RenderFrame(float deltaTime)
    {
        using var frame = FrameDebug.Measure();
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        if (bindingsDirty)
            RefreshBindings();

        bool applyEffects = renderStateDirty;
        bool trackSpriteBodyRange = false;
        using (ModulesDebug.Measure(modules.Count))
        {
            for (int i = 0; i < modules.Count; i++)
            {
                ActorRenderEffectModule module = modules[i];
                if (module == null)
                    continue;
                bool active = module.UpdateFrame(deltaTime);
                trackSpriteBodyRange |= active && module.RequiresSpriteBodyRange;
                applyEffects |= moduleVersions[i] != module.RenderStateVersion ||
                    moduleActiveStates[i] != active || active && module.RequiresContinuousRendering;
                moduleVersions[i] = module.RenderStateVersion;
                moduleActiveStates[i] = active;
            }
        }

        // 覆雪等只使用独立材质通道，状态未变时无需读取每个 Renderer 的 Sprite。
        if (!applyEffects && !trackSpriteBodyRange)
        {
            IdleSkipDebug.AddWork(1);
            return;
        }

        using var submit = SubmitDebug.Measure(renderers.Count);
        int skipped = 0, submitted = 0;
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            SpriteRenderer spriteRenderer = renderer as SpriteRenderer;
            Sprite sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
            var spriteState = (Sprite: sprite, FlipX: spriteRenderer != null && spriteRenderer.flipX,
                FlipY: spriteRenderer != null && spriteRenderer.flipY);
            // 静态参数未变化时保留已提交的 MPB，换图和动态效果仍按当前帧刷新。
            if (!applyEffects && submittedSprites.TryGetValue(renderer, out var previousSprite) &&
                previousSprite == spriteState)
            {
                skipped++;
                continue;
            }

            renderer.GetPropertyBlock(propertyBlock);
            ApplySpriteBodyRange(sprite, spriteState.FlipX, spriteState.FlipY, propertyBlock);
            bool isExternalRenderer = externalRenderers.Contains(renderer);

            for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
            {
                ActorRenderEffectModule module = modules[moduleIndex];
                if (module == null)
                    continue;

                module.ApplyFrame(
                    renderer,
                    propertyBlock,
                    deltaTime,
                    isExternalRenderer);
            }

            renderer.SetPropertyBlock(propertyBlock);
            submittedSprites[renderer] = spriteState;
            submitted++;
        }
        SpriteSkipDebug.AddWork(skipped);
        SubmittedDebug.AddWork(submitted);
        renderStateDirty = false;
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
        using var sample = BindingsDebug.Measure();
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
        moduleVersions.Clear();
        moduleActiveStates.Clear();
        for (int i = 0; i < modules.Count; i++)
        {
            moduleVersions.Add(modules[i].RenderStateVersion);
            moduleActiveStates.Add(modules[i].isActiveAndEnabled);
        }
        submittedSprites.Clear();
        renderStateDirty = true;
        bindingsDirty = false;
        sample.AddWork(renderers.Count);
    }

    /// <summary>为仅驱动外部 Renderer 的临时效果控制器指定共享 Sprite 效果材质。</summary>
    public void SetEffectSpriteMaterial(Material material)
    {
        effectSpriteMaterialOverride = material;
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
            renderStateDirty = true;
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
            submittedSprites.Remove(renderer);
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
        if (effectSpriteMaterialOverride != null && effectSpriteMaterialOverride.HasProperty(BodyMinVId))
            return effectSpriteMaterialOverride;

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
    private static void ApplySpriteBodyRange(Sprite sprite, bool flipX, bool flipY, MaterialPropertyBlock block)
    {
        if (sprite == null)
            return;

        // 当前 Renderer 的 Sprite 只读一次，避免每个模块重复跨原生边界。
        Bounds bounds = sprite.bounds;
        float minY = bounds.min.y;
        float maxY = Mathf.Max(bounds.max.y, minY + 0.0001f);
        block.SetFloat(BodyMinVId, minY);
        block.SetFloat(BodyMaxVId, maxY);
        // 原图中心和半尺寸只在提交时读取，世界变换留给 Shader 处理。
        block.SetVector(SpriteLocalBoundsId, new Vector4(
            flipX ? -bounds.center.x : bounds.center.x,
            flipY ? -bounds.center.y : bounds.center.y, bounds.extents.x, bounds.extents.y));
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

    /// <summary>连续效果保留逐帧提交，静态效果通过版本通知参数变化。</summary>
    public virtual bool RequiresContinuousRendering => true;
    /// <summary>水线等依赖身体高度的效果保持换图跟踪，独立材质通道可显式关闭。</summary>
    public virtual bool RequiresSpriteBodyRange => true;
    internal uint RenderStateVersion { get; private set; }

    protected void MarkRenderStateDirty() => RenderStateVersion++;

    /// <summary>更新模块的平滑状态，不直接操作 Renderer。</summary>
    internal bool UpdateFrame(float deltaTime)
    {
        bool active = isActiveAndEnabled;
        if (active)
            PrepareFrame(deltaTime);
        return active;
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
