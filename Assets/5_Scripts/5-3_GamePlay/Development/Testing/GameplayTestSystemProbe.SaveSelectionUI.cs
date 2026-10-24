#if UNITY_EDITOR
using System;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 存档正式面板控件采样

    [InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSaveSelectionUiRules() =>
        Register("ui_settings", "save_selection_controls", CaptureSaveSelectionControls);

    private static JObject CaptureSaveSelectionControls(JObject args)
    {
        // 只读正式 Prefab 的身份和布局，不创建单例或操作玩家存档。
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/MainMenu/Save/UI_SaveSelectionPanel.prefab");
        if (prefab == null)
            throw new InvalidOperationException("存档选择正式面板缺失。");

        var buttonNames = new JArray();
        Button renameButton = null;
        Button loadButton = null;
        foreach (Button button in prefab.GetComponentsInChildren<Button>(true))
        {
            buttonNames.Add(button.name);
            if (button.name == GameManager.GameSaveRenameButtonKey)
                renameButton = button;
            if (button.name == GameManager.GameSaveLoadButtonKey)
                loadButton = button;
        }

        var result = new JObject
        {
            ["buttons"] = buttonNames,
            ["renameKey"] = GameManager.GameSaveRenameButtonKey
        };
        if (renameButton == null)
            return result;

        RectTransform parent = renameButton.transform.parent as RectTransform;
        TMP_Text label = renameButton.GetComponentInChildren<TMP_Text>(true);
        TMP_Text selectedSave = null;
        foreach (TMP_Text text in prefab.GetComponentsInChildren<TMP_Text>(true))
            if (text.name == GameManager.GameSaveSelectedTextKey)
                selectedSave = text;
        result["rename"] = new JObject
        {
            ["active"] = renameButton.gameObject.activeSelf,
            ["interactable"] = renameButton.interactable,
            ["raycast"] = renameButton.targetGraphic != null && renameButton.targetGraphic.raycastTarget,
            ["navigation"] = (int)renameButton.navigation.mode,
            ["label"] = label != null ? label.text : null,
            ["labelRaycast"] = label != null && label.raycastTarget,
            ["labelBounds"] = label != null ? CaptureSaveUiBounds(label.rectTransform, parent) : null,
            ["bounds"] = CaptureSaveUiBounds((RectTransform)renameButton.transform, parent),
            ["parent"] = CaptureSaveUiBounds(parent, parent),
            ["title"] = selectedSave != null ? CaptureSaveUiBounds(selectedSave.rectTransform, parent) : null,
            ["load"] = loadButton != null ? CaptureSaveUiBounds((RectTransform)loadButton.transform, parent) : null
        };
        return result;
    }

    private static JObject CaptureSaveUiBounds(RectTransform rect, RectTransform relativeTo)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector3 min = relativeTo.InverseTransformPoint(corners[0]);
        Vector3 max = min;
        for (int index = 1; index < corners.Length; index++)
        {
            Vector3 point = relativeTo.InverseTransformPoint(corners[index]);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return new JObject
        {
            ["left"] = min.x, ["right"] = max.x,
            ["bottom"] = min.y, ["top"] = max.y,
            ["width"] = rect.rect.width, ["height"] = rect.rect.height
        };
    }

    #endregion
}
#endif
