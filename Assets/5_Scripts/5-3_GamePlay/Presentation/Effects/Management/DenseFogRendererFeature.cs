using UnityEngine;
using FlatWorld.WorldModel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>只在本地玩家的最终世界画面覆盖白雾，不修改实体、同步状态或区块加载距离。</summary>
public sealed class DenseFogRendererFeature : ScriptableRendererFeature
{
    #region 本地表现状态

    [SerializeField] private Shader fogShader;

    private Material material;
    private FogPass pass;
    private Player localPlayer;
    private Mod_Cam cameraModule;
    private WorldRuntime presentationWorld;
    private long presentationEpoch;
    private int worldSceneHandle;

    private void ResetPresentation()
    {
        localPlayer = null;
        cameraModule = null;
        pass?.ResetPresentation();
        presentationWorld = null;
        presentationEpoch = 0;
        worldSceneHandle = 0;
    }

    #endregion

    #region 渲染器生命周期

    public override void Create()
    {
        ResetPresentation();
        CoreUtils.Destroy(material);
        material = null;
        pass = null;
        if (fogShader == null)
        {
            Debug.LogError("[DenseFog] 渲染器资源缺少大雾 Shader。", this);
            return;
        }

        material = CoreUtils.CreateEngineMaterial(fogShader);
        pass = new FogPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!Application.isPlaying || material == null || pass == null)
            return;

        ref CameraData cameraData = ref renderingData.cameraData;
        if (cameraData.cameraType != CameraType.Game)
            return;

        WeatherMgr weather = WeatherMgr.ExistingInstance;
        if (weather == null || !weather.isActiveAndEnabled ||
            WeatherMgr.IsWeatherSuppressedInDimension(DimensionManager.Instance.ActiveDefinition))
        {
            ResetPresentation();
            return;
        }

        Player player = ItemMgr.GetInstance()?.User_Player;
        if (player == null || !player.IsLocalProfile || !player.gameObject.activeInHierarchy)
        {
            ResetPresentation();
            return;
        }

        WorldRuntime world = ChunkMgr.ExistingInstance?.WorldRuntime;
        if (world == null)
        {
            ResetPresentation();
            return;
        }
        if (player != localPlayer || player.gameObject.scene.handle != worldSceneHandle ||
            !ReferenceEquals(world, presentationWorld) || world.Epoch != presentationEpoch)
        {
            ResetPresentation();
            localPlayer = player;
            worldSceneHandle = player.gameObject.scene.handle;
            presentationWorld = world;
            presentationEpoch = world.Epoch;
        }
        if (cameraModule == null)
            cameraModule = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);

        Camera sourceCamera = cameraModule != null ? cameraModule.ControllerCamera : null;
        if (sourceCamera == null || !sourceCamera.isActiveAndEnabled || !sourceCamera.orthographic ||
            !cameraData.resolveFinalTarget || !IsLocalStackCamera(sourceCamera, cameraData.camera))
            return;

        // 雾独立读取玩家所在地浓度，允许与雨雪和强风同时存在。
        Vector2 logicalPosition = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
        float target = weather.TryGetWeatherAt(logicalPosition, out var localWeather)
            ? Mathf.Clamp01(localWeather.FogDensity) : 0f;
        if (target <= 0f && !pass.HasPresentation)
            return;
        WorldRenderingConfig.RenderingDenseFog config = WorldRenderingConfigCatalog.Default.denseFog;
        pass.Setup(material, sourceCamera, player.transform.position, logicalPosition, target, config);
        renderer.EnqueuePass(pass);
    }

    private static void ResolveWorldViewport(Camera source, bool projectionFlipped, Vector3 playerPosition,
        out Vector3 origin, out Vector3 right, out Vector3 up)
    {
        // 起雾端点与 Shader 共用实际 GPU 投影，兼容偏心镜头、缩放和环绕补绘。
        Matrix4x4 projection = GL.GetGPUProjectionMatrix(source.projectionMatrix, projectionFlipped);
        Matrix4x4 inverse = (projection * source.worldToCameraMatrix).inverse;
        Vector3 bottomLeft = inverse.MultiplyPoint(new Vector3(-1f, -1f, 0f));
        right = inverse.MultiplyPoint(new Vector3(1f, -1f, 0f)) - bottomLeft;
        up = inverse.MultiplyPoint(new Vector3(-1f, 1f, 0f)) - bottomLeft;
        origin = bottomLeft - playerPosition;
    }

    private static bool IsLocalStackCamera(Camera source, Camera candidate)
    {
        if (source == candidate)
            return true;

        // 环绕地图的最后一台补绘相机负责合成，但可见圆心和投影仍属于本地主相机。
        return source.TryGetComponent(out UniversalAdditionalCameraData data) &&
               data.renderType == CameraRenderType.Base && data.cameraStack != null &&
               data.cameraStack.Contains(candidate);
    }

    protected override void Dispose(bool disposing)
    {
        ResetPresentation();
        CoreUtils.Destroy(material);
        material = null;
        pass = null;
    }

    #endregion

    #region 最终画面合成

    private sealed class FogPass : ScriptableRenderPass
    {
        private static readonly int OriginId = Shader.PropertyToID("_FogViewportOrigin");
        private static readonly int RightId = Shader.PropertyToID("_FogViewportRight");
        private static readonly int UpId = Shader.PropertyToID("_FogViewportUp");
        private static readonly int CenterId = Shader.PropertyToID("_FogWorldCenter");
        private static readonly int RadiiId = Shader.PropertyToID("_FogRadii");
        private static readonly int ColorId = Shader.PropertyToID("_FogColor");
        private static readonly int NoiseId = Shader.PropertyToID("_FogNoise");
        private readonly ProfilingSampler sampler = new ProfilingSampler("FlatWorld Dense Fog");
        private readonly DenseFogPresentationTransition transition = new();
        private Material passMaterial;
        private Camera sourceCamera;
        private Vector3 playerPosition;
        private Vector2 logicalPlayerPosition;
        private float targetStrength;
        private int sampledFrame = -1;
        private WorldRenderingConfig.RenderingDenseFog settings;

        public bool HasPresentation => transition.HasValue;

        public FogPass()
        {
            // 白雾盖住调色、泛光与所有世界名字，低血量红边仍可在其后提示危险。
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public void ResetPresentation()
        {
            transition.Reset();
            sampledFrame = -1;
            passMaterial = null;
            sourceCamera = null;
            playerPosition = default;
            logicalPlayerPosition = default;
            targetStrength = 0f;
            settings = null;
        }

        public void Setup(Material target, Camera source, Vector3 player, Vector2 logicalCenter, float strength,
            WorldRenderingConfig.RenderingDenseFog config)
        {
            passMaterial = target;
            sourceCamera = source;
            playerPosition = player;
            logicalPlayerPosition = logicalCenter;
            targetStrength = strength;
            settings = config;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (passMaterial == null || sourceCamera == null)
                return;

            // GPU 投影会读取当前颜色目标，只能在渲染 Pass 内连同雾圈过渡一起计算。
            ResolveWorldViewport(sourceCamera, renderingData.cameraData.IsCameraProjectionMatrixFlipped(), playerPosition,
                out Vector3 viewportOrigin, out Vector3 viewportRight, out Vector3 viewportUp);
            if (sampledFrame != Time.frameCount)
            {
                float outsideRadius = DenseFogPresentationTransition.ResolveOutsideRadius(viewportOrigin, viewportRight, viewportUp);
                transition.Step(targetStrength, Time.unscaledDeltaTime, outsideRadius, settings);
                sampledFrame = Time.frameCount;
            }
            if (targetStrength <= 0f && !transition.IsTransitioning)
                transition.Reset();
            if (!transition.IsVisible)
                return;

            Vector2 radii = transition.Radii;
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, sampler))
            {
                passMaterial.SetVector(OriginId, new Vector4(viewportOrigin.x, viewportOrigin.y, 0f, 0f));
                passMaterial.SetVector(RightId, new Vector4(viewportRight.x, viewportRight.y, 0f, 0f));
                passMaterial.SetVector(UpId, new Vector4(viewportUp.x, viewportUp.y, 0f, 0f));
                passMaterial.SetVector(CenterId, new Vector4(logicalPlayerPosition.x, logicalPlayerPosition.y, 0f, 0f));
                passMaterial.SetVector(RadiiId,
                    new Vector4(radii.x, radii.y, 0f, 0f));
                passMaterial.SetColor(ColorId, settings.color);
                passMaterial.SetVector(NoiseId,
                    new Vector4(settings.noiseScale, settings.driftSpeed, settings.noiseStrength, 0f));

                CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);
                cmd.DrawProcedural(Matrix4x4.identity, passMaterial, 0, MeshTopology.Triangles, 3, 1);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    #endregion
}
