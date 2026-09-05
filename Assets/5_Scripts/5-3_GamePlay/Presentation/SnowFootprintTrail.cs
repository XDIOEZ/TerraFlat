using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 雪地脚印表现组件：按步距在世界空间发射左右交替的独立矩形脚印。
/// 脚印由单个 ParticleSystem 承载，避免为长期历史轨迹持续创建大量 GameObject；
/// 离开雪地只停止新脚印生成，已有脚印继续保留并在完整寿命结束后渐隐。
/// </summary>
[DisallowMultipleComponent]
public sealed class SnowFootprintTrail : MonoBehaviour
{
    private const string DefaultShaderName = "FlatWorld/Environment/Snow Footprint";
    private const float FootprintDepthOffset = -0.02f;

    [Header("步态")]
    [SerializeField, Min(0.01f)] private float stepDistance = 0.28f;
    [SerializeField, Min(0f)] private float lateralOffset = 0.075f;
    [SerializeField] private Vector2 footprintSize = new(0.12f, 0.22f);

    [Header("生命周期")]
    [SerializeField, Min(0.1f)] private float lifetime = 60f;
    [SerializeField, Min(0.05f)] private float fadeDuration = 3f;
    [SerializeField, Min(64)] private int maxFootprints = 2048;

    [Header("渲染")]
    [SerializeField] private Material footprintMaterial;
    [SerializeField] private int sortingOrder = -1;

    private bool surfaceActive;
    private bool hasLastStepPosition;
    private bool nextFootIsLeft = true;
    private Vector2 lastStepPosition;

    private GameObject footprintObject;
    private ParticleSystem footprintParticles;
    private ParticleSystemRenderer footprintRenderer;
    private Material runtimeMaterial;
    private bool missingShaderLogged;

    #region 对外接口

    /// <summary>设置脚印完整保留时间，淡出时间独立由 fadeDuration 控制。</summary>
    public void ConfigureLifetime(float seconds)
    {
        lifetime = Mathf.Max(0.1f, seconds);
        ApplyParticleLifetimeSettings();
    }

    /// <summary>启用或停用脚印采样；停用时保留已有脚印并继续自然衰减。</summary>
    public void SetSurfaceActive(bool active)
    {
        if (surfaceActive == active)
            return;

        surfaceActive = active;
        if (!active)
        {
            hasLastStepPosition = false;
            return;
        }

        EnsureFootprintParticleSystem();
        lastStepPosition = transform.position;
        hasLastStepPosition = true;
    }

    #endregion

    #region 运行时采样

    /// <summary>位移达到一步距离后，仅生成当前应落下的左脚或右脚脚印。</summary>
    private void LateUpdate()
    {
        if (!surfaceActive)
            return;

        Vector2 currentPosition = transform.position;
        if (!hasLastStepPosition)
        {
            lastStepPosition = currentPosition;
            hasLastStepPosition = true;
            return;
        }

        Vector2 movement = currentPosition - lastStepPosition;
        if (movement.sqrMagnitude < stepDistance * stepDistance)
            return;

        Vector2 moveDirection = movement.normalized;
        if (EmitFootprint(currentPosition, moveDirection))
            nextFootIsLeft = !nextFootIsLeft;

        lastStepPosition = currentPosition;
    }

    /// <summary>根据移动方向求左右法线偏移，并发射一枚世界空间矩形脚印。</summary>
    private bool EmitFootprint(Vector2 centerPosition, Vector2 moveDirection)
    {
        EnsureFootprintParticleSystem();
        if (footprintParticles == null || ResolveFootprintMaterial() == null)
            return false;

        Vector2 perpendicular = new(-moveDirection.y, moveDirection.x);
        float side = nextFootIsLeft ? 1f : -1f;
        Vector2 footprintPosition = centerPosition + perpendicular * (side * lateralOffset);
        // Billboard 粒子的屏幕旋转方向与世界空间 SignedAngle 相反；反号后长边才会沿实际移动方向。
        float rotation = -Vector2.SignedAngle(Vector2.up, moveDirection);
        float totalLifetime = lifetime + fadeDuration;

        ParticleSystem.EmitParams emitParams = new()
        {
            position = new Vector3(
                footprintPosition.x,
                footprintPosition.y,
                transform.position.z + FootprintDepthOffset),
            startColor = Color.white,
            startLifetime = totalLifetime,
            startSize3D = new Vector3(footprintSize.x, footprintSize.y, 1f),
            rotation = rotation
        };

        footprintParticles.Emit(emitParams, 1);
        return true;
    }

    #endregion

    #region 粒子系统

    /// <summary>创建一次独立于角色层级的世界空间粒子系统，历史脚印不会跟着角色移动。</summary>
    private void EnsureFootprintParticleSystem()
    {
        if (footprintParticles != null)
        {
            Material material = ResolveFootprintMaterial();
            if (footprintRenderer != null && material != null && footprintRenderer.sharedMaterial != material)
                footprintRenderer.sharedMaterial = material;
            return;
        }

        footprintObject = new GameObject($"SnowFootprints_{GetInstanceID()}")
        {
            layer = gameObject.layer,
            hideFlags = HideFlags.DontSave
        };

        Scene ownerScene = gameObject.scene;
        if (ownerScene.IsValid() && ownerScene.isLoaded && footprintObject.scene != ownerScene)
            SceneManager.MoveGameObjectToScene(footprintObject, ownerScene);

        footprintObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        footprintObject.transform.localScale = Vector3.one;

        footprintParticles = footprintObject.AddComponent<ParticleSystem>();
        footprintRenderer = footprintParticles.GetComponent<ParticleSystemRenderer>();
        ConfigureFootprintParticleSystem();
        footprintParticles.Play(false);
    }

    /// <summary>配置无自动发射、世界空间模拟的长寿命矩形脚印粒子。</summary>
    private void ConfigureFootprintParticleSystem()
    {
        ParticleSystem.MainModule main = footprintParticles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(footprintSize.x);
        main.startSizeY = new ParticleSystem.MinMaxCurve(footprintSize.y);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(1f);
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.maxParticles = maxFootprints;

        ParticleSystem.EmissionModule emission = footprintParticles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = footprintParticles.shape;
        shape.enabled = false;

        ParticleSystem.VelocityOverLifetimeModule velocity = footprintParticles.velocityOverLifetime;
        velocity.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = footprintParticles.sizeOverLifetime;
        size.enabled = false;

        ApplyParticleLifetimeSettings();

        if (footprintRenderer == null)
            return;

        footprintRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        footprintRenderer.alignment = ParticleSystemRenderSpace.View;
        footprintRenderer.sortingOrder = sortingOrder;
        footprintRenderer.enableGPUInstancing = true;
        footprintRenderer.sharedMaterial = ResolveFootprintMaterial();
    }

    /// <summary>保持约 lifetime 秒完全可见，并仅在最后 fadeDuration 秒渐隐。</summary>
    private void ApplyParticleLifetimeSettings()
    {
        if (footprintParticles == null)
            return;

        float totalLifetime = lifetime + fadeDuration;
        ParticleSystem.MainModule main = footprintParticles.main;
        main.startLifetime = totalLifetime;
        main.maxParticles = maxFootprints;

        float fadeStart = Mathf.Clamp01(lifetime / totalLifetime);
        Gradient fadeGradient = new();
        fadeGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, fadeStart),
                new GradientAlphaKey(0f, 1f)
            });

        ParticleSystem.ColorOverLifetimeModule color = footprintParticles.colorOverLifetime;
        color.enabled = true;
        color.color = fadeGradient;
    }

    /// <summary>优先使用 Inspector 材质；动态组件未配置材质时使用专用 Shader 创建共享运行时材质。</summary>
    private Material ResolveFootprintMaterial()
    {
        if (footprintMaterial != null)
            return footprintMaterial;

        if (runtimeMaterial != null)
            return runtimeMaterial;

        Shader shader = Shader.Find(DefaultShaderName);
        if (shader == null)
        {
            if (!missingShaderLogged)
            {
                Debug.LogError($"SnowFootprintTrail 找不到 Shader：{DefaultShaderName}", this);
                missingShaderLogged = true;
            }
            return null;
        }

        runtimeMaterial = new Material(shader)
        {
            name = "Snow Footprint (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        return runtimeMaterial;
    }

    #endregion

    #region 生命周期

    /// <summary>实体真正禁用时清掉自己的全部运行时脚印状态。</summary>
    private void OnDisable()
    {
        surfaceActive = false;
        hasLastStepPosition = false;
        nextFootIsLeft = true;
        missingShaderLogged = false;

        if (footprintParticles != null)
            footprintParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        footprintParticles = null;
        footprintRenderer = null;

        if (footprintObject != null)
        {
            footprintObject.SetActive(false);
            Destroy(footprintObject);
            footprintObject = null;
        }

        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

#if UNITY_EDITOR
    /// <summary>编辑器中保持可调参数为有效范围。</summary>
    private void OnValidate()
    {
        stepDistance = Mathf.Max(0.01f, stepDistance);
        lateralOffset = Mathf.Max(0f, lateralOffset);
        footprintSize.x = Mathf.Max(0.01f, footprintSize.x);
        footprintSize.y = Mathf.Max(0.01f, footprintSize.y);
        lifetime = Mathf.Max(0.1f, lifetime);
        fadeDuration = Mathf.Max(0.05f, fadeDuration);
        maxFootprints = Mathf.Max(64, maxFootprints);
        ApplyParticleLifetimeSettings();
    }
#endif

    #endregion
}
