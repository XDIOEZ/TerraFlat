#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>采样正式器物资源和生产渲染方法，Lua 判断覆盖与动态状态。</summary>
public static class GameplayTestPhysicalDeviceArtworkRules
{
    #region 器物外观规则采样
    [InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() => GameplayTestSystemProbe.Register("ui_settings", "physical_device_artwork", Capture);

    public static string CaptureEditorPreviewRules() => Capture(null).ToString();

    private static JObject Capture(JObject args)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<PhysicalDeviceArtworkCatalog>("Assets/Resources/UI/PhysicalDeviceArtworkCatalog.asset");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int duplicateIds = 0, oversized = 0, missingSprites = 0;
        bool handheldPartsMatch = true;
        foreach (var appearance in catalog.Appearances)
        {
            foreach (string id in appearance.ItemIds) if (!ids.Add(id)) duplicateIds++;
            string firstId = appearance.ItemIds[0];
            if (firstId.EndsWith("_Summoner", StringComparison.Ordinal))
            {
                var parent = catalog.Find(firstId.Substring(0, firstId.Length - "_Summoner".Length));
                if (parent != null) handheldPartsMatch &= appearance.Layers.Length == parent.Layers.Length &&
                    appearance.Layers.Select(layer => layer.Movement).SequenceEqual(parent.Layers.Select(layer => layer.Movement));
            }
            if (appearance.Layers.Length > PhysicalDeviceArtworkCatalog.LayerCapacity) oversized++;
            foreach (var layer in appearance.Layers)
                if (layer.Sprite == null && layer.Movement is PhysicalDeviceArtworkCatalog.Motion.Still or
                    PhysicalDeviceArtworkCatalog.Motion.Rotate or PhysicalDeviceArtworkCatalog.Motion.Drill) missingSprites++;
        }
        var panels = new JArray();
        foreach (var layout in catalog.Panels)
        {
            string path = AssetDatabase.FindAssets(layout.PanelId + " t:Prefab", new[] { "Assets/2_Prefabs/2-1_UI/Gameplay" })
                .Select(AssetDatabase.GUIDToAssetPath).Single(value => System.IO.Path.GetFileNameWithoutExtension(value) == layout.PanelId);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var view = prefab.GetComponentInChildren<PhysicalDeviceArtworkView>(true);
            var fields = new SerializedObject(view);
            var layerFields = fields.FindProperty("layers");
            bool passThrough = true;
            for (int i = 0; i < layerFields.arraySize; i++)
                passThrough &= layerFields.GetArrayElementAtIndex(i).objectReferenceValue is Image image && !image.raycastTarget;
            panels.Add(new JObject
            {
                ["id"] = layout.PanelId,
                ["appearance"] = catalog.Find(layout.DefaultAppearance) != null,
                ["pool"] = layerFields.arraySize,
                ["passThrough"] = passThrough,
                ["safeArea"] = prefab.GetComponent<SafeAreaScaleGroup>() != null
            });
        }

        string drillPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_FireDrill.prefab";
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(drillPath), scene);
        try
        {
            var view = root.GetComponentInChildren<PhysicalDeviceArtworkView>(true);
            var cold = new PhysicalDeviceArtworkView.OperatingState();
            view.PreviewArtwork(cold, .03f, true);
            Vector3 bowFirst = view.transform.Find("ArtworkLayer_3").localPosition;
            view.PreviewArtwork(cold, .13f, true);
            bool moving = bowFirst != view.transform.Find("ArtworkLayer_3").localPosition;
            var hot = new PhysicalDeviceArtworkView.OperatingState { Heat = .9f };
            view.PreviewArtwork(hot, .2f);
            float smoke = view.transform.Find("ArtworkLayer_6").GetComponent<Image>().color.a;
            float prematureEmber = view.transform.Find("ArtworkLayer_10").GetComponent<Image>().color.a;
            hot.FireSeed = true;
            view.PreviewArtwork(hot, .2f);
            float actualEmber = view.transform.Find("ArtworkLayer_10").GetComponent<Image>().color.a;
            view.ResetTransientPresentation();
            float clearedSmoke = view.transform.Find("ArtworkLayer_6").GetComponent<Image>().color.a;
            float clearedEmber = view.transform.Find("ArtworkLayer_10").GetComponent<Image>().color.a;
            var contents = new PhysicalDeviceArtworkView.OperatingState();
            int plantIndex = Array.FindIndex(catalog.Find("SpaceCultivator").Layers,
                layer => layer.Movement == PhysicalDeviceArtworkCatalog.Motion.Contents0);
            Sprite plant = catalog.Find("SpaceCultivator").Layers[plantIndex].Sprite;
            contents.SetContents(0, plant, .5f);
            view.PreviewArtwork(contents, 0f, appearanceId: "SpaceCultivator");
            Image firstPlant = view.transform.Find("ArtworkLayer_" + plantIndex).GetComponent<Image>();
            bool realContents = firstPlant.sprite == plant && firstPlant.color.a > 0f && firstPlant.transform.localScale.x == .5f;
            view.PreviewArtwork(default, 0f, appearanceId: "SpaceCultivator");
            bool emptyContents = firstPlant.color.a == 0f;
            return new JObject
            {
                ["panels"] = panels, ["appearances"] = catalog.Appearances.Length,
                ["duplicateIds"] = duplicateIds, ["oversized"] = oversized, ["missingSprites"] = missingSprites,
                ["bowMoves"] = moving, ["hotSmoke"] = smoke, ["prematureEmber"] = prematureEmber,
                ["actualEmber"] = actualEmber, ["clearedSmoke"] = clearedSmoke, ["clearedEmber"] = clearedEmber,
                ["realContents"] = realContents, ["emptyContents"] = emptyContents, ["handheldPartsMatch"] = handheldPartsMatch
            };
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    #endregion
}
#endif
