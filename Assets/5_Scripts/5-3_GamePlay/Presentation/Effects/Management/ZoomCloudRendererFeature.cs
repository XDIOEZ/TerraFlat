using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>高视野云雾只合成本机最终世界画面，缩放控制遮挡，当地风控制云层漂移。</summary>
public sealed class ZoomCloudRendererFeature : ScriptableRendererFeature
{
    #region 本地云层状态

    [SerializeField] private Shader cloudShader;
    private Material material;
    private CloudPass pass;
    private Player localPlayer;
    private Mod_Cam cameraModule;
    private bool hasSample;
    private int sampledFrame = -1;
    private int worldSceneHandle;
    private float visibleStrength;
    private Vector2 driftVelocity;
    private Vector2 cloudOffset;
    private Vector2 cloudWorldCenter;
    private Vector2 lastLogicalPlayerPosition;
    private const float MinimumVisibleStrength = 0.0005f;

    private void ResetPresentation()
    {
        localPlayer = null;
        cameraModule = null;
        hasSample = false;
        sampledFrame = -1;
        visibleStrength = 0f;
        driftVelocity = cloudOffset = cloudWorldCenter = lastLogicalPlayerPosition = Vector2.zero;
    }

    #endregion

    #region 渲染器生命周期

    public override void Create()
    {
        CoreUtils.Destroy(material);
        material = null;
        pass = null;
        ResetPresentation();
        if (cloudShader == null)
        {
            Debug.LogError("[ZoomCloud] 渲染器资源缺少高视野云雾 Shader。", this);
            return;
        }
        material = CoreUtils.CreateEngineMaterial(cloudShader);
        pass = new CloudPass();
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

        if (player != localPlayer || player.gameObject.scene.handle != worldSceneHandle)
        {
            ResetPresentation();
            localPlayer = player;
            worldSceneHandle = player.gameObject.scene.handle;
        }
        if (cameraModule == null)
            cameraModule = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);

        Camera sourceCamera = cameraModule != null ? cameraModule.ControllerCamera : null;
        if (sourceCamera == null || !sourceCamera.isActiveAndEnabled || !sourceCamera.orthographic ||
            !cameraData.resolveFinalTarget || !IsLocalStackCamera(sourceCamera, cameraData.camera))
            return;

        WorldRenderingConfig.RenderingZoomClouds config = WorldRenderingConfigCatalog.Default.zoomClouds;
        if (sampledFrame != Time.frameCount)
        {
            Vector2 logicalPlayer = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
            weather.TryGetWeatherAt(logicalPlayer, out var sample);
            float target = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                config.startOrthographicSize, config.opaqueOrthographicSize, sourceCamera.orthographicSize));
            Vector2 wind = new Vector2(sample.WindX, sample.WindY);
            Vector2 direction = wind.sqrMagnitude > 0.000001f ? wind.normalized : new Vector2(1f, 0.37f).normalized;
            float speed = Mathf.Lerp(config.minimumDriftSpeed, config.maximumDriftSpeed,
                Mathf.Pow(Mathf.Clamp01(sample.WindStrength), config.windResponseExponent));
            Vector2 targetVelocity = direction * speed;
            float seconds = Mathf.Max(0f, Time.deltaTime);
            if (!hasSample)
            {
                visibleStrength = target;
                driftVelocity = targetVelocity;
                cloudWorldCenter = logicalPlayer;
            }
            else
            {
                visibleStrength = config.transitionSeconds <= 0f ? target :
                    Mathf.MoveTowards(visibleStrength, target, seconds / config.transitionSeconds);
                driftVelocity = config.transitionSeconds <= 0f ? targetVelocity :
                    Vector2.MoveTowards(driftVelocity, targetVelocity,
                        config.maximumDriftSpeed * seconds / config.transitionSeconds);
                // 环绕边界只累加最短位移，云层不会因玩家坐标重投影突然换图。
                cloudWorldCenter += WorldTopologyRuntime.ShortestDelta(lastLogicalPlayerPosition, logicalPlayer);
            }
            // 积分漂移相位，风速变化不会把整片云瞬间跳到另一处。
            cloudOffset += driftVelocity * seconds;
            lastLogicalPlayerPosition = logicalPlayer;
            hasSample = true;
            sampledFrame = Time.frameCount;
        }
        if (visibleStrength <= MinimumVisibleStrength)
            return;

        pass.Setup(material, sourceCamera, player.transform.position, cloudWorldCenter,
            cloudOffset, visibleStrength, config);
        renderer.EnqueuePass(pass);
    }

    private static bool IsLocalStackCamera(Camera source, Camera candidate)
    {
        if (source == candidate)
            return true;
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

    #region GPU 云层合成

    private sealed class CloudPass : ScriptableRenderPass
    {
        private static readonly int OriginId = Shader.PropertyToID("_CloudViewportOrigin");
        private static readonly int RightId = Shader.PropertyToID("_CloudViewportRight");
        private static readonly int UpId = Shader.PropertyToID("_CloudViewportUp");
        private static readonly int CenterId = Shader.PropertyToID("_CloudWorldCenter");
        private static readonly int OffsetId = Shader.PropertyToID("_CloudDriftOffset");
        private static readonly int RadiiId = Shader.PropertyToID("_CloudRadiiStrength");
        private static readonly int ColorId = Shader.PropertyToID("_CloudColor");
        private static readonly int ShapeId = Shader.PropertyToID("_CloudShape");
        private readonly ProfilingSampler sampler = new ProfilingSampler("FlatWorld Zoom Clouds");
        private Material passMaterial;
        private Camera sourceCamera;
        private Vector3 playerPosition;
        private Vector2 worldCenter;
        private Vector2 offset;
        private float strength;
        private WorldRenderingConfig.RenderingZoomClouds settings;

        public CloudPass()
        {
            // 与天气雾和红边共用合成时段，渲染器登记顺序让红边仍处于最上层。
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public void Setup(Material target, Camera source, Vector3 player, Vector2 center,
            Vector2 driftOffset, float opacity, WorldRenderingConfig.RenderingZoomClouds config)
        {
            passMaterial = target;
            sourceCamera = source;
            playerPosition = player;
            worldCenter = center;
            offset = driftOffset;
            strength = opacity;
            settings = config;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (passMaterial == null || sourceCamera == null || strength <= MinimumVisibleStrength)
                return;

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
                passMaterial.SetVector(CenterId, new Vector4(worldCenter.x, worldCenter.y, 0f, 0f));
                passMaterial.SetVector(OffsetId, new Vector4(offset.x, offset.y, 0f, 0f));
                passMaterial.SetVector(RadiiId,
                    new Vector4(settings.clearRadius, settings.opaqueRadius, strength, 0f));
                passMaterial.SetColor(ColorId, settings.color);
                passMaterial.SetVector(ShapeId, new Vector4(settings.noiseScale, settings.shadowStrength, 0f, 0f));

                CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);
                cmd.DrawProcedural(Matrix4x4.identity, passMaterial, 0, MeshTopology.Triangles, 3, 1);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    #endregion
}
