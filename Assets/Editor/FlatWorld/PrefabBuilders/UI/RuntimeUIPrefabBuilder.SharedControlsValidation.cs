using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.Audio;
using FlatWorld.Settings;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    #region 公共控件回归验证

    /// <summary>验证正式资产的嵌套关系、外观继承和音量手柄，失败时输出具体资产与节点。</summary>
    [MenuItem("FlatWorld/UI/Shared Controls/Validate Prefabs")]
    public static void ValidateSharedControlPrefabs()
    {
        RequireSharedUI(!EditorApplication.isPlayingOrWillChangePlaymode, "资产验证必须在编辑模式执行");
        int instanceCount = 0;
        int panelCount = 0;
        foreach (string key in SharedControlNames)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SharedControlsRoot + key + ".prefab");
            RequireSharedUI(asset != null && asset.GetComponent<ReusableUIControl>() != null, "缺少公共资产：" + key);
            foreach (Transform node in asset.GetComponentsInChildren<Transform>(true))
                RequireSharedUI(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject) == 0, key + " 有丢失脚本");
        }
        foreach (string key in new[] { "UI_CloseButton", "UI_TabButton", "UI_ProgressBar", "UI_SwitchOption" })
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SharedControlsRoot + key + ".prefab");
            RequireSharedUI(PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant, key + " 不是继承基础控件的 Variant");
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsSharedLibraryAsset(path))
                continue;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ReusableUIControl[] instances = root.GetComponentsInChildren<ReusableUIControl>(true);
                if (instances.Length == 0)
                    continue;
                panelCount++;
                foreach (ReusableUIControl instance in instances)
                {
                    RequireSharedUI(PrefabUtility.IsPartOfPrefabInstance(instance), path + "/" + instance.name + " 已失去 Prefab 连接");
                    PropertyModification[] overrides = PrefabUtility.GetPropertyModifications(instance.gameObject);
                    if (overrides != null)
                    {
                        foreach (PropertyModification value in overrides)
                        {
                            bool colorOverride = value.target is Graphic graphic && ReusableUIControl.OwnsVisuals(graphic) &&
                                value.propertyPath.StartsWith("m_Color");
                            RequireSharedUI(!colorOverride, path + "/" + instance.name + " 存在阻断外观继承的颜色覆盖");
                        }
                    }
                    instanceCount++;
                }
                foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null)
                        continue;
                    SerializedObject serialized = new SerializedObject(component);
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType == SerializedPropertyType.ObjectReference)
                            RequireSharedUI(property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0,
                                path + "/" + component.name + "." + property.propertyPath + " 引用丢失");
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        RequireSharedUI(instanceCount > 6, "迁移范围不足，尚未在窗口中应用公共控件");
        ValidateAudioSliderAssets();
        ValidateSharedStylePropagation();
        string message = $"PASS: {SharedControlNames.Length} 个公共资产，4 个 Variant，{panelCount} 个引用资产，{instanceCount} 个嵌套实例；引用、六路音量几何、主题隔离和源资产样式传播通过。";
        Directory.CreateDirectory("Temp/SharedUI");
        File.WriteAllText("Temp/SharedUI/validation.txt", message);
        Debug.Log("[Shared UI] " + message);
    }

    /// <summary>直接检查正式音量页；零值/中值/满值均验证填充，主题再应用不得破坏手柄。</summary>
    private static void ValidateAudioSliderAssets()
    {
        string path = SettingsPanelsRoot + RuntimeUIPrefabKeys.AudioSettings + ".prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Slider[] sliders = root.GetComponentsInChildren<Slider>(true).Where(value => IsAudioSlider(value.name)).ToArray();
            RequireSharedUI(sliders.Length == 6, "音量页必须有六个命名稳定的滑块");
            foreach (Slider slider in sliders)
            {
                RequireSharedUI(slider.GetComponent<ReusableUIControl>() != null, slider.name + " 仍是独立控件");
                RequireSharedUI(slider.handleRect != null && slider.handleRect.gameObject.activeSelf && slider.handleRect.sizeDelta.y > 0f,
                    slider.name + " 手柄不可见或高度为零");
                RequireSharedUI(slider.GetComponent<Image>().raycastTarget && slider.GetComponent<Image>().color.a == 0f,
                    slider.name + " 命中区与轨道未分离");
                RequireSharedUI(slider.GetComponent<LayoutElement>().preferredHeight >= 60f, slider.name + " 触控高度不足");
                foreach (float value in new[] { 0f, 0.5f, 1f })
                {
                    slider.SetValueWithoutNotify(value);
                    RequireSharedUI(Mathf.Abs(slider.normalizedValue - value) < 0.001f, slider.name + " 数值范围异常");
                    RequireSharedUI(Mathf.Abs(slider.fillRect.anchorMax.x - value) < 0.001f, slider.name + " 填充未随数值更新");
                }
            }
            var visuals = root.GetComponentsInChildren<Graphic>(true)
                .Where(ReusableUIControl.OwnsVisuals).Select(value => (Graphic: value, Color: value.color)).ToArray();
            FlatWorldUITheme.Apply(root.transform);
            foreach (var visual in visuals)
                RequireSharedUI(visual.Graphic.color == visual.Color, visual.Graphic.name + " 被主题代码覆盖");
            foreach (Slider slider in sliders)
                RequireSharedUI(slider.handleRect.sizeDelta.y > 0f, slider.name + " 被主题压扁");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>短暂修改基础按钮资产，检查真实窗口与 Variant 继承，再无条件恢复原始样式。</summary>
    private static void ValidateSharedStylePropagation()
    {
        string sourcePath = SharedControlsRoot + "UI_Button.prefab";
        GameObject source = PrefabUtility.LoadPrefabContents(sourcePath);
        Image image = source.GetComponent<Image>();
        Color original = image.color;
        Color probe = new Color(0.31f, 0.32f, 0.33f, 1f);
        GameObject panel = null;
        try
        {
            image.color = probe;
            PrefabUtility.SaveAsPrefabAsset(source, sourcePath);
            // 正常构建入口只能补建缺失资产，不能覆盖用户刚编辑的公共外观。
            EnsureSharedControlPrefabs();
            panel = PrefabUtility.LoadPrefabContents(SettingsComponentsRoot + RuntimeUIPrefabKeys.InputBindingRow + ".prefab");
            Button[] buttons = panel.GetComponentsInChildren<Button>(true);
            RequireSharedUI(buttons.Length >= 3, "按键绑定行公共按钮不足");
            foreach (Button button in buttons)
                RequireSharedUI(button.GetComponent<Image>().color == probe, button.name + " 未继承基础按钮的新外观");
            foreach (string key in new[] { "UI_CloseButton", "UI_TabButton" })
            {
                GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>(SharedControlsRoot + key + ".prefab");
                RequireSharedUI(variant.GetComponent<Image>().color == probe, key + " 未继承基础按钮的新外观");
            }
        }
        finally
        {
            if (panel != null)
                PrefabUtility.UnloadPrefabContents(panel);
            image.color = original;
            PrefabUtility.SaveAsPrefabAsset(source, sourcePath);
            PrefabUtility.UnloadPrefabContents(source);
        }
    }

    /// <summary>在已经打开的真实音量页测试 UI 到 Provider 的写入与百分比；始终恢复玩家原音量。</summary>
    [MenuItem("FlatWorld/UI/Shared Controls/Validate Runtime Audio")]
    public static void ValidateSharedRuntimeAudio()
    {
        RequireSharedUI(EditorApplication.isPlaying, "运行时音量验证必须在 Play Mode 执行");
        AudioSettingsPanelLauncher page = UnityEngine.Object.FindObjectsOfType<AudioSettingsPanelLauncher>(true)
            .FirstOrDefault(value => value.gameObject.activeInHierarchy);
        RequireSharedUI(page != null, "请先打开音量设置页");
        AudioSettingsPanelLauncher.Ensure(page.transform);
        AudioSettingsPanelBinder.Ensure(page.transform).Bind();
        AudioSettingsPanelBinder.Ensure(page.transform).Bind();
        ISettingsProvider provider = AudioService.Instance;
        UnityEngine.Canvas.ForceUpdateCanvases();
        string[] names = { "MasterVolume", "MusicVolume", "SfxVolume", "UIVolume", "AmbientVolume", "VoiceVolume" };
        string[] keys = { AudioService.MasterVolumeSettingKey, AudioService.MusicVolumeSettingKey,
            AudioService.SfxVolumeSettingKey, AudioService.UiVolumeSettingKey,
            AudioService.AmbientVolumeSettingKey, AudioService.VoiceVolumeSettingKey };
        for (int index = 0; index < names.Length; index++)
        {
            Slider slider = page.GetComponentsInChildren<Slider>(true).First(value => value.name == names[index]);
            TMP_Text label = page.GetComponentsInChildren<TMP_Text>(true).First(value => value.name == names[index] + "_数值");
            ISettingsSlider setting = provider.GetSlider(keys[index]);
            float original = setting.Value;
            ScrollRect scroll = slider.GetComponentInParent<ScrollRect>();
            float scrollPosition = scroll != null ? scroll.verticalNormalizedPosition : 1f;
            try
            {
                if (scroll != null)
                    scroll.verticalNormalizedPosition = 1f - (float)index / (names.Length - 1);
                UnityEngine.Canvas.ForceUpdateCanvases();
                foreach (float value in new[] { 0f, 0.37f, 1f })
                {
                    DragRuntimeAudioSlider(slider, value);
                    RequireSharedUI(Mathf.Abs(setting.Value - value) < 0.001f, names[index] + " 未写入音频服务");
                    RequireSharedUI(label.text == Mathf.RoundToInt(value * 100f) + "%", names[index] + " 百分比未同步");
                }
            }
            finally
            {
                setting.SetValue(original);
                slider.SetValueWithoutNotify(original);
                page.OnSettingsPageShown();
                if (scroll != null)
                    scroll.verticalNormalizedPosition = scrollPosition;
            }
        }
        Debug.Log("[Shared UI] PASS: 六路音量真实射线/拖拽、Provider 写入、百分比与重复绑定验证通过；原音量已恢复。");
    }

    /// <summary>经过实际 UI 射线与原生 Slider 拖拽事件测试，不直接写 value 绕过命中区。</summary>
    private static void DragRuntimeAudioSlider(Slider slider, float normalized)
    {
        RectTransform area = (RectTransform)slider.handleRect.parent;
        Canvas canvas = slider.GetComponentInParent<Canvas>().rootCanvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector3 world = area.TransformPoint(new Vector3(Mathf.Lerp(area.rect.xMin, area.rect.xMax, normalized), area.rect.center.y));
        PointerEventData pointer = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(camera, world)
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        RequireSharedUI(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Slider>() == slider,
            slider.name + " 未通过真实 UI 射线命中，可能被遮挡或布局越界");
        pointer.pointerPressRaycast = hits[0];
        ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.initializePotentialDrag);
        ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerUpHandler);
    }

    private static void RequireSharedUI(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("[Shared UI] " + message);
    }

    #endregion
}
