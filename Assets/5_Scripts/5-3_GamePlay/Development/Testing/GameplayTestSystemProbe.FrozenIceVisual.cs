#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class GameplayTestSystemProbe
{
    #region 冰面隔离画面与性能采样

    // 用隔离预览相机比较真实底床、半透明冰和没有冻结来源的原冰。
    private static JObject ServiceFrozenIceVisual(JObject args)
    {
        using var fixture = new FrozenIceVisualFixture();
        fixture.Build();
        fixture.Render();
        JObject image = fixture.CapturePreview();
        fixture.ShowAllChunks();
        fixture.SetIceVisible(false);
        double baseline = fixture.MeasureRenderSubmissionMilliseconds();
        fixture.SetIceVisible(true);
        double ice = fixture.MeasureRenderSubmissionMilliseconds();
        return new JObject
        {
            ["preview"] = image, ["chunkCount"] = fixture.ChunkCount,
            ["cellCount"] = fixture.ChunkCount * 16 * 16, ["iceGroupCount"] = fixture.IceGroupCount,
            ["bindMilliseconds"] = fixture.BindMilliseconds,
            ["baselineRenderMilliseconds"] = baseline, ["iceRenderMilliseconds"] = ice,
            ["renderSamples"] = FrozenIceVisualFixture.RenderSamples,
            ["metricScope"] = "editor-camera-render-submission"
        };
    }

    private sealed class FrozenIceVisualFixture : IDisposable
    {
        private const int Layer = 31;
        private const int ChunkSize = 16;
        private const int ChunksPerAxis = 4;
        private const int Width = 1280;
        private const int Height = 720;
        private const int Origin = 10000;
        internal const int RenderSamples = 16;
        private readonly List<ChunkTilemapRenderer> renderers = new();
        private readonly List<GameObject> chunkRoots = new();
        private readonly List<GameObject> iceRoots = new();
        private Scene scene;
        private WorldRuntime world;
        private GameObject root;
        private GameObject lightRoot;
        private Camera camera;
        private RenderTexture target;
        private bool disposed;
        internal int ChunkCount => chunkRoots.Count;
        internal int IceGroupCount => iceRoots.Count;
        internal double BindMilliseconds { get; private set; }

        internal void Build()
        {
            const string prefabPath = "Assets/2_Prefabs/World/WorldModel/ChunkView.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var prefabRenderer = prefab != null ? prefab.GetComponent<ChunkTilemapRenderer>() : null;
            var serialized = prefabRenderer != null ? new SerializedObject(prefabRenderer) : null;
            var palette = serialized?.FindProperty("palette")?.objectReferenceValue as ChunkTilePaletteSO;
            var material = serialized?.FindProperty("groundMaterial")?.objectReferenceValue as Material;
            if (palette == null || material == null)
                throw new InvalidOperationException("冰面现场缺少 ChunkView 的正式地块目录或材质。");

            scene = CreateGameplayTestProbeScene("FrozenIceVisual");
            root = new GameObject("Frozen ice visual chunks") { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = new Vector3(Origin, Origin, 0f);
            world = new WorldRuntime("probe:frozen-ice-visual", 1);
            var profile = new ChunkGenerationProfileSnapshot("probe:frozen-ice-visual", 1, ChunkSize, ChunkSize);
            var chunks = new List<ChunkRuntime>();
            for (int cy = 0; cy < ChunksPerAxis; cy++)
            for (int cx = 0; cx < ChunksPerAxis; cx++)
            {
                var origin = new Int2(Origin + cx * ChunkSize, Origin + cy * ChunkSize);
                var address = new FlatWorld.WorldModel.WorldAddress("probe:frozen-ice-visual", origin);
                ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
                using var buffer = new ChunkTerrainBuffer(ChunkSize, ChunkSize);
                for (int y = 0; y < ChunkSize; y++)
                for (int x = 0; x < ChunkSize; x++)
                {
                    int bedId = y < 5 ? 3 : y < 10 ? 4 : 1;
                    int biome = y < 5 ? (int)SurfaceBiomeKind.Ocean :
                        y < 10 ? (int)SurfaceBiomeKind.River : (int)SurfaceBiomeKind.Grassland;
                    bool showcase = cx == 0 && cy == 0;
                    bool frozen = !showcase || (x >= 5 && x < 10);
                    int ground = showcase && x < 5 ? bedId : 9;
                    var bed = new TerrainCell(bedId, 0, 0, biome, 3, TerrainCellFlags.Walkable);
                    buffer.SetCell(x, y, new TerrainCell(ground, 0, 0, biome, 4, TerrainCellFlags.Walkable));
                    if (frozen)
                    {
                        string liquid = y < 5 ? LiquidTypeCatalog.SeaWaterId : LiquidTypeCatalog.DirtyWaterId;
                        FrozenSurfaceWaterLayer.WriteGenerationSource(buffer, y * ChunkSize + x,
                            new FrozenSurfaceWaterSource(bed, 9, buffer.LiquidTypes.GetIndex(liquid), 80));
                    }
                }
                using var result = new ChunkGenerationResult(request, buffer);
                if (!world.TryCommit(result, out string reason)) throw new InvalidOperationException(reason);
                if (!world.TryGetChunk(address, out ChunkRuntime chunk))
                    throw new InvalidOperationException("冰面现场区块未提交。");
                chunks.Add(chunk);
            }

            long start = Stopwatch.GetTimestamp();
            for (int index = 0; index < chunks.Count; index++)
            {
                var chunkRoot = new GameObject("IceChunk_" + index) { layer = Layer };
                chunkRoot.transform.SetParent(root.transform, false);
                chunkRoot.transform.localPosition = new Vector3(index % ChunksPerAxis * ChunkSize,
                    index / ChunksPerAxis * ChunkSize, 0f);
                chunkRoots.Add(chunkRoot);
                var renderer = chunkRoot.AddComponent<ChunkTilemapRenderer>();
                renderers.Add(renderer);
                renderer.ConfigureFrozenIcePresentationTests(palette, material);
                renderer.SetWorld(world);
                renderer.Bind(chunks[index]);
            }
            BindMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = Layer;
                if (child.name.StartsWith("IceMesh_", StringComparison.Ordinal)) iceRoots.Add(child.gameObject);
            }
            for (int i = 1; i < chunkRoots.Count; i++) chunkRoots[i].SetActive(false);
            root.SetActive(true);

            // 预览灯使用空闲乘法通道，避免与编辑器已有全局灯冲突。
            bool style0Used = false, style2Used = false;
            foreach (Light2D existing in Resources.FindObjectsOfTypeAll<Light2D>())
            {
                if (!existing.gameObject.scene.IsValid() || !existing.isActiveAndEnabled ||
                    existing.lightType != Light2D.LightType.Global) continue;
                style0Used |= existing.blendStyleIndex == 0;
                style2Used |= existing.blendStyleIndex == 2;
            }
            if (style0Used && style2Used)
                throw new InvalidOperationException("冰面预览没有空闲的乘法光照通道。");
            lightRoot = new GameObject("Frozen ice visual light") { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(lightRoot, scene);
            lightRoot.transform.position = new Vector3(Origin + 32f, Origin + 32f, -1f);
            var light = lightRoot.AddComponent<Light2D>();
            light.blendStyleIndex = style0Used ? 2 : 0;
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;

            var cameraRoot = new GameObject("Frozen ice visual camera") { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 9f;
            camera.aspect = (float)Width / Height;
            camera.scene = scene;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.transform.position = new Vector3(Origin + 8f, Origin + 8f, -20f);
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                { name = "FrozenIceVisual", antiAliasing = 1, filterMode = FilterMode.Point };
            if (!target.Create()) throw new InvalidOperationException("冰面现场无法创建渲染纹理。");
            camera.targetTexture = target;
        }

        internal void Render() => camera.Render();

        internal JObject CapturePreview()
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
                image.Apply(false, false);
                Color32[] pixels = image.GetPixels32();
                var rows = new JArray();
                for (int row = 0; row < 3; row++)
                {
                    int eligible = 0, blended = 0;
                    for (int y = 44 + row * 200; y < 236 + row * 200; y += 4)
                    for (int x = 324; x < 516; x += 4)
                    {
                        Color32 bed = pixels[y * Width + x];
                        Color32 ice = pixels[y * Width + x + 200];
                        Color32 opaque = pixels[y * Width + x + 400];
                        CountBlendedChannel(bed.r, ice.r, opaque.r, ref eligible, ref blended);
                        CountBlendedChannel(bed.g, ice.g, opaque.g, ref eligible, ref blended);
                        CountBlendedChannel(bed.b, ice.b, opaque.b, ref eligible, ref blended);
                    }
                    rows.Add(new JObject { ["eligible"] = eligible, ["blended"] = blended });
                }
                string runId = GameplayTestSelection.ResolveRunId(Guid.NewGuid().ToString("N"));
                string folder = Path.Combine(GameplayTestArtifacts.GetRunDirectory(runId),
                    "cases", "rendering_building_liquid_surface");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "frozen-ice-preview.png");
                byte[] png = image.EncodeToPNG();
                File.WriteAllBytes(path, png);
                return new JObject { ["path"] = path, ["bytes"] = png.Length, ["rows"] = rows };
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
            }
        }

        private static void CountBlendedChannel(byte bed, byte ice, byte opaque, ref int eligible, ref int blended)
        {
            int difference = opaque - bed;
            if (Math.Abs(difference) < 20) return;
            eligible++;
            float fraction = (ice - bed) / (float)difference;
            if (fraction > .15f && fraction < .85f) blended++;
        }

        internal void ShowAllChunks()
        {
            foreach (GameObject chunk in chunkRoots) chunk.SetActive(true);
            camera.transform.position = new Vector3(Origin + 32f, Origin + 32f, -20f);
            camera.orthographicSize = 34f;
        }

        internal void SetIceVisible(bool visible)
        {
            foreach (GameObject ice in iceRoots) ice.SetActive(visible);
        }

        internal double MeasureRenderSubmissionMilliseconds()
        {
            for (int i = 0; i < 4; i++) camera.Render();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < RenderSamples; i++) camera.Render();
            return (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / RenderSamples;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (ChunkTilemapRenderer renderer in renderers)
                if (renderer != null) { renderer.Unbind(); renderer.SetWorld(null); }
            world?.Dispose();
            if (camera != null) { camera.targetTexture = null; Object.DestroyImmediate(camera.gameObject); }
            if (lightRoot != null) Object.DestroyImmediate(lightRoot);
            if (root != null) Object.DestroyImmediate(root);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (scene.IsValid()) CloseGameplayTestProbeScene(scene);
        }
    }
    #endregion
}
#endif
