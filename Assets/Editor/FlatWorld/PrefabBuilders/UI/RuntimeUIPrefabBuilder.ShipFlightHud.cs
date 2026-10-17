using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

public static partial class RuntimeUIPrefabBuilder
{
    #region 飞船起降高度尺
    [MenuItem("FlatWorld/UI/Rebuild Ship Flight HUD")]
    public static void RebuildShipFlightHUD()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) throw new MissingReferenceException($"缺少统一字体：{FontPath}");
        Directory.CreateDirectory(HudRoot);
        SaveShipFlightHudPrefab();
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerShipFlightHUD);
        AssetDatabase.SaveAssets();
        Debug.Log("[Runtime UI] 已固化飞船起降高度尺与玩家挂载。");
    }

    private static void SaveShipFlightHudPrefab()
    {
        string path = HudRoot + RuntimeUIPrefabKeys.ShipFlightHud + ".prefab";
        SaveNewPrefab(path, BuildShipFlightHUD);
        EnsureRuntimePrefabAddressable(path);
    }

    private static GameObject BuildShipFlightHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.ShipFlightHud, null, typeof(CanvasGroup));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
        rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = new Vector2(320f, 0f);
        rect.sizeDelta = new Vector2(240f, 460f);
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.alpha = 1f; group.interactable = false; group.blocksRaycasts = false;

        TextMeshProUGUI title = CreateText("AltitudeTitle", root.transform, "飞行高度", 18f, Muted);
        title.fontStyle = FontStyles.Bold;
        SetTopLeft(title.rectTransform, 0f, 0f, 240f, 30f);
        TextMeshProUGUI height = CreateText("AltitudeValue", root.transform, "80 m", 30f, Cream);
        height.fontStyle = FontStyles.Bold;
        SetTopLeft(height.rectTransform, 0f, 32f, 240f, 42f);

        GameObject tape = CreateUIObject("AltitudeTape", root.transform);
        SetTopLeft(tape.GetComponent<RectTransform>(), 0f, 88f, 170f, 280f);
        UnityEngine.UI.Image spine = CreateImage("TapeSpine", tape.transform, Muted);
        SetTopLeft(spine.rectTransform, 78f, 0f, 2f, 280f);
        var labels = new TMP_Text[5];
        for (int i = 0; i <= 20; i++)
        {
            float level = i / 20f;
            bool major = i % 5 == 0;
            UnityEngine.UI.Image tick = CreateImage("Tick" + i, tape.transform, Muted);
            ConfigureTapeNode(tick.rectTransform, level, major ? 64f : 70f, major ? 16f : 10f, 2f);
            if (!major) continue;
            TextMeshProUGUI label = CreateText("Scale" + i / 5, tape.transform, (i * 5).ToString(), 17f, Muted);
            label.alignment = TextAlignmentOptions.MidlineRight;
            ConfigureTapeNode(label.rectTransform, level, 0f, 56f, 26f);
            labels[i / 5] = label;
        }
        GameObject pointer = CreateUIObject("AltitudePointer", tape.transform);
        ConfigureTapeNode(pointer.GetComponent<RectTransform>(), .8f, 0f, 170f, 18f);
        UnityEngine.UI.Image upper = CreateImage("ChevronUpper", pointer.transform, Cream);
        SetTopLeft(upper.rectTransform, 82f, 4f, 12f, 2f);
        upper.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -30f);
        UnityEngine.UI.Image lower = CreateImage("ChevronLower", pointer.transform, Cream);
        SetTopLeft(lower.rectTransform, 82f, 12f, 12f, 2f);
        lower.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 30f);
        UnityEngine.UI.Image line = CreateImage("PointerLine", pointer.transform, Cream);
        SetTopLeft(line.rectTransform, 94f, 8f, 60f, 2f);
        UnityEngine.UI.Image cap = CreateImage("PointerEnd", pointer.transform, Cream);
        SetTopLeft(cap.rectTransform, 154f, 4f, 2f, 10f);
        TextMeshProUGUI hint = CreateText("TouchdownHint", root.transform, "", 17f, Amber);
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Truncate;
        SetTopLeft(hint.rectTransform, 0f, 396f, 240f, 64f);
        foreach (UnityEngine.UI.Graphic graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            graphic.raycastTarget = false;
        root.AddComponent<ShipFlightHudView>().Configure(pointer.GetComponent<RectTransform>(), title, height, hint,
            labels, new UnityEngine.UI.Graphic[] { upper, lower, line, cap }, Cream, Amber);
        return root;
    }

    private static void ConfigureTapeNode(RectTransform rect, float level, float left, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, level);
        rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = new Vector2(left, 0f);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void EnsurePlayerShipFlightHUD(GameObject root)
    {
        // 驾驶 HUD 随玩家重建和销毁，不由全局管理器持有玩家引用。
        if (root != null && root.GetComponent<PlayerShipFlightHUD>() == null)
            root.AddComponent<PlayerShipFlightHUD>();
    }
    #endregion
}
