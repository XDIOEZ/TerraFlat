using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 角色 Buff 光照控制单元：观察 BuffManager 的生命周期事件，并把指定 Buff 映射为独立的 Unity 2D 点光源。
/// 光源创建在角色子层级中，因此会自然跟随角色；本类只负责渲染表现，不参与 Buff 数值、伤害、Tick 或存档。
/// 当前“光耀”使用暖黄色点光；“感染”通过角色统一渲染模块叠加低强度绿色染色，Buff 移除或到期时自动恢复。
/// </summary>
[DisallowMultipleComponent]
public sealed class ActorBuffLightController : MonoBehaviour
{
    #region 配置

    public const string RadianceBuffId = "光耀";
    private const string RuntimeLightName = "BuffLight_Radiance";

    [Header("Buff 光照")]
    [Tooltip("启用真实 2D 点光源的 Buff ID。")]
    [SerializeField] private string buffId = RadianceBuffId;

    [Tooltip("光耀的暖色光颜色。")]
    [SerializeField] private Color lightColor = new Color(1f, 0.82f, 0.32f, 1f);

    [Tooltip("点光源强度。")]
    [SerializeField, Min(0f)] private float intensity = 0.85f;

    [Tooltip("点光源完整亮度半径。")]
    [SerializeField, Min(0f)] private float innerRadius = 0.8f;

    [Tooltip("点光源衰减结束半径。")]
    [SerializeField, Min(0.01f)] private float outerRadius = 5f;

    [Header("感染染色")]
    [Tooltip("感染时叠加到角色主体的轻微绿色。")]
    [SerializeField] private Color infectionTint = new Color(0.3f, 0.78f, 0.28f, 1f);

    [Tooltip("感染绿色的混合强度，保持轻微可见。")]
    [SerializeField, Range(0f, 1f)] private float infectionTintStrength = 0.18f;

    #endregion

    #region 运行时状态

    private BuffManager buffManager;
    private Light2D runtimeLight;
    private ActorRenderColorEffect renderColorEffect;
    private bool lightActive;

    public Light2D RuntimeLight => runtimeLight;
    public bool IsLightActive => runtimeLight != null && runtimeLight.enabled && lightActive;
    public bool IsInfectionTintActive { get; private set; }

    #endregion

    #region 生命周期

    private void Awake()
    {
        EnsureRuntimeLight();
    }

    private void OnEnable()
    {
        BindBuffManager();
        RefreshBuffVisualState();
    }

    private void Start()
    {
        RefreshBuffVisualState();
    }

    private void OnDisable()
    {
        UnbindBuffManager();
        SetLightActive(false);
        SetInfectionTintActive(false);
    }

    private void OnDestroy()
    {
        UnbindBuffManager();
    }

    /// <summary>模块运行时补装 BuffManager 后重新绑定观察者。</summary>
    private void OnTransformParentChanged()
    {
        if (!isActiveAndEnabled)
            return;

        BindBuffManager();
        RefreshBuffVisualState();
    }

    private void OnValidate()
    {
        intensity = Mathf.Max(0f, intensity);
        innerRadius = Mathf.Max(0f, innerRadius);
        outerRadius = Mathf.Max(0.01f, outerRadius);
        innerRadius = Mathf.Min(innerRadius, outerRadius);
        infectionTintStrength = Mathf.Clamp01(infectionTintStrength);
    }

    #endregion

    #region Buff 观察

    private void BindBuffManager()
    {
        Item owner = GetComponentInParent<Item>();
        BuffManager candidate = owner?.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
        if (candidate == null && owner != null)
            candidate = owner.GetComponentInChildren<BuffManager>(true);
        if (candidate == null)
            candidate = GetComponentInParent<BuffManager>();
        if (candidate == buffManager)
            return;

        UnbindBuffManager();
        buffManager = candidate;
        if (buffManager == null)
            return;

        buffManager.BuffAdded += HandleBuffChanged;
        buffManager.BuffRemoved += HandleBuffChanged;
    }

    private void UnbindBuffManager()
    {
        if (buffManager != null)
        {
            buffManager.BuffAdded -= HandleBuffChanged;
            buffManager.BuffRemoved -= HandleBuffChanged;
        }

        buffManager = null;
    }

    private void HandleBuffChanged(BuffInstance runtime)
    {
        if (runtime == null ||
            (!string.Equals(runtime.DefinitionId, buffId, System.StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(runtime.DefinitionId, InfectionBuffIds.Infection, System.StringComparison.OrdinalIgnoreCase)))
            return;

        RefreshBuffVisualState();
    }

    /// <summary>根据权威 Buff 状态同步光源，兼容存档恢复后首次启用。</summary>
    public void RefreshLightState()
    {
        RefreshBuffVisualState();
    }

    /// <summary>根据权威 Buff 状态同时刷新光照与感染染色。</summary>
    public void RefreshBuffVisualState()
    {
        EnsureRuntimeLight();
        SetLightActive(buffManager != null && buffManager.HasBuff(buffId));
        SetInfectionTintActive(buffManager != null && buffManager.HasBuff(InfectionBuffIds.Infection));
    }

    #endregion

    #region 光源管理

    private void EnsureRuntimeLight()
    {
        if (runtimeLight == null)
        {
            Transform existing = transform.Find(RuntimeLightName);
            if (existing != null)
                runtimeLight = existing.GetComponent<Light2D>();
        }

        if (runtimeLight == null)
        {
            GameObject lightObject = new GameObject(RuntimeLightName, typeof(Light2D));
            lightObject.transform.SetParent(transform, false);
            runtimeLight = lightObject.GetComponent<Light2D>();
        }

        runtimeLight.lightType = Light2D.LightType.Point;
        runtimeLight.color = lightColor;
        runtimeLight.intensity = intensity;
        runtimeLight.pointLightInnerRadius = Mathf.Min(innerRadius, outerRadius);
        runtimeLight.pointLightOuterRadius = outerRadius;
        runtimeLight.falloffIntensity = 0.65f;
        runtimeLight.transform.localPosition = Vector3.zero;
        runtimeLight.transform.localRotation = Quaternion.identity;
    }

    private void SetLightActive(bool active)
    {
        EnsureRuntimeLight();
        lightActive = active;
        if (runtimeLight.enabled != active)
            runtimeLight.enabled = active;

        LightLayerMgr.Instance?.RefreshAllActiveChunks();
    }

    /// <summary>感染存在时设置轻微绿色，结束后恢复角色原色。</summary>
    private void SetInfectionTintActive(bool active)
    {
        renderColorEffect ??= GetComponent<ActorRenderColorEffect>();
        if (renderColorEffect == null)
            renderColorEffect = GetComponentInChildren<ActorRenderColorEffect>(true);

        IsInfectionTintActive = active && renderColorEffect != null;
        if (renderColorEffect == null)
            return;

        if (active)
            renderColorEffect.SetStatusTint(infectionTint, infectionTintStrength);
        else
            renderColorEffect.ClearStatusTint();
    }

    #endregion
}
