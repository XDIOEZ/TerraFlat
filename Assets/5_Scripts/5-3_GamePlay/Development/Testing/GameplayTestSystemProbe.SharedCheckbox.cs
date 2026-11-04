#if UNITY_EDITOR
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 共享复选框资产采样

    [InitializeOnLoadMethod]
    private static void RegisterSharedCheckboxRules() =>
        Register("ui_settings", "shared_checkbox_prefabs", CaptureSharedCheckboxPrefabs);

    private static JObject CaptureSharedCheckboxPrefabs(JObject args)
    {
        const string rootPath = "Assets/2_Prefabs/2-1_UI/";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(rootPath + "Common/Controls/UI_Toggle.prefab");
        var sourceToggle = source.GetComponent<Toggle>();
        var unshared = new JArray();
        int checkboxCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { rootPath }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (Toggle toggle in asset.GetComponentsInChildren<Toggle>(true))
            {
                if (toggle.GetComponentInParent<TMPro.TMP_Dropdown>(true) != null)
                    continue;
                checkboxCount++;
                if (toggle != sourceToggle && PrefabUtility.GetCorrespondingObjectFromOriginalSource(toggle) != sourceToggle)
                    unshared.Add(path + "/" + toggle.name);
            }
        }

        var scene = CreateGameplayTestProbeScene("SharedCheckboxRules");
        var instance = Object.Instantiate(source);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
        try
        {
            // 采样正式 Prefab 的伸缩几何和原生 Toggle 事件，不复制绘制规则。
            var toggle = instance.GetComponent<Toggle>();
            var samples = new JArray();
            foreach (Vector2 size in new[] { new Vector2(76f, 60f), new Vector2(120f, 84f), new Vector2(30f, 30f) })
            {
                ((RectTransform)instance.transform).sizeDelta = size;
                Rect box = toggle.targetGraphic.rectTransform.rect;
                Rect fill = toggle.graphic.rectTransform.rect;
                samples.Add(new JObject { ["width"] = box.width, ["height"] = box.height,
                    ["fillWidth"] = fill.width, ["fillHeight"] = fill.height });
            }
            int events = 0;
            toggle.onValueChanged.AddListener(_ => events++);
            toggle.SetIsOnWithoutNotify(false);
            float offAlpha = toggle.graphic.canvasRenderer.GetAlpha();
            toggle.SetIsOnWithoutNotify(true);
            float onAlpha = toggle.graphic.canvasRenderer.GetAlpha();
            int silentEvents = events;
            toggle.isOn = false;
            return new JObject { ["checkboxCount"] = checkboxCount, ["unshared"] = unshared,
                ["samples"] = samples, ["white"] = toggle.graphic.color == Color.white,
                ["rootHit"] = instance.GetComponent<Image>().raycastTarget,
                ["fillHit"] = toggle.graphic.raycastTarget, ["offAlpha"] = offAlpha,
                ["onAlpha"] = onAlpha, ["silentEvents"] = silentEvents, ["changeEvents"] = events };
        }
        finally
        {
            Object.DestroyImmediate(instance);
            CloseGameplayTestProbeScene(scene);
        }
    }

    #endregion
}
#endif
