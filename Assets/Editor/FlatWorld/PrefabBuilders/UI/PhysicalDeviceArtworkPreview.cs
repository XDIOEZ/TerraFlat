using System.IO;
using System.Linq;
using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>独立预览场景渲染正式器物面板，不切换用户场景或启动游戏。</summary>
public static class PhysicalDeviceArtworkPreview
{
    #region 全界面与阶段截图
    private const string Output = "Library/FlatWorldDeviceArtwork/Previews";

    public static string ReadConsoleSummary()
    {
        Type entries = Type.GetType("UnityEditor.LogEntries, UnityEditor.CoreModule");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        object[] counts = { 0, 0, 0 };
        entries.GetMethod("GetCountsByType", flags).Invoke(null, counts);
        int count = (int)entries.GetMethod("GetCount", flags).Invoke(null, null);
        Type entryType = Type.GetType("UnityEditor.LogEntry, UnityEditor.CoreModule");
        var recent = new JArray();
        entries.GetMethod("StartGettingEntries", flags).Invoke(null, null);
        try
        {
            for (int i = Mathf.Max(0, count - 30); i < count; i++)
            {
                object entry = Activator.CreateInstance(entryType);
                entries.GetMethod("GetEntryInternal", flags).Invoke(null, new[] { (object)i, entry });
                recent.Add(new JObject { ["message"] = (string)entryType.GetField("message", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry),
                    ["mode"] = Convert.ToInt32(entryType.GetField("mode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry)) });
            }
        }
        finally { entries.GetMethod("EndGettingEntries", flags).Invoke(null, null); }
        return new JObject { ["errors"] = (int)counts[0], ["warnings"] = (int)counts[1], ["logs"] = (int)counts[2], ["recent"] = recent }.ToString();
    }

    [MenuItem("FlatWorld/其他工具/UI/预览全部器物界面")]
    public static void RenderAll()
    {
        Directory.CreateDirectory(Output);
        var catalog = AssetDatabase.LoadAssetAtPath<PhysicalDeviceArtworkCatalog>(PhysicalDeviceArtworkPrefabBuilder.CatalogPath);
        foreach (var layout in catalog.Panels)
        {
            RenderOne(layout.PanelId, layout.DefaultAppearance, true);
            RenderOne(layout.PanelId, layout.DefaultAppearance, false);
            RenderOne(layout.PanelId, layout.DefaultAppearance, true, .04f, 1366, 768);
            RenderOne(layout.PanelId, layout.DefaultAppearance, true, .04f, 1080, 1920);
        }
        var fluidIds = JObject.Parse(File.ReadAllText("Assets/Resources/Config/Mechanical/mechanical-catalog.json"))["Nodes"]
            .Where(value => value["Fluid"] != null).Select(value => (string)value["Id"]).ToHashSet();
        foreach (var appearance in catalog.Appearances)
        {
            string id = appearance.ItemIds[0];
            if (catalog.Panels.Any(layout => layout.DefaultAppearance == id)) continue;
            string panel = id.StartsWith("HandDrill") ? "UI_HandDrill" : fluidIds.Contains(id) ? "UI_IndustrialFluid" : id.Contains("PortableGasTank") ? "UI_FluidTank" :
                id == "InductionStove" ? "UI_Furnace" : id.Contains("Anvil") ? "UI_Anvil" : "UI_Mechanical";
            RenderOne(panel, id, true);
        }
        RenderOne("UI_FireDrill", "SparkMaker", false);
        RenderOne("UI_FireDrill", "SparkMaker", true, .13f);
        RenderOne("UI_FireDrill", "SparkMaker", true, .2f, 1080, 1920);
        for (int i = 0; i < 16; i++)
        {
            RenderOne("UI_FireDrill", "SparkMaker", true, i * .03f, 1280, 720);
            RenderOne("UI_HandDrill", "HandDrill", true, i * .03f, 1280, 720);
        }
    }

    public static void RenderOne(string panelId, string appearanceId, bool working, float time = .04f, int width = 1920, int height = 1080)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var target = new RenderTexture(width, height, 24);
        RenderTexture previous = RenderTexture.active;
        Camera camera = null;
        try
        {
            var root = new GameObject("器物预览画布", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);
            string path = AssetDatabase.FindAssets(panelId + " t:Prefab", new[] { "Assets/2_Prefabs/2-1_UI/Gameplay" })
                .Select(AssetDatabase.GUIDToAssetPath).Single(value => Path.GetFileNameWithoutExtension(value) == panelId);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
            instance.transform.SetParent(root.transform, false);
            var panelRect = (RectTransform)instance.transform;
            panelRect.anchoredPosition = Vector2.zero;
            instance.transform.localScale = Vector3.one;
            foreach (CanvasGroup group in instance.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1f;
            foreach (TMP_Text text in instance.GetComponentsInChildren<TMP_Text>(true))
            {
                var binder = text.GetComponent<FlatWorld.Localization.LocalizedTextBinder>();
                if (binder != null) binder.enabled = false;
            }
            var artwork = instance.GetComponentInChildren<PhysicalDeviceArtworkView>(true);
            var snapshot = working ? new PhysicalDeviceArtworkView.OperatingState
            {
                SignedRpm = 20, LinearSpeed = 1, Heat = .9f, Liquid = .6f, Gas = .7f, Charge = .65f,
                Fuel = .7f, Progress = .6f, Working = true, Burning = true, Open = true, FireSeed = appearanceId == "SparkMaker"
            } : default;
            if (working)
                foreach (var layer in AssetDatabase.LoadAssetAtPath<PhysicalDeviceArtworkCatalog>(PhysicalDeviceArtworkPrefabBuilder.CatalogPath).Find(appearanceId).Layers)
                {
                    if (layer.Movement is PhysicalDeviceArtworkCatalog.Motion.InputAttachment or PhysicalDeviceArtworkCatalog.Motion.ToolStroke)
                        snapshot.InputAttachment = layer.Sprite;
                    if (layer.Movement >= PhysicalDeviceArtworkCatalog.Motion.Contents0 && layer.Movement <= PhysicalDeviceArtworkCatalog.Motion.Contents3)
                        snapshot.SetContents(layer.Movement - PhysicalDeviceArtworkCatalog.Motion.Contents0, layer.Sprite);
                }
            artwork.PreviewArtwork(snapshot, time, working, appearanceId);
            Canvas.ForceUpdateCanvases();
            instance.GetComponent<SafeAreaScaleGroup>()?.ApplyScale();
            var cameraObject = new GameObject("器物预览相机", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>();
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(0, 0, -2000);
            camera.orthographic = true; camera.orthographicSize = height * .5f; camera.farClipPlane = 4000;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(29, 32, 31, 255);
            camera.targetTexture = target; canvas.worldCamera = camera;
            Canvas.ForceUpdateCanvases();
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                Directory.CreateDirectory(Output);
                File.WriteAllBytes($"{Output}/{panelId}_{appearanceId}_{(working ? "working" : "cold")}_{time:0.00}_{width}x{height}.png", image.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(image); }
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/" + panelId + ".error.txt", exception.ToString());
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            if (camera != null) camera.targetTexture = null;
            target.Release(); Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    #endregion
}
