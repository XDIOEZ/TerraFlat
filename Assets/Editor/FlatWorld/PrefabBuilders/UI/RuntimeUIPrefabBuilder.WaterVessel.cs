using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    /// <summary>装配可供桌面和触屏共用的通用水容器操作窗口。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Water Vessel UI")]
    public static void RebuildWaterVesselUI()
    {
        ClayJarUIArtBuilder.Build();
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        string path = "Assets/2_Prefabs/2-1_UI/Gameplay/Containers/" + WaterVesselPanel.PrefabKey + ".prefab";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        SaveNewPrefab(path, BuildWaterVessel);
        EnsureRuntimePrefabAddressable(path);
        AssetDatabase.SaveAssets();
    }
    /// <summary>使用统一面板和按钮尺寸生成持久化控件，不在运行时拼装视觉节点。</summary>
    private static GameObject BuildWaterVessel()
    {
        GameObject root = CreateModalPanelRoot(WaterVesselPanel.PrefabKey, new Vector2(800f, 700f));
        root.SetActive(false);
        Transform content = root.transform.Find("设置对话框");
        CreateText("陶罐标题", content, "水容器", 26f, Amber).gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
        CreateText("水量状态", content, "空容器　0 / 8 份", 22f, Cream).gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;
        GameObject picture = CreateUIObject("陶罐剖面", content);
        picture.AddComponent<LayoutElement>().preferredHeight = 380f;
        GameObject pour = CreateUIObject("倾倒液流", picture.transform, typeof(WaterVesselPourGraphic));
        SetCentered((RectTransform)pour.transform, new Vector2(-115f, 0f), new Vector2(420f, 420f));
        pour.GetComponent<WaterVesselPourGraphic>().raycastTarget = false;
        GameObject art = CreateUIObject("陶罐切面", picture.transform, typeof(Image));
        SetCentered((RectTransform)art.transform, new Vector2(-115f, 0f), new Vector2(350f, 350f));
        art.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ClayJarUIArtBuilder.Root + "ClayJar_Cutaway.png");
        art.GetComponent<Image>().raycastTarget = false;
        RectTransform leftPourOutlet = (RectTransform)CreateUIObject("左罐口出口", art.transform).transform;
        SetCentered(leftPourOutlet, new Vector2(-58f, 120f), Vector2.zero);
        RectTransform rightPourOutlet = (RectTransform)CreateUIObject("右罐口出口", art.transform).transform;
        SetCentered(rightPourOutlet, new Vector2(58f, 120f), Vector2.zero);
        GameObject cavity = CreateUIObject("罐内轮廓", art.transform, typeof(Image), typeof(Mask));
        Stretch((RectTransform)cavity.transform);
        cavity.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ClayJarUIArtBuilder.Root + "ClayJar_Interior.png");
        cavity.GetComponent<Image>().raycastTarget = false;
        cavity.GetComponent<Mask>().showMaskGraphic = false;
        GameObject water = CreateUIObject("液体表现", cavity.transform, typeof(WaterVesselLiquidGraphic));
        Stretch((RectTransform)water.transform);
        WaterVesselLiquidGraphic liquid = water.GetComponent<WaterVesselLiquidGraphic>();
        liquid.raycastTarget = false;
        liquid.Styles = new[]
        {
            new WaterVesselLiquidGraphic.LiquidStyle
            {
                VisualState = "filled",
                Body = new Color32(110,130,145,245), Surface = new Color32(180,200,210,255),
                Detail = new Color32(140,160,175,255), Deep = new Color32(110,130,145,245)
            },
            new WaterVesselLiquidGraphic.LiquidStyle
            {
                VisualState = "dirty",
                Body = new Color32(106,95,56,252), Surface = new Color32(177,160,107,255),
                Detail = new Color32(80,72,45,255), Deep = new Color32(70,58,37,255),
                Murkiness = 0.96f, Sediment = 0.14f, SurfaceDebris = 0.9f, SuspendedParticles = 0.92f
            },
            new WaterVesselLiquidGraphic.LiquidStyle
            {
                VisualState = "drinkable",
                Body = new Color32(57,139,187,235), Surface = new Color32(169,225,242,255),
                Detail = new Color32(124,196,224,255), Deep = new Color32(57,139,187,235)
            },
            new WaterVesselLiquidGraphic.LiquidStyle
            {
                VisualState = "sea",
                Body = new Color32(35,120,140,245), Surface = new Color32(137,224,221,255),
                Detail = new Color32(221,247,234,255), Deep = new Color32(35,120,140,245), Foam = true
            }
        };
        CreateSettingsHint(content, "手持水容器对准水域使用即可装水；拖动陶罐可倾倒，脏淡水可直接喝，也可烧开，海水可加热制盐。", 64f);

        GameObject actions = CreateUIObject("操作列表", content);
        LayoutElement actionsLayout = actions.AddComponent<LayoutElement>();
        actionsLayout.ignoreLayout = true;
        actionsLayout.preferredWidth = 220f;
        actionsLayout.preferredHeight = 400f;
        SetCentered((RectTransform)actions.transform, new Vector2(250f, 10f), new Vector2(220f, 400f));
        VerticalLayoutGroup actionsGroup = actions.AddComponent<VerticalLayoutGroup>();
        actionsGroup.spacing = 10f;
        actionsGroup.childAlignment = TextAnchor.UpperCenter;
        actionsGroup.childControlWidth = true;
        actionsGroup.childControlHeight = true;
        actionsGroup.childForceExpandWidth = true;
        actionsGroup.childForceExpandHeight = false;
        CreateButton("饮水按钮", actions.transform, "饮水", 220f, 64f, true);
        CreateButton("关闭按钮", actions.transform, "关闭", 220f, 64f, false);
        PortableBuildingPanelBuilder.ConfigureVessel(root);
        ConfigureBackgroundlessWaterVessel(root, content);
        WaterVesselPanel panel = root.AddComponent<WaterVesselPanel>();
        panel.Liquid = liquid;
        panel.LeftPourOutlet = leftPourOutlet;
        panel.RightPourOutlet = rightPourOutlet;
        root.SetActive(true);
        return root;
    }

    /// <summary>与石臼一致移除整块灰色底板，仅保留透明射线阻挡面和独立操作按钮。</summary>
    private static void ConfigureBackgroundlessWaterVessel(GameObject root, Transform content)
    {
        Image rootBlocker = root.GetComponent<Image>();
        rootBlocker.color = Color.clear;
        rootBlocker.raycastTarget = true;

        Image contentBlocker = content.GetComponent<Image>();
        contentBlocker.color = Color.clear;
        contentBlocker.raycastTarget = true;

        Outline contentOutline = content.GetComponent<Outline>();
        if (contentOutline != null)
            Object.DestroyImmediate(contentOutline);
    }
}
