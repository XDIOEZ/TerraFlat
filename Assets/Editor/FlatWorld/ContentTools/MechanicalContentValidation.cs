using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>机械首版的显式资源验收：全项目内容校验加真实 Prefab 隔离渲染；不进入玩家世界或读写存档。</summary>
public static class MechanicalContentValidation
{
    #region 内容与视图验证
    [MenuItem("FlatWorld/机械扭矩/验证内容与面板")]
    public static void ValidateAndRender()
    {
        MechanicalContentBuilder.Build();
        MechanicalContentBuilder.ValidateAssets();
        var report = FlatWorldContentValidator.ValidateAll(FlatWorldContentValidationMode.Manual, false);
        Directory.CreateDirectory("Library/MechanicalValidation");
        File.WriteAllText("Library/MechanicalValidation/content.json", JsonConvert.SerializeObject(new
        {
            report.ErrorCount, report.WarningCount,
            Issues = report.Issues.Select(issue => new { issue.ErrorId, issue.AssetPath, issue.FieldName, issue.Message })
        }, Formatting.Indented));
        foreach (string id in new[] { "UI_HandDrill", "UI_Mechanical" })
        {
            RenderPanel(id, true);
            if (id == "UI_Mechanical") RenderPanel(id, false);
        }
        if (report.HasErrors) throw new InvalidOperationException("内容校验失败，见 Library/MechanicalValidation/content.json");
        Debug.Log($"[Mechanical] 内容校验通过：{report.ErrorCount} 错误，{report.WarningCount} 警告；已生成三张真实面板预览。");
    }

    private static void RenderPanel(string id, bool processing)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("MechanicalPreview", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(960, 640);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/" + id + ".prefab");
            var panel = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            panel.transform.SetParent(root.transform, false);
            panel.SetActive(true);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, .5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.localScale = Vector3.one;
            foreach (var group in panel.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1;
            var view = panel.GetComponent<MechanicalPanelView>();
            view.SetProcessingVisible(processing);
            view.Title.text = id == "UI_HandDrill" ? "手钻" : processing ? "锯木机" : "手摇轮";
            view.Status.text = id == "UI_HandDrill" ? "加工进度 50%" : "运行中 · 转速 60 · 扭矩 24/12";
            view.ActionButton.gameObject.SetActive(id == "UI_HandDrill" || !processing);
            view.ActionButton.GetComponentInChildren<TMPro.TMP_Text>(true).text = id == "UI_HandDrill" ? "钻孔" : "摇动";
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            var cameraObject = new GameObject("MechanicalPreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.orthographic = true;
            camera.farClipPlane = 2000;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(32, 32, 32, 255);
            canvas.worldCamera = camera;
            Render(camera, "Library/MechanicalValidation/" + id + (processing ? "_processing" : "_source") + ".png");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void Render(Camera camera, string path)
    {
        var target = new RenderTexture(960, 640, 24);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.orthographicSize = 320;
            camera.aspect = 1.5f;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(960, 640, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }
    #endregion
}
