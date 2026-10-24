using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>地表拉远后的宇宙观察只合成画面，不迁移角色、场景或权威宇宙坐标。</summary>
public sealed class UniverseOverviewRendererFeature : ScriptableRendererFeature, ICameraZoomPolicy
{
    #region 观察策略与生命周期

    [SerializeField] private Shader overviewShader;
    [SerializeField] private Material planetSurfaceMaterial;
    private Material material;
    private OverviewPass pass;
    private IDisposable zoomRegistration;
    private Player playerOwner;
    private SpaceSessionState stateOwner;
    private int sceneHandle;
    private float opacity;

    public override void Create()
    {
        Release();
        if (overviewShader == null || planetSurfaceMaterial == null ||
            planetSurfaceMaterial.FindPass("PlanetUnlit") < 0)
        {
            Debug.LogError("[UniverseView] 渲染器缺少宇宙观察 Shader 或星球无光照绘制 Pass。", this);
            return;
        }
        material = CoreUtils.CreateEngineMaterial(overviewShader);
        pass = new OverviewPass(material, planetSurfaceMaterial);
        zoomRegistration = CameraZoomExtensions.Register(this);
    }

    public bool TryGetProfile(Mod_Cam camera, out CameraZoomProfile profile)
    {
        profile = default;
        if (!Application.isPlaying || !isActive || pass == null || camera == null || camera.HasFollowOverride ||
            camera.Player == null || !camera.Player.IsLocalProfile || !camera.isActiveAndEnabled)
            return false;
        var config = WorldRenderingConfigCatalog.Default.universeView;
        if (SpaceMgr.IsSpaceScene(camera.Player.gameObject.scene.name))
        {
            profile = new CameraZoomProfile(config.maximumOrthographicSize, camera.MinPovValue,
                config.zoomMultiplier);
            return true;
        }
        if (!TryResolveAnchor(camera, out _, out _)) return false;
        profile = new CameraZoomProfile(config.maximumOrthographicSize, config.startOrthographicSize,
            config.zoomMultiplier, config.startOrthographicSize);
        return true;
    }

    public float ClampInputSize(Mod_Cam camera, float currentSize, float requestedSize)
    {
        if (!TryResolveAnchor(camera, out _, out _)) return requestedSize;
        return SurfaceZoomTransition.ClampInputSize(currentSize, requestedSize,
            SurfaceZoomTransition.Sample(camera), WorldRenderingConfigCatalog.Default.universeView);
    }

    private static bool TryResolveAnchor(Mod_Cam camera, out SpaceSession session, out BodyState body)
    {
        session = SpaceSession.Current;
        body = null;
        DimensionManager dimensions = DimensionManager.ExistingInstance;
        Player player = camera != null ? camera.Player : null;
        if (session?.State == null || player == null || !player.IsLocalProfile ||
            !player.gameObject.activeInHierarchy || camera.HasFollowOverride ||
            dimensions == null || dimensions.IsTransitioning || !dimensions.ActiveAddress.IsSurface ||
            SpaceMgr.IsSpaceScene(player.gameObject.scene.name)) return false;
        var save = SaveDataMgr.Instance?.SaveData;
        return save?.PlanetData_Dict != null &&
               save.PlanetData_Dict.TryGetValue(dimensions.ActiveAddress.PlanetId, out PlanetData planet) &&
               session.TryGetBody(planet.BodyId, out body);
    }

    private void ResetView()
    {
        playerOwner = null;
        stateOwner = null;
        opacity = 0f;
        pass?.Reset();
    }

    private void Release()
    {
        zoomRegistration?.Dispose();
        zoomRegistration = null;
        ResetView();
        pass?.Dispose();
        pass = null;
        CoreUtils.Destroy(material);
        material = null;
    }

    protected override void Dispose(bool disposing) => Release();

    #endregion

    #region 最终本地画面与云层过渡

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!Application.isPlaying || pass == null || renderingData.cameraData.cameraType != CameraType.Game)
            return;
        Player player = ItemMgr.GetInstance()?.User_Player;
        Mod_Cam camera = player?.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
        if (!TryResolveAnchor(camera, out SpaceSession session, out BodyState anchor))
        {
            ResetView();
            return;
        }
        Camera source = camera.ControllerCamera;
        ref CameraData cameraData = ref renderingData.cameraData;
        if (source == null || !source.isActiveAndEnabled || !cameraData.resolveFinalTarget ||
            !IsLocalStackCamera(source, cameraData.camera)) return;
        if (playerOwner != player || !ReferenceEquals(stateOwner, session.State) ||
            sceneHandle != player.gameObject.scene.handle)
        {
            ResetView();
            playerOwner = player;
            stateOwner = session.State;
            sceneHandle = player.gameObject.scene.handle;
        }
        var config = WorldRenderingConfigCatalog.Default.universeView;
        float size = camera.CurrentOrthographicSize;
        // 与地表云层共用淡入淡出进度，切换宇宙时不收缩玩家周围的固定雾圈。
        opacity = SurfaceZoomTransition.Sample(camera);
        if (opacity <= 0.0005f)
        {
            pass.ReleaseTexture();
            return;
        }
        // 过渡完成时只露出星球局部，继续拉远才逐步看到整颗星球。
        double halfHeight = anchor.RadiusMeters * config.entryPlanetRadiusMultiplier *
                            (Math.Max(config.startOrthographicSize, (double)size) / config.opaqueOrthographicSize);
        pass.Setup(session, anchor, halfHeight, opacity, config.minimumBodyPixels);
        renderer.EnqueuePass(pass);
    }

    private static bool IsLocalStackCamera(Camera source, Camera candidate)
        => source == candidate || source.TryGetComponent(out UniversalAdditionalCameraData data) &&
           data.renderType == CameraRenderType.Base && data.cameraStack != null && data.cameraStack.Contains(candidate);

    #endregion

    #region 真实星体远景的 GPU 合成

    private sealed class OverviewPass : ScriptableRenderPass, IDisposable
    {
        private static readonly int ScreenId = Shader.PropertyToID("_UniverseScreenSize");
        private static readonly int BlendId = Shader.PropertyToID("_UniverseBlend");
        private static readonly int LightId = Shader.PropertyToID("_SurfaceLightDirection");
        private static readonly int SeedId = Shader.PropertyToID("_SurfaceSeed");
        private readonly Material overviewMaterial;
        private readonly Material planetMaterial;
        private readonly int planetPass;
        private readonly Mesh quad;
        private readonly ProfilingSampler sampler = new("FlatWorld Universe Overview");
        private readonly Dictionary<string, MaterialPropertyBlock> appearances = new(StringComparer.Ordinal);
        private readonly HashSet<string> missingPrefabs = new(StringComparer.Ordinal);
        private readonly Dictionary<SpaceSectorId, UniverseSimulation> previews = new();
        private readonly HashSet<SpaceSectorId> drawnSectors = new();
        private RTHandle texture;
        private SpaceSession session;
        private BodyState anchor;
        private double halfHeight;
        private float opacity;
        private float minimumPixels;

        public OverviewPass(Material overview, Material planet)
        {
            overviewMaterial = overview;
            planetMaterial = planet;
            planetPass = planet.FindPass("PlanetUnlit");
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            quad = new Mesh { name = "宇宙观察星体四边形" };
            quad.vertices = new[] { new Vector3(-1f, -1f), new Vector3(-1f, 1f),
                new Vector3(1f, 1f), new Vector3(1f, -1f) };
            quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateBounds();
            quad.UploadMeshData(true);
        }

        public void Setup(SpaceSession current, BodyState center, double height, float blend, float pixels)
        { session = current; anchor = center; halfHeight = height; opacity = blend; minimumPixels = pixels; }

        public void ReleaseTexture() { texture?.Release(); texture = null; }
        public void Reset()
        {
            ReleaseTexture();
            appearances.Clear(); missingPrefabs.Clear(); previews.Clear(); drawnSectors.Clear();
            session = null; anchor = null;
        }
        public void Dispose() { Reset(); CoreUtils.Destroy(quad); }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (session?.State == null || anchor == null) return;
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.useMipMap = descriptor.autoGenerateMips = false;
            RenderingUtils.ReAllocateIfNeeded(ref texture, descriptor, FilterMode.Bilinear,
                TextureWrapMode.Clamp, name: "UniverseOverview");
            float aspect = (float)descriptor.width / Mathf.Max(1, descriptor.height);
            CommandBuffer cmd = CommandBufferPool.Get();
            try
            {
                using (new ProfilingScope(cmd, sampler))
                {
                    CoreUtils.SetRenderTarget(cmd, texture, ClearFlag.Color, Color.black);
                    overviewMaterial.SetVector(ScreenId, new Vector4(descriptor.width, descriptor.height, 0f, 0f));
                    cmd.DrawProcedural(Matrix4x4.identity, overviewMaterial, 0, MeshTopology.Triangles, 3);
                    Matrix4x4 projection = GL.GetGPUProjectionMatrix(
                        Matrix4x4.Ortho(-aspect, aspect, -1f, 1f, -1f, 1f), true);
                    RenderingUtils.SetViewAndProjectionMatrices(cmd, Matrix4x4.identity, projection, true);
                    drawnSectors.Clear();
                    foreach (UniverseState state in session.State.Sectors)
                    {
                        drawnSectors.Add(state.Sector);
                        DrawSector(cmd, state, aspect, descriptor.height);
                    }
                    // 尚未进入的配置星区只建只读目录副本，观察不会创建场景或写入权威存档。
                    var catalog = SpaceCatalog.LoadDefault();
                    foreach (SpaceSectorId sector in catalog.ConfiguredSectors)
                    {
                        if (!drawnSectors.Add(sector)) continue;
                        if (!previews.TryGetValue(sector, out UniverseSimulation preview))
                        {
                            UniverseState state = catalog.GenerateSector(SaveDataMgr.Instance.SaveData.Seed, sector);
                            state.SimulationSeconds = session.State.SimulationSeconds;
                            previews.Add(sector, preview = new UniverseSimulation(state));
                        }
                        double delta = session.State.SimulationSeconds - preview.State.SimulationSeconds;
                        if (delta > 0d) preview.AdvanceTime(delta);
                        DrawSector(cmd, preview.State, aspect, descriptor.height);
                    }
                    RenderingUtils.SetViewAndProjectionMatrices(cmd, renderingData.cameraData.GetViewMatrix(),
                        renderingData.cameraData.GetGPUProjectionMatrix(), true);
                    overviewMaterial.SetFloat(BlendId, opacity);
                    Blitter.BlitCameraTexture(cmd, texture, renderingData.cameraData.renderer.cameraColorTargetHandle,
                        overviewMaterial, 1);
                }
                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        private void DrawSector(CommandBuffer cmd, UniverseState state, float aspect, int pixelHeight)
        {
            BodyState sun = null;
            foreach (BodyState body in state.Bodies)
                if (string.IsNullOrEmpty(body.ParentBodyId)) { sun = body; break; }
            foreach (BodyState body in state.Bodies)
            {
                SpaceVector2 relative = SpaceSectorCoordinates.Relative(body.Sector, body.PositionMeters,
                    anchor.Sector) - anchor.PositionMeters;
                double x = relative.X / halfHeight, y = relative.Y / halfHeight;
                double radius = Math.Max(body.RadiusMeters / halfHeight, minimumPixels * 2d / Math.Max(1, pixelHeight));
                double extent = radius * 1.3d;
                if (Math.Abs(x) > aspect + extent || Math.Abs(y) > 1d + extent) continue;
                if (!appearances.TryGetValue(body.PrefabId, out MaterialPropertyBlock properties))
                {
                    GameObject prefab = GameRes.ExistingInstance?.GetPrefab(body.PrefabId, false);
                    Planet planet = prefab != null ? prefab.GetComponent<Planet>() : null;
                    if (planet == null)
                    {
                        if (missingPrefabs.Add(body.PrefabId)) Debug.LogError("[UniverseView] 星体外观未加载：" + body.PrefabId);
                        continue;
                    }
                    properties = new MaterialPropertyBlock();
                    planet.ApplySurfaceProperties(properties);
                    appearances.Add(body.PrefabId, properties);
                    missingPrefabs.Remove(body.PrefabId);
                }
                double angle = body.RotationInitialRadians + (Math.Abs(body.RotationPeriodSeconds) > 1e-12d
                    ? state.SimulationSeconds * Math.PI * 2d / body.RotationPeriodSeconds : 0d);
                Quaternion rotation = Quaternion.Euler(0f, 0f, (float)(body.EquatorTiltRadians * 180d / Math.PI));
                SpaceVector2 light = sun != null ? (sun.PositionMeters - body.PositionMeters).Normalized : default;
                Vector3 direction = Quaternion.Inverse(rotation) * new Vector3((float)light.X, (float)light.Y, .8f);
                properties.SetVector(LightId, direction.normalized);
                properties.SetFloat(SeedId, (body.Seed & 0xFFFF) * .0137f);
                properties.SetFloat("_SurfaceRotation", (float)Math.IEEERemainder(angle, Math.PI * 2d));
                float surfaceExtent = properties.GetFloat("_SurfaceExtent");
                float scale = (float)radius * surfaceExtent;
                cmd.DrawMesh(quad, Matrix4x4.TRS(new Vector3((float)x, (float)y, 0f), rotation,
                    new Vector3(scale, scale, 1f)), planetMaterial, 0, planetPass, properties);
            }
        }
    }

    #endregion
}
