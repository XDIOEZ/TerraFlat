#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class GameplayTestSystemProbe
{
    #region 一万火格隔离相机实际渲染基准

    private const int FireRenderBenchmarkWidth = 1920;
    private const int FireRenderBenchmarkHeight = 1080;
    private const int FireRenderBenchmarkLayer = 31;
    private const int FireRenderBenchmarkSampleFrames = 60;

    [InitializeOnLoadMethod]
    private static void RegisterWorldFireRenderBenchmarkInEditor()
        => RegisterWorldFireRenderBenchmarkOperation();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void RegisterWorldFireRenderBenchmarkAtRuntime()
        => RegisterWorldFireRenderBenchmarkOperation();

    private static void RegisterWorldFireRenderBenchmarkOperation()
        => GameplayTestRuntimeProbe.RegisterAsync("rendering", "world_fire_render_benchmark",
            CaptureWorldFireRenderBenchmark);

    private static IEnumerator CaptureWorldFireRenderBenchmark(Player player, JObject args,
        Action<JObject> completed)
    {
        const string chunkViewPath = "Assets/2_Prefabs/World/WorldModel/ChunkView.prefab";
        GameObject chunkViewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(chunkViewPath);
        ChunkTilemapRenderer chunkView = chunkViewPrefab != null
            ? chunkViewPrefab.GetComponent<ChunkTilemapRenderer>() : null;
        var serializedChunkView = chunkView != null ? new SerializedObject(chunkView) : null;
        Sprite fireSprite = serializedChunkView?.FindProperty("fireSprite")?.objectReferenceValue as Sprite;
        Material fireMaterial = serializedChunkView?.FindProperty("fireMaterial")?.objectReferenceValue as Material;
        Shader depthMeshShader = Resources.Load<Shader>("Shaders/ChunkDepthSpriteLit");
        if (fireSprite == null || fireMaterial == null || depthMeshShader == null)
            throw new InvalidOperationException("火场现场基准缺少 ChunkView 正式 Sprite、材质或深度 Shader。");

        int warmupFrames = ReadFireBenchmarkFrameCount(args, "warmupFrames", 12, 2, 120);
        int sampleFrames = ReadFireBenchmarkFrameCount(args, "sampleFrames", FireRenderBenchmarkSampleFrames, 10, 600);
        var fixture = new WorldFireRenderBenchmarkFixture();
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        GameplayTestRuntimeProbe.RegisterAsyncCleanup(fixture.CleanupAsync);
        try
        {
            fixture.Build(fireSprite, fireMaterial, depthMeshShader);
            fixture.BeginCameraSampling();
            var endOfFrame = new WaitForEndOfFrame();
            for (int frame = 0; frame < warmupFrames; frame++)
                yield return endOfFrame;

            fixture.StartMetrics(sampleFrames);
            for (int frame = 0; frame < sampleFrames; frame++)
            {
                yield return endOfFrame;
                fixture.SampleProfilerMetrics();
                fixture.SampleFrameTiming();
            }

            fixture.StopCameraSampling();
            fixture.StopMetrics();
            fixture.CapturePreviewImage();
            completed(fixture.CreateResult(warmupFrames, sampleFrames));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static int ReadFireBenchmarkFrameCount(JObject args, string property, int fallback,
        int minimum, int maximum)
    {
        if (args == null || args[property] == null) return fallback;
        if (!int.TryParse(args[property].ToString(), out int value))
            throw new ArgumentException("火场渲染基准帧数无效：" + property);
        return Mathf.Clamp(value, minimum, maximum);
    }

    private sealed class WorldFireRenderBenchmarkFixture : IDisposable
    {
        private readonly List<Transform> owners = new(GameplayTestSystemProbe.FireBatchProbeChunkSize *
            GameplayTestSystemProbe.FireBatchProbeChunkSize);
        private readonly List<ChunkDepthMeshRenderer> renderers = new(GameplayTestSystemProbe.FireBatchProbeChunkSize *
            GameplayTestSystemProbe.FireBatchProbeChunkSize);
        private readonly List<Camera> disabledCameras = new();
        private readonly FrameTiming[] latestFrameTiming = new FrameTiming[1];
        private readonly GameplayTestSystemProbe.NumericMetric cpuFrameMilliseconds = new();
        private readonly GameplayTestSystemProbe.NumericMetric cpuMainThreadMilliseconds = new();
        private readonly GameplayTestSystemProbe.NumericMetric cpuRenderThreadMilliseconds = new();
        private readonly GameplayTestSystemProbe.NumericMetric gpuFrameMilliseconds = new();
        private GameplayTestSystemProbe.NumericMetric drawCalls = new();
        private GameplayTestSystemProbe.NumericMetric gcAllocatedBytes = new();
        private ProfilerRecorder drawCallsRecorder;
        private ProfilerRecorder gcAllocatedRecorder;
        private Scene scene;
        private GameObject root;
        private Camera benchmarkCamera;
        private RenderTexture renderTexture;
        private Material sourceMaterial;
        private Shader rendererShader;
        private string fireSpriteName;
        private GameplayTestSystemProbe.FireMeshMetrics initialUploadMetrics;
        private GameplayTestSystemProbe.FireMeshMetrics finalUploadMetrics;
        private Bounds fireMeshBounds;
        private Bounds cameraViewBounds;
        private int actualMeshRendererCount;
        private int actualSortingGroupCount;
        private int expectedSortingLayerId;
        private int expectedWorldEffectSortingOrder;
        private int renderedCameraFrames;
        private int renderedCameraFramesAtSampleStart;
        private ulong lastFrameStartTimestamp;
        private bool hasFrameStartTimestamp;
        private bool frameTimingSupported;
        private bool pipelineCameraCallbackRegistered;
        private bool builtInCameraCallbackRegistered;
        private bool disposed;
        private string previewImagePath;
        private int previewImageBytes;

        internal void Build(Sprite fireSprite, Material fireMaterial, Shader depthMeshShader)
        {
            sourceMaterial = fireMaterial;
            rendererShader = depthMeshShader;
            fireSpriteName = fireSprite != null ? fireSprite.name : null;
            scene = SceneManager.CreateScene("FireRenderBenchmark_" + Guid.NewGuid().ToString("N"));
            int chunkCountPerAxis = Mathf.CeilToInt((float)GameplayTestSystemProbe.FireBatchProbeCellCount /
                GameplayTestSystemProbe.FireBatchProbeChunkSize);

            root = new GameObject("10000 World Fire Benchmark") { hideFlags = HideFlags.HideAndDontSave };
            root.layer = FireRenderBenchmarkLayer;
            SceneManager.MoveGameObjectToScene(root, scene);
            int sortingLayerId, sortingOrder;
            WorldRenderingManager manager = WorldRenderingManager.GetInstance();
            if (manager != null)
                manager.GetSortingKey(WorldSortingManager.WorldEffectCategory, out sortingLayerId, out sortingOrder);
            else
                WorldSortingManager.GetResourceSortingKey(WorldSortingManager.WorldEffectCategory,
                    out sortingLayerId, out sortingOrder);
            expectedSortingLayerId = sortingLayerId;
            expectedWorldEffectSortingOrder = sortingOrder;

            for (int chunkY = 0; chunkY < chunkCountPerAxis; chunkY++)
            for (int chunkX = 0; chunkX < chunkCountPerAxis; chunkX++)
            {
                var owner = new GameObject($"FireChunk_{chunkX}_{chunkY}") { layer = FireRenderBenchmarkLayer };
                owner.transform.SetParent(root.transform, false);
                owner.transform.localPosition = new Vector3(chunkX * GameplayTestSystemProbe.FireBatchProbeChunkSize,
                    chunkY * GameplayTestSystemProbe.FireBatchProbeChunkSize, 0f);
                owners.Add(owner.transform);
                renderers.Add(new ChunkDepthMeshRenderer(owner.transform, sortingLayerId, sortingOrder,
                    useParentSortingAnchor: true));
            }

            long started = Stopwatch.GetTimestamp();
            for (int y = 0; y < GameplayTestSystemProbe.FireBatchProbeCellCount; y++)
            for (int x = 0; x < GameplayTestSystemProbe.FireBatchProbeCellCount; x++)
            {
                int chunkX = x / GameplayTestSystemProbe.FireBatchProbeChunkSize;
                int chunkY = y / GameplayTestSystemProbe.FireBatchProbeChunkSize;
                GameplayTestSystemProbe.SubmitFireQuad(renderers[chunkY * chunkCountPerAxis + chunkX], x, y,
                    fireSprite, fireMaterial, Color.white);
            }
            buildCpuMilliseconds = GameplayTestSystemProbe.ElapsedMilliseconds(started);

            started = Stopwatch.GetTimestamp();
            GameplayTestSystemProbe.FlushDiagnosticMeshes(renderers);
            initialFlushCpuMilliseconds = GameplayTestSystemProbe.ElapsedMilliseconds(started);
            initialUploadMetrics = GameplayTestSystemProbe.FireMeshMetrics.Capture(renderers,
                sourceMaterial, rendererShader);
            actualMeshRendererCount = root.GetComponentsInChildren<MeshRenderer>(true)
                .Count(renderer => renderer.enabled);
            actualSortingGroupCount = root.GetComponentsInChildren<SortingGroup>(true).Length;
            fireMeshBounds = CaptureRenderBounds(root);

            var cameraRoot = new GameObject("Isolated 1080p Fire Benchmark Camera")
                { hideFlags = HideFlags.HideAndDontSave, layer = FireRenderBenchmarkLayer };
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            benchmarkCamera = cameraRoot.AddComponent<Camera>();
            benchmarkCamera.enabled = false;
            benchmarkCamera.orthographic = true;
            benchmarkCamera.orthographicSize = GameplayTestSystemProbe.FireBatchProbeCellCount * .5f + 2f;
            benchmarkCamera.aspect = (float)FireRenderBenchmarkWidth / FireRenderBenchmarkHeight;
            benchmarkCamera.cullingMask = 1 << FireRenderBenchmarkLayer;
            benchmarkCamera.nearClipPlane = .1f;
            benchmarkCamera.farClipPlane = 100f;
            benchmarkCamera.clearFlags = CameraClearFlags.SolidColor;
            benchmarkCamera.backgroundColor = Color.black;
            benchmarkCamera.allowHDR = false;
            benchmarkCamera.allowMSAA = false;
            benchmarkCamera.allowDynamicResolution = false;
            benchmarkCamera.transform.position = new Vector3(fireMeshBounds.center.x,
                fireMeshBounds.center.y, -20f);

            renderTexture = new RenderTexture(FireRenderBenchmarkWidth, FireRenderBenchmarkHeight, 24,
                RenderTextureFormat.ARGB32)
            {
                name = "WorldFire_1920x1080_Benchmark",
                antiAliasing = 1,
                filterMode = FilterMode.Point
            };
            if (!renderTexture.Create())
                throw new InvalidOperationException("Unity 未能创建火场 1920×1080 RenderTexture。");
            benchmarkCamera.targetTexture = renderTexture;
            cameraViewBounds = new Bounds(new Vector3(benchmarkCamera.transform.position.x,
                benchmarkCamera.transform.position.y, 0f), new Vector3(
                2f * benchmarkCamera.orthographicSize * benchmarkCamera.aspect,
                2f * benchmarkCamera.orthographicSize, 500f));
        }

        private double buildCpuMilliseconds;
        private double initialFlushCpuMilliseconds;

        internal void BeginCameraSampling()
        {
            Camera[] currentCameras = Camera.allCameras;
            for (int i = 0; i < currentCameras.Length; i++)
            {
                Camera camera = currentCameras[i];
                if (camera == null || camera == benchmarkCamera || !camera.enabled) continue;
                disabledCameras.Add(camera);
                camera.enabled = false;
            }

            if (GraphicsSettings.currentRenderPipeline != null)
            {
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
                pipelineCameraCallbackRegistered = true;
            }
            else
            {
                Camera.onPostRender += OnBuiltInCameraPostRender;
                builtInCameraCallbackRegistered = true;
            }

            frameTimingSupported = FrameTimingManager.IsFeatureEnabled();
            benchmarkCamera.enabled = true;
        }

        internal void StartMetrics(int frameCapacity)
        {
            renderedCameraFramesAtSampleStart = renderedCameraFrames;
            int capacity = Mathf.Clamp(frameCapacity + 4, 16, 604);
            drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render,
                "Draw Calls Count", capacity);
            gcAllocatedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory,
                "GC Allocated In Frame", capacity);
        }

        internal void SampleProfilerMetrics()
        {
            if (drawCallsRecorder.Valid && drawCallsRecorder.Count > 0 && drawCallsRecorder.LastValue >= 0)
                drawCalls.Add(drawCallsRecorder.LastValue);
            if (gcAllocatedRecorder.Valid && gcAllocatedRecorder.Count > 0 && gcAllocatedRecorder.LastValue >= 0)
                gcAllocatedBytes.Add(gcAllocatedRecorder.LastValue);
        }

        internal void SampleFrameTiming()
        {
            if (!frameTimingSupported) return;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, latestFrameTiming) == 0) return;
            FrameTiming timing = latestFrameTiming[0];
            if (hasFrameStartTimestamp && timing.frameStartTimestamp == lastFrameStartTimestamp) return;
            hasFrameStartTimestamp = true;
            lastFrameStartTimestamp = timing.frameStartTimestamp;
            cpuFrameMilliseconds.AddPositive(timing.cpuFrameTime);
            cpuMainThreadMilliseconds.AddPositive(timing.cpuMainThreadFrameTime);
            cpuRenderThreadMilliseconds.AddPositive(timing.cpuRenderThreadFrameTime);
            // GPU timer 为0时代表平台没有提供有效 GPU 样本。
            gpuFrameMilliseconds.AddPositive(timing.gpuFrameTime);
        }

        internal JObject CreateResult(int warmupFrames, int requestedSampleFrames)
        {
            finalUploadMetrics = GameplayTestSystemProbe.FireMeshMetrics.Capture(renderers,
                sourceMaterial, rendererShader);
            int cellCount = GameplayTestSystemProbe.FireBatchProbeCellCount *
                GameplayTestSystemProbe.FireBatchProbeCellCount;
            bool cameraContainsFullMeshBounds = ContainsXY(cameraViewBounds, fireMeshBounds);
            Bounds cellGridBounds = new(new Vector3(GameplayTestSystemProbe.FireBatchProbeCellCount * .5f,
                GameplayTestSystemProbe.FireBatchProbeCellCount * .5f, 0f),
                new Vector3(GameplayTestSystemProbe.FireBatchProbeCellCount,
                    GameplayTestSystemProbe.FireBatchProbeCellCount, 1f));
            bool cameraContains100By100Grid = ContainsXY(cameraViewBounds, cellGridBounds);
            bool noMeshUploadDuringRender = initialUploadMetrics.IndexBufferUploadCount ==
                    finalUploadMetrics.IndexBufferUploadCount &&
                initialUploadMetrics.VertexBufferUploadCount == finalUploadMetrics.VertexBufferUploadCount &&
                initialUploadMetrics.TopologyRebuildCount == finalUploadMetrics.TopologyRebuildCount &&
                initialUploadMetrics.IndexBufferUploadBytes == finalUploadMetrics.IndexBufferUploadBytes &&
                initialUploadMetrics.VertexBufferUploadBytes == finalUploadMetrics.VertexBufferUploadBytes;

            return new JObject
            {
                ["assetsReady"] = sourceMaterial != null && rendererShader != null,
                ["benchmarkScope"] = "isolated-scene-single-camera-render-texture",
                ["fireSpriteName"] = fireSpriteName,
                ["fireMaterialShader"] = sourceMaterial != null && sourceMaterial.shader != null
                    ? sourceMaterial.shader.name : null,
                ["depthMeshShaderName"] = rendererShader != null ? rendererShader.name : null,
                ["cellCount"] = cellCount,
                ["cellsPerChunk"] = GameplayTestSystemProbe.FireBatchProbeChunkSize,
                ["expectedChunkCount"] = Mathf.CeilToInt((float)GameplayTestSystemProbe.FireBatchProbeCellCount /
                    GameplayTestSystemProbe.FireBatchProbeChunkSize) *
                    Mathf.CeilToInt((float)GameplayTestSystemProbe.FireBatchProbeCellCount /
                        GameplayTestSystemProbe.FireBatchProbeChunkSize),
                ["expectedSortingLayerId"] = expectedSortingLayerId,
                ["expectedWorldEffectSortingOrder"] = expectedWorldEffectSortingOrder,
                ["expectedFireDepthSortingOrder"] = GameplayTestSystemProbe.FireBatchProbeSortingOrder,
                ["actualEnabledMeshRendererCount"] = actualMeshRendererCount,
                ["actualSortingGroupCount"] = actualSortingGroupCount,
                ["drawGroupCount"] = initialUploadMetrics.DrawGroupCount,
                ["drawVertexCount"] = initialUploadMetrics.DrawVertexCount,
                ["drawIndexCount"] = initialUploadMetrics.DrawIndexCount,
                ["renderTextureCreated"] = renderTexture != null && renderTexture.IsCreated(),
                ["renderTextureWidth"] = renderTexture != null ? renderTexture.width : 0,
                ["renderTextureHeight"] = renderTexture != null ? renderTexture.height : 0,
                ["cameraOrthographic"] = benchmarkCamera != null && benchmarkCamera.orthographic,
                ["cameraOrthographicSize"] = benchmarkCamera != null ? benchmarkCamera.orthographicSize : 0f,
                ["cameraAspect"] = benchmarkCamera != null ? benchmarkCamera.aspect : 0f,
                ["cameraContains100By100Grid"] = cameraContains100By100Grid,
                ["cameraContainsAllFireMeshBounds"] = cameraContainsFullMeshBounds,
                ["fireMeshBounds"] = BoundsToJson(fireMeshBounds),
                ["cameraViewBounds"] = BoundsToJson(cameraViewBounds),
                ["warmupFrames"] = warmupFrames,
                ["requestedSampleFrames"] = requestedSampleFrames,
                ["warmupCameraRenderFrames"] = renderedCameraFramesAtSampleStart,
                ["actualSampleCameraRenderFrames"] = renderedCameraFrames - renderedCameraFramesAtSampleStart,
                ["previewImagePath"] = previewImagePath,
                ["previewImageBytes"] = previewImageBytes,
                ["buildCpuMilliseconds"] = buildCpuMilliseconds,
                ["initialFlushCpuMilliseconds"] = initialFlushCpuMilliseconds,
                ["initialUploads"] = initialUploadMetrics.ToJson(actualMeshRendererCount, actualSortingGroupCount),
                ["finalUploads"] = finalUploadMetrics.ToJson(actualMeshRendererCount, actualSortingGroupCount),
                ["renderDeltaUploads"] = new JObject
                {
                    ["indexBufferUploadCount"] = finalUploadMetrics.IndexBufferUploadCount -
                        initialUploadMetrics.IndexBufferUploadCount,
                    ["indexBufferUploadBytes"] = finalUploadMetrics.IndexBufferUploadBytes -
                        initialUploadMetrics.IndexBufferUploadBytes,
                    ["vertexBufferUploadCount"] = finalUploadMetrics.VertexBufferUploadCount -
                        initialUploadMetrics.VertexBufferUploadCount,
                    ["vertexBufferUploadBytes"] = finalUploadMetrics.VertexBufferUploadBytes -
                        initialUploadMetrics.VertexBufferUploadBytes,
                    ["topologyRebuildCount"] = finalUploadMetrics.TopologyRebuildCount -
                        initialUploadMetrics.TopologyRebuildCount
                },
                ["noMeshUploadDuringRender"] = noMeshUploadDuringRender,
                ["uploadCounterScope"] = "UNITY_EDITOR counters at actual Mesh.SetIndexBufferData/SetVertexBufferData calls",
                ["frameTimingSupported"] = frameTimingSupported,
                ["cpuFrameMilliseconds"] = cpuFrameMilliseconds.ToJson(),
                ["cpuMainThreadMilliseconds"] = cpuMainThreadMilliseconds.ToJson(),
                ["cpuRenderThreadMilliseconds"] = cpuRenderThreadMilliseconds.ToJson(),
                ["gpuFrameMilliseconds"] = gpuFrameMilliseconds.ToJson(),
                ["drawCalls"] = drawCalls.ToJson(),
                ["gcAllocatedBytes"] = gcAllocatedBytes.ToJson(),
                ["drawCallsCounterName"] = "Render/Draw Calls Count",
                ["drawCallsCounterScope"] = "whole player frame with other enabled cameras temporarily disabled",
                ["gcCounterName"] = "Memory/GC Allocated In Frame"
            };
        }

        internal void CapturePreviewImage()
        {
            if (renderTexture == null || !renderTexture.IsCreated())
                throw new InvalidOperationException("火场基准没有可读回的 RenderTexture。");
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                throw new InvalidOperationException("无法定位火场基准的项目目录。");
            string folder = Path.Combine(projectRoot, "Library", "FlatWorldGameplayTests",
                "WorldFireRenderBenchmark");
            Directory.CreateDirectory(folder);
            previewImagePath = Path.GetFullPath(Path.Combine(folder,
                "WorldFireRenderBenchmark_" + Guid.NewGuid().ToString("N") + ".png"));

            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(renderTexture.width, renderTexture.height,
                TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = renderTexture;
                readback.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0, false);
                readback.Apply(false, false);
                byte[] png = readback.EncodeToPNG();
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("火场基准 RT 没有产生 PNG 数据。");
                File.WriteAllBytes(previewImagePath, png);
                previewImageBytes = png.Length;
            }
            finally
            {
                RenderTexture.active = previous;
                DestroyOwnedObject(readback);
            }
        }

        private static Bounds CaptureRenderBounds(GameObject target)
        {
            Renderer[] children = target.GetComponentsInChildren<Renderer>(true);
            if (children.Length == 0) throw new InvalidOperationException("火场基准未创建实际 Renderer。");
            Bounds bounds = children[0].bounds;
            for (int i = 1; i < children.Length; i++) bounds.Encapsulate(children[i].bounds);
            return bounds;
        }

        private static bool ContainsXY(Bounds container, Bounds content)
            => content.min.x >= container.min.x && content.max.x <= container.max.x &&
               content.min.y >= container.min.y && content.max.y <= container.max.y;

        private static JObject BoundsToJson(Bounds bounds)
            => new()
            {
                ["minX"] = bounds.min.x, ["minY"] = bounds.min.y,
                ["maxX"] = bounds.max.x, ["maxY"] = bounds.max.y
            };

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == benchmarkCamera) renderedCameraFrames++;
        }

        private void OnBuiltInCameraPostRender(Camera camera)
        {
            if (camera == benchmarkCamera) renderedCameraFrames++;
        }

        internal void StopCameraSampling()
        {
            if (pipelineCameraCallbackRegistered)
            {
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
                pipelineCameraCallbackRegistered = false;
            }
            if (builtInCameraCallbackRegistered)
            {
                Camera.onPostRender -= OnBuiltInCameraPostRender;
                builtInCameraCallbackRegistered = false;
            }
            if (benchmarkCamera != null) benchmarkCamera.enabled = false;
            for (int i = disabledCameras.Count - 1; i >= 0; i--)
                if (disabledCameras[i] != null) disabledCameras[i].enabled = true;
            disabledCameras.Clear();
        }

        internal void StopMetrics()
        {
            if (drawCallsRecorder.Valid) drawCallsRecorder.Dispose();
            if (gcAllocatedRecorder.Valid) gcAllocatedRecorder.Dispose();
            drawCallsRecorder = default;
            gcAllocatedRecorder = default;
        }

        internal IEnumerator CleanupAsync()
        {
            Scene ownedScene = scene;
            Dispose();
            if (!ownedScene.IsValid() || !ownedScene.isLoaded) yield break;
            if (Application.isPlaying)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(ownedScene);
                while (unload != null && !unload.isDone) yield return null;
            }
            else
            {
                EditorSceneManager.CloseScene(ownedScene, true);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopCameraSampling();
            StopMetrics();
            foreach (ChunkDepthMeshRenderer renderer in renderers) renderer.Dispose();
            renderers.Clear();
            owners.Clear();
            if (benchmarkCamera != null)
            {
                benchmarkCamera.targetTexture = null;
                DestroyOwnedObject(benchmarkCamera.gameObject);
            }
            if (root != null) DestroyOwnedObject(root);
            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyOwnedObject(renderTexture);
            }
        }

        private static void DestroyOwnedObject(Object value)
        {
            if (value == null) return;
            if (value is GameObject gameObject) gameObject.SetActive(false);
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }

    private sealed class NumericMetric
    {
        private int count;
        private double sum;
        private double maximum;

        internal void Add(long value) => Add((double)value);
        internal void AddPositive(double value)
        {
            if (value > 0d) Add(value);
        }
        private void Add(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d) return;
            count++;
            sum += value;
            if (value > maximum) maximum = value;
        }

        internal JObject ToJson()
            => new()
            {
                ["status"] = count > 0 ? "available" : "unavailable",
                ["sampleCount"] = count,
                ["average"] = count > 0 ? sum / count : 0d,
                ["maximum"] = count > 0 ? maximum : 0d
            };
    }

    #endregion
}
#endif
