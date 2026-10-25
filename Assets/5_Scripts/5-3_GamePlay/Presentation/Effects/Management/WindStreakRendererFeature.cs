using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>在本地世界画面合成顺风条纹，无风时不提交绘制。</summary>
public sealed class WindStreakRendererFeature : ScriptableRendererFeature
{
    #region 本机风效与生命周期

    [SerializeField] private Shader windShader;

    private readonly WindStreakPresentation presentation = new();
    private Material material;
    private WindPass pass;
    private Player localPlayer;
    private Mod_Cam cameraModule;
    private Camera sourceCamera;
    private int worldSceneHandle;
    private int sampledFrame = -1;
    private Vector2 lastCameraPosition;
    private bool hasCameraPosition;

    public override void Create()
    {
        ResetPresentation();
        CoreUtils.Destroy(material);
        material = null;
        pass = null;
        if (windShader == null)
        {
            Debug.LogError("[WindStreaks] 渲染器资源缺少风条纹 Shader。", this);
            return;
        }
        material = CoreUtils.CreateEngineMaterial(windShader);
        pass = new WindPass();
    }

    private void ResetPresentation()
    {
        presentation.Reset();
        localPlayer = null;
        cameraModule = null;
        sourceCamera = null;
        sampledFrame = -1;
        worldSceneHandle = 0;
        lastCameraPosition = Vector2.zero;
        hasCameraPosition = false;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!Application.isPlaying || material == null || pass == null)
            return;

        ref CameraData cameraData = ref renderingData.cameraData;
        if (cameraData.cameraType != CameraType.Game)
            return;

        Player player = ItemMgr.GetExistingInstance()?.User_Player;
        WeatherMgr weather = player != null ? WeatherMgr.GetForWorld(player.gameObject.scene.name, false) : null;
        if (player == null || !player.IsLocalProfile || !player.gameObject.activeInHierarchy ||
            weather == null || !weather.isActiveAndEnabled || !weather.IsLocalWeatherWorld)
        {
            ResetPresentation();
            return;
        }

        if (localPlayer != player || worldSceneHandle != player.gameObject.scene.handle)
        {
            ResetPresentation();
            localPlayer = player;
            worldSceneHandle = player.gameObject.scene.handle;
        }
        if (cameraModule == null)
            cameraModule = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
        Camera source = cameraModule != null ? cameraModule.ControllerCamera : null;
        if (source == null || !source.isActiveAndEnabled || !source.orthographic)
        {
            ResetPresentation();
            return;
        }
        if (!cameraData.resolveFinalTarget || !IsLocalStackCamera(source, cameraData.camera))
            return;
        if (sourceCamera != source)
        {
            presentation.Reset();
            hasCameraPosition = false;
            sourceCamera = source;
            sampledFrame = -1;
        }

        WorldRenderingConfig.RenderingWindStreaks config = WorldRenderingConfigCatalog.Default.windStreaks;
        if (sampledFrame != Time.frameCount)
        {
            Vector2 cameraPosition = WorldLocalPresentation.ToLogical((Vector2)source.transform.position);
            Vector2 delta = hasCameraPosition
                ? WorldTopologyRuntime.ShortestDelta(lastCameraPosition, cameraPosition) : Vector2.zero;
            presentation.Step(weather.PresentedWind, delta, Time.deltaTime, config);
            lastCameraPosition = cameraPosition;
            hasCameraPosition = presentation.IsVisible;
            sampledFrame = Time.frameCount;
        }
        if (!presentation.IsVisible || SurfaceZoomTransition.Sample(cameraModule) >= 1f)
            return;

        pass.Setup(material, source, presentation, config);
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
        ResetPresentation();
        CoreUtils.Destroy(material);
        material = null;
        pass = null;
    }

    #endregion

    #region 单次 GPU 合成

    private sealed class WindPass : ScriptableRenderPass
    {
        private static readonly int OriginId = Shader.PropertyToID("_WindViewportOrigin");
        private static readonly int RightId = Shader.PropertyToID("_WindViewportRight");
        private static readonly int UpId = Shader.PropertyToID("_WindViewportUp");
        private static readonly int DirectionId = Shader.PropertyToID("_WindStreakDirection");
        private static readonly int PatternId = Shader.PropertyToID("_WindStreakPattern");
        private static readonly int ShapeId = Shader.PropertyToID("_WindStreakShape");
        private static readonly int ColorId = Shader.PropertyToID("_WindStreakColor");
        private readonly ProfilingSampler sampler = new("FlatWorld Wind Streaks");
        private Material passMaterial;
        private Camera sourceCamera;
        private WindStreakPresentation state;
        private WorldRenderingConfig.RenderingWindStreaks settings;

        public WindPass() => renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        public void Setup(Material target, Camera source, WindStreakPresentation presentation,
            WorldRenderingConfig.RenderingWindStreaks config)
        {
            passMaterial = target;
            sourceCamera = source;
            state = presentation;
            settings = config;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (passMaterial == null || sourceCamera == null || !state.IsVisible)
                return;

            // GPU 投影必须等颜色目标就绪再读取，最终补绘相机仍使用本地主相机的世界基底。
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(sourceCamera.projectionMatrix,
                renderingData.cameraData.IsCameraProjectionMatrixFlipped());
            Matrix4x4 inverse = (projection * sourceCamera.worldToCameraMatrix).inverse;
            Vector3 bottomLeft = inverse.MultiplyPoint(new Vector3(-1f, -1f, 0f));
            Vector3 right = inverse.MultiplyPoint(new Vector3(1f, -1f, 0f)) - bottomLeft;
            Vector3 up = inverse.MultiplyPoint(new Vector3(-1f, 1f, 0f)) - bottomLeft;
            Vector3 origin = bottomLeft - sourceCamera.transform.position;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, sampler))
            {
                passMaterial.SetVector(OriginId, new Vector4(origin.x, origin.y, 0f, 0f));
                passMaterial.SetVector(RightId, new Vector4(right.x, right.y, 0f, 0f));
                passMaterial.SetVector(UpId, new Vector4(up.x, up.y, 0f, 0f));
                passMaterial.SetVector(DirectionId, new Vector4(state.Direction.x, state.Direction.y, 0f, 0f));
                passMaterial.SetVector(PatternId,
                    new Vector4(state.Phase.x, state.Phase.y, settings.cellSize.x, settings.cellSize.y));
                passMaterial.SetVector(ShapeId,
                    new Vector4(state.Parameters.y, state.Parameters.z, state.Parameters.w, settings.width));
                passMaterial.SetColor(ColorId, settings.color);
                CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);
                cmd.DrawProcedural(Matrix4x4.identity, passMaterial, 0, MeshTopology.Triangles, 3, 1);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    #endregion
}
