using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>在独立预览场景渲染陶罐水质与水位，不启动游戏或修改用户场景。</summary>
public static class WaterVesselUIPreview
{
    [MenuItem("FlatWorld/UI/Preview Water Vessel UI")]
    public static void Render()
    {
        string[] ids = { "dirty", "drinkable", "sea" };
        string[] names = { "脏水", "淡水", "海水" };
        for (int i = 0; i < ids.Length; i++) RenderOne(ids[i], names[i], i);
    }
    private static void RenderOne(string id, string name, int index)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var target = new RenderTexture(680, 820, 24);
        var previous = RenderTexture.active;
        Camera camera = null;
        try
        {
            var root = new GameObject("预览", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(680, 820);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2_Prefabs/2-1_UI/Gameplay/Containers/UI_WaterVessel.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            instance.transform.SetParent(root.transform, false);
            instance.transform.localScale = Vector3.one;
            instance.GetComponent<WaterVesselPanel>().Liquid.SetWater(4, 8, id, true);
            foreach (TMP_Text text in instance.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == "水量状态") text.text = name + "　4 / 8 份\n加热进度：0 秒";
            var cameraObject = new GameObject("预览相机", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>();
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.orthographic = true; camera.orthographicSize = 410; camera.farClipPlane = 2000;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(32,32,32,255);
            camera.targetTexture = target; canvas.worldCamera = camera;
            Canvas.ForceUpdateCanvases();
            instance.GetComponent<WaterVesselPanel>().Liquid.Rebuild(CanvasUpdate.PreRender);
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(680,820,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,680,820),0,0); image.Apply();
            File.WriteAllBytes("Library/WaterVesselPreview_" + index + ".png",image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active = previous;
            if (camera != null) camera.targetTexture = null;
            target.Release(); Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
