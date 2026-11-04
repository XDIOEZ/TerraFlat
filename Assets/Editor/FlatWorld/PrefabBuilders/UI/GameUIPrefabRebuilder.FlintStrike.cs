using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class GameUIPrefabRebuilder
{
    #region 燧石器物面板

    private static void BuildFlintStrikeArtworkWindow(GameObject root)
    {
        RectTransform frame = FindRect(root.transform, "FlintStrikeContent");
        if (frame == null)
        {
            frame = CreateRect("FlintStrikeContent", root.transform);
            for (int index = root.transform.childCount - 1; index >= 0; index--)
            {
                Transform child = root.transform.GetChild(index);
                if (child != frame) child.SetParent(frame, false);
            }
        }
        frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
        frame.sizeDelta = new Vector2(680f, 500f);
        frame.anchoredPosition = Vector2.zero;
        if (frame.GetComponent<UIVisualOwner>() == null) frame.gameObject.AddComponent<UIVisualOwner>();
        foreach (string name in new[] { "FWUI_标题", "FWUI_FooterHint", "FWUI_SectionTitle_INPUT", "FWUI_SectionTitle_OUTPUT" })
            FindRect(frame, name).SetParent(frame, false);
        foreach (string name in new[] { "FWUI_Chrome", "UI_PanelBackground" })
        {
            RectTransform decoration = FindRect(frame, name);
            if (decoration != null) Object.DestroyImmediate(decoration.gameObject);
        }
        FindRect(frame, "Progress").gameObject.SetActive(false);
        Image background = root.GetComponent<Image>();
        background.color = Color.clear;
        background.raycastTarget = false;
        foreach (Outline outline in root.GetComponents<Outline>()) outline.enabled = false;
        RectTransform artwork = FindRect(frame, "FlintStrikeArtwork");
        if (artwork == null) artwork = CreateRect("FlintStrikeArtwork", frame);
        SetTopLeft(artwork, 40f, 65f, 600f, 300f);
        artwork.SetAsFirstSibling();
        FlintStrikeToolGraphic graphic = artwork.GetComponent<FlintStrikeToolGraphic>();
        if (graphic == null) graphic = artwork.gameObject.AddComponent<FlintStrikeToolGraphic>();
        graphic.raycastTarget = false;
        graphic.ConfigureArtwork(AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/6_Art/Generated/Tools/FlintStrikeUI/FlintStrike_Components.png"));
        SetTopLeft(FindRect(frame, "FWUI_标题"), 24f, 12f, 540f, 40f);
        SetTopLeft(FindRect(frame, "FWUI_FooterHint"), 24f, 470f, 632f, 24f);
        SetTopLeft(FindRect(frame, "输入"), 296f, 302f, 88f, 88f);
        SetTopLeft(FindRect(frame, "输出"), 554f, 302f, 88f, 88f);
        foreach (string name in new[] { "输入_1", "输出_1" })
            SetTopLeft(FindRect(frame, name), 0f, 0f, 88f, 88f);
        SetTopLeft(FindRect(frame, "FWUI_SectionTitle_INPUT"), 288f, 392f, 104f, 20f);
        SetTopLeft(FindRect(frame, "FWUI_SectionTitle_OUTPUT"), 546f, 392f, 104f, 20f);
        foreach (string name in new[] { "FWUI_SectionTitle_INPUT", "FWUI_SectionTitle_OUTPUT", "FWUI_FooterHint" })
            FindRect(frame, name).GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;
        SetTopLeft(FindRect(frame, "合成按钮"), 250f, 414f, 180f, 46f);
        SetTopLeft(FindRect(frame, "关闭"), 608f, 12f, 48f, 48f);
        root.GetComponent<UI_Drag>().rectTransform = frame;
        root.GetComponentInChildren<UIWindowDragSurface>(true).Configure(frame, 58f, 10f);
        root.GetComponent<BaseUIAnimation>().SetMotionRoot(frame);
        Stretch(root.GetComponent<RectTransform>());
        SafeAreaScaleGroup scale = root.GetComponent<SafeAreaScaleGroup>();
        if (scale == null) scale = root.AddComponent<SafeAreaScaleGroup>();
        scale.Configure(frame, new[] { frame }, new Vector2(20f, 20f));
    }

    #endregion
}
