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
        GameObject root = CreateModalPanelRoot(WaterVesselPanel.PrefabKey, new Vector2(590f, 430f));
        Transform content = root.transform.Find("设置对话框");
        CreateText("陶罐标题", content, "水容器", 26f, Amber).gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
        CreateText("水量状态", content, "空容器　0 / 8 份", 22f, Cream).gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;
        CreateSettingsHint(content, "手持水容器对准水域使用即可装水；脏淡水可直接喝，也可烧开，海水可加热制盐。", 56f);
        Transform row = CreateFooter(content);
        CreateButton("饮水按钮", row, "饮水", 158f, 64f, true);
        CreateButton("转水按钮", row, "从手持容器倒入", 292f, 64f, false);
        Transform footer = CreateFooter(content);
        CreateButton("倒空按钮", footer, "倒空", 158f, 64f, false);
        CreateButton("关闭按钮", footer, "关闭", 158f, 64f, false);
        PortableBuildingPanelBuilder.ConfigureVessel(root);
        root.AddComponent<WaterVesselPanel>();
        return root;
    }
}
