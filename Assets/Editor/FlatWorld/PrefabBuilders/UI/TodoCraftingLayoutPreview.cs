using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 在隔离预览场景中验证两个制作面板的可用空间适配，并输出实际 Prefab 的渲染图。
/// 不启动游戏、不读写玩家存档、不改根 CanvasScaler，也不修改原 Prefab。
/// 同一个实例依次缩小、扩大，检查安全边界与非累乘缩放。
/// </summary>
public static class TodoCraftingLayoutPreview
{
    #region 验证定义

    private const string OutputDirectory = "Library/FlatWorldTodoValidation";
    private static readonly string[] PanelIds = { "UI_HandCraftTable", "UI_MakerTable" };
    private static readonly Vector2Int[] AvailableSizes =
    {
        new Vector2Int(1920, 1080), new Vector2Int(640, 360),
        new Vector2Int(1344, 756), new Vector2Int(360, 640), new Vector2Int(960, 540)
    };

    [Serializable]
    private sealed class PreviewCase
    {
        public string panel;
        public int width;
        public int height;
        public float scale;
        public string image;
    }

    [Serializable]
    private sealed class PreviewReport
    {
        public bool passed;
        public string note = "尺寸为面板可用 UI 坐标空间，不模拟设备 CanvasScaler 或系统安全区。";
        public List<PreviewCase> cases = new List<PreviewCase>();
    }

    #endregion

    #region 显式验证入口

    [MenuItem("FlatWorld/诊断/待办/制作面板多尺寸预览与边界检查")]
    public static void ValidateAndRender()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("制作面板隔离预览请在非播放模式执行。");

        Directory.CreateDirectory(OutputDirectory);
        var report = new PreviewReport();
        try
        {
            foreach (string id in PanelIds)
                ValidatePanel(id, report);
            report.passed = true;
            Debug.Log($"[TodoCraftingLayoutPreview] PASS {report.cases.Count} 个制作面板尺寸；图片与报告：{OutputDirectory}");
        }
        finally
        {
            File.WriteAllText(Path.Combine(OutputDirectory, "crafting-layout.json"),
                JsonUtility.ToJson(report, true));
        }
    }

    private static void ValidatePanel(string id, PreviewReport report)
    {
        string path = $"Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/{id}.prefab";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Require(asset != null, $"缺少制作面板：{path}");
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("制作面板验证画布", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)root.transform;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            instance.transform.SetParent(root.transform, false);
            instance.SetActive(true);
            var panel = instance.GetComponent<RectTransform>();
            Require(panel != null && panel.anchorMin == Vector2.zero && panel.anchorMax == Vector2.one,
                $"{id} 根节点不是全屏 Stretch。");
            Require(panel.offsetMin.sqrMagnitude < 0.001f && panel.offsetMax.sqrMagnitude < 0.001f,
                $"{id} 全屏外层仍带有绝对偏移。");

            SafeAreaScaleGroup[] groups = instance.GetComponentsInChildren<SafeAreaScaleGroup>(true);
            Require(groups.Length == 1, $"{id} 应仅有一个面板级 SafeAreaScaleGroup，实际 {groups.Length}。");
            var serialized = new SerializedObject(groups[0]);
            var bounds = serialized.FindProperty("boundsTarget").objectReferenceValue as RectTransform;
            Vector2 margin = serialized.FindProperty("safeMargin").vector2Value;
            Require(bounds != null, $"{id} 缺少固定内容边界引用。");
            Require(Mathf.Abs(bounds.sizeDelta.x - 1344f) < 0.01f &&
                    Mathf.Abs(bounds.sizeDelta.y - 756f) < 0.01f,
                $"{id} 内容层不再是 1344×756。");

            var cameraObject = new GameObject("制作面板验证相机", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(0f, 0f, -1000f);
            camera.orthographic = true;
            camera.farClipPlane = 2000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(32, 32, 32, 255);
            canvas.worldCamera = camera;

            foreach (Vector2Int size in AvailableSizes)
            {
                canvasRect.sizeDelta = size;
                LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
                Canvas.ForceUpdateCanvases();
                groups[0].ApplyScale();
                Canvas.ForceUpdateCanvases();
                float expected = Mathf.Min(1f,
                    (size.x - margin.x * 2f) / 1344f, (size.y - margin.y * 2f) / 756f);
                Require(Mathf.Abs(bounds.localScale.x - expected) < 0.001f &&
                        Mathf.Abs(bounds.localScale.y - expected) < 0.001f,
                    $"{id} {size} 缩放错误：{bounds.localScale}，预期 {expected}。");
                Require(panel.localScale == Vector3.one, $"{id} 不应缩放全屏外层。");

                var corners = new Vector3[4];
                bounds.GetWorldCorners(corners);
                foreach (Vector3 worldCorner in corners)
                {
                    Vector3 corner = canvasRect.InverseTransformPoint(worldCorner);
                    Require(Mathf.Abs(corner.x) <= size.x * 0.5f - margin.x + 0.1f &&
                            Mathf.Abs(corner.y) <= size.y * 0.5f - margin.y + 0.1f,
                        $"{id} {size} 内容越出可用边界：{corner}。");
                }

                string image = Path.Combine(OutputDirectory, $"{id}_{size.x}x{size.y}.png");
                Render(camera, size, image);
                report.cases.Add(new PreviewCase
                {
                    panel = id, width = size.x, height = size.y,
                    scale = bounds.localScale.x, image = image
                });
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    #endregion

    #region 预览渲染与断言

    private static void Render(Camera camera, Vector2Int size, string path)
    {
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(size.x, size.y, 24);
        Texture2D image = null;
        try
        {
            camera.orthographicSize = size.y * 0.5f;
            camera.aspect = (float)size.x / size.y;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, size.x, size.y), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    #endregion
}
