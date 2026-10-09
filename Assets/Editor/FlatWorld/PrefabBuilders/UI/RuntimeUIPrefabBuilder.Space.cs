using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    #region 太空正式面板和模块资源
    [MenuItem("FlatWorld/太空/构建飞船与下落选址页面")]
    public static void RebuildSpacePanels()
    {
        if (Application.isPlaying) throw new InvalidOperationException("运行期间不重建太空 Prefab。");
        const string modulePath = "Assets/2_Prefabs/Gameplay/Modules/Space/Module_ShipPart.prefab";
        Directory.CreateDirectory(Path.GetDirectoryName(modulePath));
        GameObject module = new("Module_ShipPart"); module.AddComponent<Mod_ShipPart>();
        try { PrefabUtility.SaveAsPrefabAsset(module, modulePath); }
        finally { UnityEngine.Object.DestroyImmediate(module); }
        EnsureRuntimePrefabAddressable(modulePath);
        foreach (string name in new[] { "UI_Ship", "UI_SpaceLanding" })
        {
            GameObject root = PrefabUtility.LoadPrefabContents(IndustrialBasePath);
            try
            {
                ConfigureIndustrialPanel(root, "UI_FluidTank"); root.name = name;
                ((RectTransform)root.transform).sizeDelta = new Vector2(900, 740);
                MechanicalPanelView view = root.GetComponent<MechanicalPanelView>();
                view.Title.text = name == "UI_Ship" ? "飞船状态与操作" : "下落选址";
                view.Status.name = name == "UI_Ship" ? "FWUI_ShipStatus" : "FWUI_LandingStatus";
                view.Status.fontSize = 20;
                var status = (RectTransform)view.StatusScroll.transform;
                status.anchorMin = new Vector2(0, .35f); status.anchorMax = new Vector2(1, 1);
                status.offsetMin = new Vector2(18, 10); status.offsetMax = new Vector2(-18, -58);
                if (name == "UI_Ship")
                {
                    AddIndustrialField(root.transform, "FWUI_ShipX", "导航/落点 X", new Vector2(24, -530), new Vector2(210, 42));
                    AddIndustrialField(root.transform, "FWUI_ShipY", "导航/落点 Y", new Vector2(248, -530), new Vector2(210, 42));
                    AddIndustrialField(root.transform, "FWUI_ShipPressure", "舱内目标气压 kPa", new Vector2(472, -530), new Vector2(250, 42));
                    string[] keys = { "Drive", "Exit", "Ignite", "Navigate", "Pick", "Cancel", "Landing", "Dock", "Undock", "Pressure" };
                    string[] labels = { "驾驶", "退出驾驶", "点火 / 停机", "按坐标导航", "点击画面选点", "取消导航", "设置下落位置", "本船确认对接", "解除当前接口", "应用供气压强" };
                    for (int i = 0; i < keys.Length; i++)
                        AddIndustrialButton(root.transform, "FWUI_Ship" + keys[i], labels[i],
                            new Vector2(24 + i % 5 * 173, 128 - i / 5 * 56), new Vector2(162, 46), Vector2.zero, new Vector2(0, .5f));
                }
                else
                {
                    GameObject map = new("FWUI_LandingMap", typeof(RectTransform), typeof(RawImage));
                    map.transform.SetParent(root.transform, false);
                    var rect = (RectTransform)map.transform;
                    LayoutIndustrialControl(rect, new Vector2(0, 198), new Vector2(300, 300), new Vector2(.5f, 0), new Vector2(.5f, .5f));
                    status.anchorMin = new Vector2(0, .49f);
                }
                SetUILayerRecursively(root); FlatWorldUITheme.Apply(root.transform); PrepareSharedControlsForSave(root);
                string path = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path); EnsureRuntimePrefabAddressable(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        foreach (var group in settings.groups) if (group != null) EditorUtility.SetDirty(group);
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
    }
    #endregion
}
