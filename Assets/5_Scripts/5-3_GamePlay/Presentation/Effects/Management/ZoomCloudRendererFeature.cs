using UnityEngine;
using FlatWorld.Diagnostics;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>共用星球柔边云团，以玩家实际加载窗口为边界遮住远处未加载区域。</summary>
public sealed class ZoomCloudRendererFeature : ScriptableRendererFeature
{
    #region 性能分段
    // 此处只量 CPU 准备和命令录制，GPU 耗时仍看原有渲染采样器。
    private static readonly PerformanceDebugPoint PassSetupMarker = new("FlatWorld.Rendering.ZoomClouds.PassSetupCPU");
    private static readonly PerformanceDebugPoint CommandRecordMarker = new("FlatWorld.Rendering.ZoomClouds.RecordCommandsCPU");
    #endregion

    #region 本地云层状态

    [SerializeField] private Shader cloudShader;
    private Material material;
    private CloudPass pass;
    private Player localPlayer;
    private Mod_Cam cameraModule;
    private Mod_ChunkLoader chunkLoader;
    private bool hasSample;
    private int sampledFrame = -1;
    private int worldSceneHandle;
    private Vector2 driftVelocity;
    private Vector2 cloudOffset;
    private Vector2 cloudWorldCenter;
    private Vector2 lastLogicalPlayerPosition;

    private void ResetPresentation()
    {
        localPlayer = null;
        cameraModule = null;
        chunkLoader = null;
        hasSample = false;
        sampledFrame = -1;
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
        if (chunkLoader == null)
            chunkLoader = player.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
        if (chunkLoader == null || !chunkLoader.TryGetLoadingWindowBounds(out Rect activeBounds, out Rect presentationBounds))
            return;

        Camera sourceCamera = cameraModule != null ? cameraModule.ControllerCamera : null;
        if (sourceCamera == null || !sourceCamera.isActiveAndEnabled || !sourceCamera.orthographic ||
            !cameraData.resolveFinalTarget || !IsLocalStackCamera(sourceCamera, cameraData.camera))
            return;

        using var setupScope = PassSetupMarker.Measure(1);
        WorldRenderingConfig.RenderingZoomClouds config = WorldRenderingConfigCatalog.Default.zoomClouds;
        float universeOpacity = SurfaceZoomTransition.Sample(cameraModule);
        if (sampledFrame != Time.frameCount)
        {
            Vector2 logicalPlayer = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
            weather.TryGetWeatherAt(logicalPlayer, out var sample);
            Vector2 wind = new Vector2(sample.WindX, sample.WindY);
            Vector2 direction = wind.sqrMagnitude > 0.000001f ? wind.normalized : new Vector2(1f, 0.37f).normalized;
            float speed = Mathf.Lerp(config.minimumDriftSpeed, config.maximumDriftSpeed,
                Mathf.Pow(Mathf.Clamp01(sample.WindStrength), config.windResponseExponent));
            Vector2 targetVelocity = direction * speed;
            float seconds = Mathf.Max(0f, Time.deltaTime);
            if (!hasSample)
            {
                driftVelocity = targetVelocity;
                cloudWorldCenter = logicalPlayer;
            }
            else
            {
                driftVelocity = config.driftTransitionSeconds <= 0f ? targetVelocity :
                    Vector2.MoveTowards(driftVelocity, targetVelocity,
                        config.maximumDriftSpeed * seconds / config.driftTransitionSeconds);
                // 环绕边界只累加最短位移，云层不会因玩家坐标重投影突然换图。
                cloudWorldCenter += WorldTopologyRuntime.ShortestDelta(lastLogicalPlayerPosition, logicalPlayer);
            }
            // 积分漂移相位，风速变化不会把整片云瞬间跳到另一处。
            cloudOffset += driftVelocity * seconds;
            lastLogicalPlayerPosition = logicalPlayer;
            hasSample = true;
            sampledFrame = Time.frameCount;
        }
        // 宇宙画面已完全接管时停止绘制底下的云，缩近交接时再恢复。
        if (universeOpacity >= 1f && CameraZoomExtensions.GetProfile(cameraModule).MaximumRenderSize > 0f)
            return;

        pass.Setup(material, sourceCamera, player.transform.position, cloudWorldCenter,
            cloudOffset, activeBounds, presentationBounds, config);
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
        private readonly ProfilingSampler sampler = new ProfilingSampler("FlatWorld Zoom Clouds");
        private Material passMaterial;
        private Camera sourceCamera;
        private Vector3 playerPosition;
        private Vector2 worldCenter;
        private Vector2 offset;
        private Rect activeBounds;
        private Rect presentationBounds;
        private WorldRenderingConfig.RenderingZoomClouds settings;

        public CloudPass()
        {
            // 与天气雾和红边共用合成时段，渲染器登记顺序让红边仍处于最上层。
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public void Setup(Material target, Camera source, Vector3 player, Vector2 center,
            Vector2 driftOffset, Rect activeWindow, Rect presentationWindow,
            WorldRenderingConfig.RenderingZoomClouds config)
        {
            passMaterial = target;
            sourceCamera = source;
            playerPosition = player;
            worldCenter = center;
            offset = driftOffset;
            activeBounds = activeWindow;
            presentationBounds = presentationWindow;
            settings = config;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (passMaterial == null || sourceCamera == null)
                return;

            using var recordScope = CommandRecordMarker.Measure(1);
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, sampler))
            {
                ZoomCloudMaterialParameters.Apply(passMaterial, sourceCamera, renderingData.cameraData.IsCameraProjectionMatrixFlipped(),
                    playerPosition, worldCenter, offset, activeBounds, presentationBounds, settings);

                CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);
                cmd.DrawProcedural(Matrix4x4.identity, passMaterial, 0, MeshTopology.Triangles, 3, 1);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    #endregion
}

/// <summary>云圈世界投影和材质参数独立于渲染管线调度，供正式绘制与受限采样共用。</summary>
public static class ZoomCloudMaterialParameters
{
    #region 云圈材质参数

    private static readonly int OriginId = Shader.PropertyToID("_CloudViewportOrigin");
    private static readonly int RightId = Shader.PropertyToID("_CloudViewportRight");
    private static readonly int UpId = Shader.PropertyToID("_CloudViewportUp");
    private static readonly int CenterId = Shader.PropertyToID("_CloudWorldCenter");
    private static readonly int OffsetId = Shader.PropertyToID("_CloudDriftOffset");
    private static readonly int WindowId = Shader.PropertyToID("_CloudWindow");
    private static readonly int FeatherId = Shader.PropertyToID("_CloudFeather");
    private static readonly int ColorId = Shader.PropertyToID("_CloudColor");
    private static readonly int ShapeId = Shader.PropertyToID("_CloudShape");

    public static void Apply(Material target, Camera source, bool projectionFlipped,
        Vector3 playerPosition, Vector2 worldCenter, Vector2 driftOffset,
        Rect activeBounds, Rect presentationBounds,
        WorldRenderingConfig.RenderingZoomClouds settings)
    {
        Matrix4x4 projection = GL.GetGPUProjectionMatrix(source.projectionMatrix, projectionFlipped);
        Matrix4x4 inverse = (projection * source.worldToCameraMatrix).inverse;
        Vector3 bottomLeft = inverse.MultiplyPoint(new Vector3(-1f, -1f, 0f));
        Vector3 right = inverse.MultiplyPoint(new Vector3(1f, -1f, 0f)) - bottomLeft;
        Vector3 up = inverse.MultiplyPoint(new Vector3(-1f, 1f, 0f)) - bottomLeft;
        Vector3 origin = bottomLeft - playerPosition;
        target.SetVector(OriginId, new Vector4(origin.x, origin.y, 0f, 0f));
        target.SetVector(RightId, new Vector4(right.x, right.y, 0f, 0f));
        target.SetVector(UpId, new Vector4(up.x, up.y, 0f, 0f));
        target.SetVector(CenterId, new Vector4(worldCenter.x, worldCenter.y, 0f, 0f));
        target.SetVector(OffsetId, new Vector4(driftOffset.x, driftOffset.y, 0f, 0f));
        Vector2 centerOffset = presentationBounds.center - (Vector2)playerPosition;
        Vector2 opaqueHalfSize = presentationBounds.size * 0.5f;
        // 活动窗完全清晰，柔雾用额外表现圈；没有额外圈时仅内收一格保留柔边。
        Vector2 clearHalfSize = new Vector2(
            Mathf.Min(activeBounds.width * 0.5f, Mathf.Max(0f, opaqueHalfSize.x - 1f)),
            Mathf.Min(activeBounds.height * 0.5f, Mathf.Max(0f, opaqueHalfSize.y - 1f)));
        Vector2 feather = opaqueHalfSize - clearHalfSize;
        target.SetVector(WindowId, new Vector4(centerOffset.x, centerOffset.y, clearHalfSize.x, clearHalfSize.y));
        target.SetVector(FeatherId, new Vector4(feather.x, feather.y, 0f, 0f));
        target.SetColor(ColorId, settings.color);
        target.SetVector(ShapeId, new Vector4(settings.noiseScale, settings.shadowStrength, 0f, 0f));
    }

    #endregion
}
