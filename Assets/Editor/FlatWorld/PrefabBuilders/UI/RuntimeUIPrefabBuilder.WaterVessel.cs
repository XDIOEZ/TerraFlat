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
        GameObject root = CreateModalPanelRoot(WaterVesselPanel.PrefabKey, new Vector2(680f, 820f));
        root.SetActive(false);
        Transform content = root.transform.Find("设置对话框");
        CreateText("陶罐标题", content, "水容器", 26f, Amber).gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
        CreateText("水量状态", content, "空容器　0 / 8 份", 22f, Cream).gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;
        GameObject picture = CreateUIObject("陶罐剖面", content);
        picture.AddComponent<LayoutElement>().preferredHeight = 350f;
        GameObject art = CreateUIObject("陶罐切面", picture.transform, typeof(Image));
        SetCentered((RectTransform)art.transform, Vector2.zero, new Vector2(350f, 350f));
        art.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ClayJarUIArtBuilder.Root + "ClayJar_Cutaway.png");
        art.GetComponent<Image>().raycastTarget = false;
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
            new WaterVesselLiquidGraphic.LiquidStyle { VisualState = "filled", Body = new Color32(110,130,145,245), Surface = new Color32(180,200,210,255), Detail = new Color32(140,160,175,255) },
            new WaterVesselLiquidGraphic.LiquidStyle { VisualState = "dirty", Body = new Color32(103,105,58,245), Surface = new Color32(158,157,94,255), Detail = new Color32(68,76,43,255) },
            new WaterVesselLiquidGraphic.LiquidStyle { VisualState = "drinkable", Body = new Color32(57,139,187,235), Surface = new Color32(169,225,242,255), Detail = new Color32(124,196,224,255) },
            new WaterVesselLiquidGraphic.LiquidStyle { VisualState = "sea", Body = new Color32(35,120,140,245), Surface = new Color32(137,224,221,255), Detail = new Color32(221,247,234,255), Foam = true }
        };
        CreateSettingsHint(content, "手持水容器对准水域使用即可装水；脏淡水可直接喝，也可烧开，海水可加热制盐。", 56f);
        Transform row = CreateFooter(content);
        row.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
        CreateButton("饮水按钮", row, "饮水", 158f, 64f, true);
        CreateButton("转水按钮", row, "从手持容器倒入", 292f, 64f, false);
        Transform footer = CreateFooter(content);
        footer.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
        CreateButton("倒空按钮", footer, "倒空", 158f, 64f, false);
        CreateButton("关闭按钮", footer, "关闭", 158f, 64f, false);
        PortableBuildingPanelBuilder.ConfigureVessel(root);
        root.AddComponent<WaterVesselPanel>().Liquid = liquid;
        root.SetActive(true);
        return root;
    }
}
