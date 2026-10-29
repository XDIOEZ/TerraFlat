using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>只读取本地玩家体温，在最终世界画面绘制渐进方块冰霜或边缘热浪。</summary>
public sealed class TemperatureScreenRendererFeature : ScriptableRendererFeature
{
    #region 本地玩家表现状态

    private const float MinimumVisibleSeverity = 0.0005f;
    private static int runtimeSessionVersion;

    [SerializeField] private Shader temperatureShader;

    private Material material;
    private TemperaturePass pass;
    private Player playerOwner;
    private Mod_Temperature temperatureModule;
    private Mod_DamageReceiver healthModule;
    private Mod_Cam cameraModule;
    private Camera localCamera;
    private uint temperatureGeneration;
    private uint healthGeneration;
    private uint cameraGeneration;
    private GameRes lifecycleResourceOwner;
    private GameManager worldLifecycle;
    private GameRes resourceOwner;
    private ResourceAssetScope resourceSession;
    private int observedRuntimeSession;
    private int sampledFrame = -1;
    private float signedSeverity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void BeginRuntimeSession()
    {
        // 关闭域重载时仍让上一轮游戏的表现和临时纹理失效。
        unchecked { runtimeSessionVersion++; }
    }

    private void ClearPresentation()
    {
        playerOwner = null;
        temperatureModule = null;
        healthModule = null;
        cameraModule = null;
        localCamera = null;
        temperatureGeneration = 0;
        healthGeneration = 0;
        cameraGeneration = 0;
        resourceOwner = null;
        resourceSession = null;
        signedSeverity = 0f;
        pass?.ReleaseScreenCopy();
    }

    #endregion

    #region 渲染器生命周期

    public override void Create()
    {
        ClearPresentation();
        CoreUtils.Destroy(material);
        material = null;
        pass = null;
        lifecycleResourceOwner = null;
        worldLifecycle = null;
        sampledFrame = -1;
        observedRuntimeSession = runtimeSessionVersion;
        if (temperatureShader == null)
        {
            Debug.LogError("[TemperatureScreen] 渲染器资源缺少体温屏幕效果 Shader。", this);
            return;
        }

        material = CoreUtils.CreateEngineMaterial(temperatureShader);
        pass = new TemperaturePass(material);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!Application.isPlaying || material == null || pass == null)
            return;

        ref CameraData cameraData = ref renderingData.cameraData;
        if (cameraData.cameraType != CameraType.Game)
            return;
        if (observedRuntimeSession != runtimeSessionVersion)
        {
            ClearPresentation();
            sampledFrame = -1;
            observedRuntimeSession = runtimeSessionVersion;
        }

        // 环绕补绘与其它游戏相机共享一帧采样，不能重复推进过渡。
        if (sampledFrame != Time.frameCount)
        {
            sampledFrame = Time.frameCount;
            SampleLocalPlayer();
        }

        if (Mathf.Abs(signedSeverity) <= MinimumVisibleSeverity ||
            localCamera == null || !localCamera.isActiveAndEnabled ||
            !cameraData.resolveFinalTarget || !IsLocalStackCamera(localCamera, cameraData.camera))
            return;

        pass.Setup(signedSeverity, Time.unscaledTime);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        ClearPresentation();
        lifecycleResourceOwner = null;
        worldLifecycle = null;
        pass = null;
        CoreUtils.Destroy(material);
        material = null;
    }

    #endregion

    #region 真实体温与生命周期采样

    private void SampleLocalPlayer()
    {
        GameRes resources = GameRes.ExistingInstance;
        if (lifecycleResourceOwner != resources)
        {
            lifecycleResourceOwner = resources;
            // 只在资源管理器换代时查询所属WorldManager子树，不创建单例或逐帧扫描场景。
            worldLifecycle = resources != null
                ? PersistentRuntimeObjectHierarchy.GetBusinessOwnerRoot(resources.gameObject).GetComponentInChildren<GameManager>(true) : null;
        }
        if (resources == null || !resources.isLoadFinish ||
            worldLifecycle == null || !worldLifecycle.IsInGameWorld)
        {
            ClearPresentation();
            return;
        }

        Player player = ItemMgr.GetInstance()?.User_Player;
        if (player == null || !player.IsLocalProfile || !player.IsInitialized ||
            !player.gameObject.activeInHierarchy)
        {
            ClearPresentation();
            return;
        }

        Mod_Temperature temperature = player.itemMods.GetMod_ByID<Mod_Temperature>(ModText.Temperature);
        Mod_DamageReceiver health = player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        Mod_Cam camera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
        if (temperature == null || !temperature.IsRuntimeLoaded || !temperature.Enabled ||
            health == null || !health.IsRuntimeLoaded || !health.Enabled ||
            !float.IsFinite(health.Hp) || health.Hp <= 0f ||
            camera == null || !camera.IsRuntimeLoaded || !camera.Enabled ||
            camera.ControllerCamera == null || !camera.ControllerCamera.isActiveAndEnabled)
        {
            ClearPresentation();
            return;
        }

        if (playerOwner != player || temperatureModule != temperature || healthModule != health ||
            cameraModule != camera || localCamera != camera.ControllerCamera ||
            temperatureGeneration != temperature.RuntimeGeneration ||
            healthGeneration != health.RuntimeGeneration || cameraGeneration != camera.RuntimeGeneration ||
            resourceOwner != resources || !ReferenceEquals(resourceSession, resources.ResourceAssets))
        {
            ClearPresentation();
            playerOwner = player;
            temperatureModule = temperature;
            healthModule = health;
            cameraModule = camera;
            localCamera = camera.ControllerCamera;
            temperatureGeneration = temperature.RuntimeGeneration;
            healthGeneration = health.RuntimeGeneration;
            cameraGeneration = camera.RuntimeGeneration;
            resourceOwner = resources;
            resourceSession = resources.ResourceAssets;
        }

        temperature.GetSafeTemperatureRange(out float minimum, out float maximum);
        TemperatureWarningPresentationSample sample = TemperatureWarningPresentationRules.Evaluate(
            temperature.Data.CurrentTemperature, minimum, maximum);
        signedSeverity = TemperatureWarningPresentationRules.AdvanceScreenSeverity(
            signedSeverity, sample.SignedDanger, Time.unscaledDeltaTime);
        if (signedSeverity <= MinimumVisibleSeverity)
            pass.ReleaseScreenCopy();
    }

    private static bool IsLocalStackCamera(Camera source, Camera candidate)
        => source == candidate || source.TryGetComponent(out UniversalAdditionalCameraData data) &&
           data.renderType == CameraRenderType.Base && data.cameraStack != null &&
           data.cameraStack.Contains(candidate);

    #endregion

    #region 边缘 GPU 合成

    private sealed class TemperaturePass : ScriptableRenderPass
    {
        private static readonly int SeverityId = Shader.PropertyToID("_TemperatureSeverity");
        private static readonly int TimeId = Shader.PropertyToID("_TemperatureTime");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_TemperatureScreenSize");
        private static readonly int SourceTexelSizeId = Shader.PropertyToID("_TemperatureSourceTexelSize");
        private readonly ProfilingSampler frostSampler = new("FlatWorld Temperature Frost Edge");
        private readonly ProfilingSampler heatSampler = new("FlatWorld Temperature Heat Edge");
        private readonly Material passMaterial;
        private RTHandle screenCopy;
        private float severity;
        private float animationTime;
        private bool isHeat;

        public TemperaturePass(Material targetMaterial)
        {
            passMaterial = targetMaterial;
            // URP 2D 用这个精确事件保留可合成的颜色缓冲，列表顺序让低血量红边最后绘制。
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public void Setup(float signedValue, float unscaledTime)
        {
            severity = Mathf.Clamp01(Mathf.Abs(signedValue));
            animationTime = unscaledTime;
            isHeat = signedValue > 0f;
            ConfigureInput(isHeat ? ScriptableRenderPassInput.Color : ScriptableRenderPassInput.None);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!isHeat)
                return;

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            RenderingUtils.ReAllocateIfNeeded(ref screenCopy, descriptor, FilterMode.Bilinear,
                TextureWrapMode.Clamp, name: "_FlatWorldTemperatureScreenCopy");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (passMaterial == null || severity <= MinimumVisibleSeverity || (isHeat && screenCopy == null))
                return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, isHeat ? heatSampler : frostSampler))
            {
                RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                int width = Mathf.Max(1, descriptor.width);
                int height = Mathf.Max(1, descriptor.height);
                passMaterial.SetFloat(SeverityId, severity);
                passMaterial.SetFloat(TimeId, animationTime);
                passMaterial.SetVector(ScreenSizeId, new Vector4(width, height, 1f / width, 1f / height));
                RTHandle colorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
                if (isHeat)
                {
                    int sourceWidth = Mathf.Max(1, screenCopy.rt.width);
                    int sourceHeight = Mathf.Max(1, screenCopy.rt.height);
                    passMaterial.SetVector(SourceTexelSizeId,
                        new Vector4(1f / sourceWidth, 1f / sourceHeight, sourceWidth, sourceHeight));
                    // 真热浪先复制再扭曲写回，避免同一张相机纹理同时读写。
                    Blitter.BlitCameraTexture(cmd, colorTarget, screenCopy);
                    Blitter.BlitCameraTexture(cmd, screenCopy, colorTarget, passMaterial, 1);
                }
                else
                {
                    CoreUtils.SetRenderTarget(cmd, colorTarget);
                    cmd.DrawProcedural(Matrix4x4.identity, passMaterial, 0, MeshTopology.Triangles, 3, 1);
                }
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void ReleaseScreenCopy()
        {
            screenCopy?.Release();
            screenCopy = null;
        }
    }

    #endregion
}
