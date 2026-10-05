using UnityEngine;
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
    private bool hasWeatherSample;
    private float visibleStrength;
    private int sampledFrame = -1;
    private const float MinimumVisibleStrength = 0.0005f;

    private void ResetPresentation()
    {
        localPlayer = null;
        cameraModule = null;
        hasWeatherSample = false;
        visibleStrength = 0f;
        sampledFrame = -1;
    }

    #endregion

    #region 渲染器生命周期

    public override void Create()
    {
        CoreUtils.Destroy(material);
        material = null;
        pass = null;
        ResetPresentation();
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

        if (player != localPlayer)
        {
            ResetPresentation();
            localPlayer = player;
        }
        if (cameraModule == null)
            cameraModule = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);

        Camera sourceCamera = cameraModule != null ? cameraModule.ControllerCamera : null;
        if (sourceCamera == null || !sourceCamera.isActiveAndEnabled || !sourceCamera.orthographic ||
            !cameraData.resolveFinalTarget || !IsLocalStackCamera(sourceCamera, cameraData.camera))
            return;

        float target = weather.CurrentWeather == WeatherType.Fog ? weather.CurrentWeatherIntensity : 0f;
        WorldRenderingConfig.RenderingDenseFog config = WorldRenderingConfigCatalog.Default.denseFog;
        if (sampledFrame != Time.frameCount)
        {
            // 雾中读档、入房或重生直接显示现有浓度，不先暴露一帧远处玩家。
            visibleStrength = !hasWeatherSample || config.transitionSeconds <= 0f
                ? target
                : Mathf.MoveTowards(visibleStrength, target, Time.deltaTime / config.transitionSeconds);
            hasWeatherSample = true;
            sampledFrame = Time.frameCount;
        }
        if (visibleStrength <= MinimumVisibleStrength)
            return;

        pass.Setup(material, sourceCamera, player.transform.position, visibleStrength, config);
        renderer.EnqueuePass(pass);
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
        CoreUtils.Destroy(material);
        material = null;
        pass = null;
        ResetPresentation();
    }

    #endregion

    #region 最终画面合成

    private sealed class FogPass : ScriptableRenderPass
    {
        private static readonly int OriginId = Shader.PropertyToID("_FogViewportOrigin");
        private static readonly int RightId = Shader.PropertyToID("_FogViewportRight");
        private static readonly int UpId = Shader.PropertyToID("_FogViewportUp");
        private static readonly int CenterId = Shader.PropertyToID("_FogWorldCenter");
        private static readonly int RadiiId = Shader.PropertyToID("_FogRadiiStrength");
        private static readonly int ColorId = Shader.PropertyToID("_FogColor");
        private static readonly int NoiseId = Shader.PropertyToID("_FogNoise");
        private readonly ProfilingSampler sampler = new ProfilingSampler("FlatWorld Dense Fog");
        private Material passMaterial;
        private Camera sourceCamera;
        private Vector3 playerPosition;
        private float strength;
        private WorldRenderingConfig.RenderingDenseFog settings;

        public FogPass()
        {
            // 白雾盖住调色、泛光与所有世界名字，低血量红边仍可在其后提示危险。
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public void Setup(Material target, Camera source, Vector3 center, float opacity,
            WorldRenderingConfig.RenderingDenseFog config)
        {
            passMaterial = target;
            sourceCamera = source;
            playerPosition = center;
            strength = opacity;
            settings = config;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (passMaterial == null || sourceCamera == null)
                return;

            // 由实际 GPU 投影恢复世界 XY，兼容渲染纹理翻转、宽高比、缩放与环绕补绘。
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(sourceCamera.projectionMatrix,
                renderingData.cameraData.IsCameraProjectionMatrixFlipped());
            Matrix4x4 inverse = (projection * sourceCamera.worldToCameraMatrix).inverse;
            Vector3 bottomLeft = inverse.MultiplyPoint(new Vector3(-1f, -1f, 0f));
            Vector3 right = inverse.MultiplyPoint(new Vector3(1f, -1f, 0f)) - bottomLeft;
            Vector3 up = inverse.MultiplyPoint(new Vector3(-1f, 1f, 0f)) - bottomLeft;
            Vector3 origin = bottomLeft - playerPosition;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, sampler))
            {
                passMaterial.SetVector(OriginId, new Vector4(origin.x, origin.y, 0f, 0f));
                passMaterial.SetVector(RightId, new Vector4(right.x, right.y, 0f, 0f));
                passMaterial.SetVector(UpId, new Vector4(up.x, up.y, 0f, 0f));
                passMaterial.SetVector(CenterId, new Vector4(playerPosition.x, playerPosition.y, 0f, 0f));
                passMaterial.SetVector(RadiiId,
                    new Vector4(settings.clearRadius, settings.opaqueRadius, strength, 0f));
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
