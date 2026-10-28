// 按业务职责维护 Prefab，复用公共控件与布局实现。
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 客户端设置页

    /// <summary>构建可嵌入主设置内容区的音量分页。</summary>
    private static GameObject BuildAudioSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.AudioSettings,
            out Transform content);
        CreateSettingsHeader(content, "音量调节");
        CreateSettingsHint(content, "主音量控制全部声音；其他通道可以单独调整。设置会自动保存。", 42f);

        CreateSliderRow(content, "主音量", "MasterVolume");
        CreateSliderRow(content, "音乐音量", "MusicVolume");
        CreateSliderRow(content, "音效音量", "SfxVolume");
        CreateSliderRow(content, "UI 音量", "UIVolume");
        CreateSliderRow(content, "环境音量", "AmbientVolume");
        CreateSliderRow(content, "语音音量", "VoiceVolume");

        Transform footer = CreateFooter(content);
        CreateSettingsButton("恢复默认按钮", footer, "恢复默认", 132f, 42f, false);
        return root;
    }

    /// <summary>构建界面缩放、UI 动画速度、快捷栏间距、滚轮翻行数、触屏透明度、安全区、移动摇杆与触控分区设置页。</summary>
    private static GameObject BuildInterfaceSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.UISettings,
            out Transform content);
        CreateSettingsHeader(content, "界面设置");
        CreateSettingsHint(
            content,
            "左右触控区决定移动和普通指向摇杆的响应范围；中间区域保留给后续操作。调整会立即保存。",
            54f);

        GameObject scaleRow = CreateRow("界面缩放行", content, 52f);
        CreateRowLabel(scaleRow.transform, "界面缩放", 112f);
        Slider slider = CreateSlider("界面缩放", scaleRow.transform);
        slider.minValue = UIUserSettings.MinimumScale;
        slider.maxValue = UIUserSettings.MaximumScale;
        slider.value = UIUserSettings.DefaultScale;
        TextMeshProUGUI valueText = CreateText(
            "界面缩放数值",
            scaleRow.transform,
            $"{Mathf.RoundToInt(UIUserSettings.DefaultScale * 100f)}%",
            16f,
            Amber);
        valueText.alignment = TextAlignmentOptions.MidlineRight;
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        GameObject animationSpeedRow = CreateRow("UI动画速度行", content, 52f);
        CreateRowLabel(animationSpeedRow.transform, "UI 动画速度", 112f);
        Slider animationSpeedSlider = CreateSlider("UI动画速度", animationSpeedRow.transform);
        animationSpeedSlider.minValue = UIUserSettings.MinimumAnimationSpeed;
        animationSpeedSlider.maxValue = UIUserSettings.MaximumAnimationSpeed;
        animationSpeedSlider.value = UIUserSettings.DefaultAnimationSpeed;
        TextMeshProUGUI animationSpeedValue = CreateText(
            "UI动画速度数值",
            animationSpeedRow.transform,
            $"{UIUserSettings.DefaultAnimationSpeed:0.##}×",
            16f,
            Amber);
        animationSpeedValue.alignment = TextAlignmentOptions.MidlineRight;
        animationSpeedValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        GameObject hotbarSpacingRow = CreateRow("快捷栏底部间距行", content, 52f);
        CreateRowLabel(hotbarSpacingRow.transform, "快捷栏底部间距", 136f);
        Slider hotbarSpacingSlider = CreateSlider("快捷栏底部间距", hotbarSpacingRow.transform);
        hotbarSpacingSlider.minValue = UIUserSettings.MinimumHotbarBottomSpacing;
        hotbarSpacingSlider.maxValue = UIUserSettings.MaximumHotbarBottomSpacing;
        hotbarSpacingSlider.wholeNumbers = true;
        hotbarSpacingSlider.value = UIUserSettings.DefaultHotbarBottomSpacing;
        TextMeshProUGUI hotbarSpacingValue = CreateText(
            "快捷栏底部间距数值",
            hotbarSpacingRow.transform,
            $"{Mathf.RoundToInt(UIUserSettings.DefaultHotbarBottomSpacing)} px",
            16f,
            Amber);
        hotbarSpacingValue.alignment = TextAlignmentOptions.MidlineRight;
        hotbarSpacingValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        GameObject wheelRowsRow = CreateRow("滚轮每次翻动行数行", content, 52f);
        CreateRowLabel(wheelRowsRow.transform, "滚轮每次翻动行数", 160f);
        Slider wheelRowsSlider = CreateSlider("滚轮每次翻动行数", wheelRowsRow.transform);
        wheelRowsSlider.minValue = UIUserSettings.MinimumScrollRowsPerWheelTick;
        wheelRowsSlider.maxValue = UIUserSettings.MaximumScrollRowsPerWheelTick;
        wheelRowsSlider.wholeNumbers = true;
        wheelRowsSlider.value = UIUserSettings.DefaultScrollRowsPerWheelTick;
        TextMeshProUGUI wheelRowsValue = CreateText(
            "滚轮每次翻动行数数值",
            wheelRowsRow.transform,
            $"{UIUserSettings.DefaultScrollRowsPerWheelTick} 行",
            16f,
            Amber);
        wheelRowsValue.alignment = TextAlignmentOptions.MidlineRight;
        wheelRowsValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        GameObject opacityRow = CreateRow("触屏控件透明度行", content, 52f);
        CreateRowLabel(opacityRow.transform, "触屏控件透明度", 128f);
        Slider opacitySlider = CreateSlider("触屏控件透明度", opacityRow.transform);
        opacitySlider.minValue = UIUserSettings.MinimumTouchControlsOpacityPercent;
        opacitySlider.maxValue = UIUserSettings.MaximumTouchControlsOpacityPercent;
        opacitySlider.wholeNumbers = true;
        opacitySlider.value = UIUserSettings.DefaultTouchControlsOpacityPercent;
        TextMeshProUGUI opacityValue = CreateText(
            "触屏控件透明度数值",
            opacityRow.transform,
            $"{Mathf.RoundToInt(UIUserSettings.DefaultTouchControlsOpacityPercent)}%",
            16f,
            Amber);
        opacityValue.alignment = TextAlignmentOptions.MidlineRight;
        opacityValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        GameObject safeAreaRow = CreateRow("安全区域适配行", content, 48f);
        TextMeshProUGUI safeLabel = CreateText("安全区域说明", safeAreaRow.transform, "适配屏幕安全区域", 17f, Cream);
        safeLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateToggle("安全区域适配", safeAreaRow.transform);

        GameObject joystickRow = CreateRow("移动摇杆模式行", content, 48f);
        TextMeshProUGUI joystickLabel = CreateText(
            "移动摇杆模式说明",
            joystickRow.transform,
            "浮动移动摇杆（关闭则固定）",
            17f,
            Cream);
        joystickLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Toggle joystickToggle = CreateToggle("浮动移动摇杆", joystickRow.transform);
        joystickToggle.isOn = true;

        GameObject leftZoneRow = CreateRow("左侧触控区比例行", content, 52f);
        CreateRowLabel(leftZoneRow.transform, "左侧触控区", 128f);
        Slider leftZoneSlider = CreateSlider("左侧触控区比例", leftZoneRow.transform);
        leftZoneSlider.minValue = UIUserSettings.MinimumControlZoneRatio;
        leftZoneSlider.maxValue = UIUserSettings.MaximumControlZoneRatio;
        leftZoneSlider.value = UIUserSettings.DefaultLeftControlZoneRatio;
        TextMeshProUGUI leftZoneValue = CreateText(
            "左侧触控区数值",
            leftZoneRow.transform,
            "33%",
            16f,
            Amber);
        leftZoneValue.alignment = TextAlignmentOptions.MidlineRight;
        leftZoneValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        GameObject rightZoneRow = CreateRow("右侧触控区比例行", content, 52f);
        CreateRowLabel(rightZoneRow.transform, "右侧触控区", 128f);
        Slider rightZoneSlider = CreateSlider("右侧触控区比例", rightZoneRow.transform);
        rightZoneSlider.minValue = UIUserSettings.MinimumControlZoneRatio;
        rightZoneSlider.maxValue = UIUserSettings.MaximumControlZoneRatio;
        rightZoneSlider.value = UIUserSettings.DefaultRightControlZoneRatio;
        TextMeshProUGUI rightZoneValue = CreateText(
            "右侧触控区数值",
            rightZoneRow.transform,
            "33%",
            16f,
            Amber);
        rightZoneValue.alignment = TextAlignmentOptions.MidlineRight;
        rightZoneValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        TextMeshProUGUI zoneStatus = CreateText(
            "触控区域比例文本",
            content,
            "触控区域比例：左 33%｜中 34%｜右 33%",
            16f,
            Amber);
        zoneStatus.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        TextMeshProUGUI status = CreateText("状态文本", content, "安全区域适配：开启（推荐）", 16f, Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Transform footer = CreateFooter(content);
        CreateSettingsButton("恢复默认按钮", footer, "恢复默认", 132f, 42f, false);
        return root;
    }

    /// <summary>构建可滚动的内嵌镜头控制页。</summary>
    private static GameObject BuildCameraControlSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.CameraControlSettings,
            out Transform content);
        CreateSettingsHeader(content, "镜头控制");
        CreateSettingsHint(
            content,
            "双指缩放灵敏度为 0 时关闭，数值越大响应越快。镜头前探正值为提前跟随，负值为惯性；缩放影响系数为正时拉远会增强预测，为负时会减弱。",
            64f);

        GameObject pinchZoomRow = CreateRow("双指缩放行", content, 52f);
        CreateRowLabel(pinchZoomRow.transform, "双指缩放灵敏度", 128f);
        Slider pinchZoomSlider = CreateSlider("双指缩放灵敏度", pinchZoomRow.transform);
        pinchZoomSlider.minValue = UIUserSettings.MinimumPinchZoomSensitivity;
        pinchZoomSlider.maxValue = UIUserSettings.MaximumPinchZoomSensitivity;
        pinchZoomSlider.wholeNumbers = true;
        pinchZoomSlider.value = UIUserSettings.DefaultPinchZoomSensitivity;
        TextMeshProUGUI pinchZoomValue = CreateText(
            "双指缩放灵敏度数值",
            pinchZoomRow.transform,
            "0",
            16f,
            Amber);
        pinchZoomValue.alignment = TextAlignmentOptions.MidlineRight;
        pinchZoomValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

        GameObject lookaheadRow = CreateRow("镜头前探行", content, 52f);
        CreateRowLabel(lookaheadRow.transform, "镜头前探", 128f);
        Slider lookaheadSlider = CreateSlider("镜头前探", lookaheadRow.transform);
        lookaheadSlider.minValue = CameraUserSettings.MinimumLookahead;
        lookaheadSlider.maxValue = CameraUserSettings.MaximumLookahead;
        lookaheadSlider.value = CameraUserSettings.DefaultLookahead;
        TextMeshProUGUI lookaheadValue = CreateText(
            "镜头前探数值",
            lookaheadRow.transform,
            "+0.00s",
            16f,
            Amber);
        lookaheadValue.alignment = TextAlignmentOptions.MidlineRight;
        lookaheadValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

        GameObject smoothingRow = CreateRow("预判平滑行", content, 52f);
        CreateRowLabel(smoothingRow.transform, "预判平滑", 128f);
        Slider smoothingSlider = CreateSlider("预判平滑", smoothingRow.transform);
        smoothingSlider.minValue = CameraUserSettings.MinimumLookaheadSmoothing;
        smoothingSlider.maxValue = CameraUserSettings.MaximumLookaheadSmoothing;
        smoothingSlider.value = CameraUserSettings.DefaultLookaheadSmoothing;
        TextMeshProUGUI smoothingValue = CreateText(
            "预判平滑数值",
            smoothingRow.transform,
            "0.0",
            16f,
            Amber);
        smoothingValue.alignment = TextAlignmentOptions.MidlineRight;
        smoothingValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

        GameObject influenceRow = CreateRow("缩放影响系数行", content, 52f);
        CreateRowLabel(influenceRow.transform, "缩放影响系数", 128f);
        Slider influenceSlider = CreateSlider("缩放影响系数", influenceRow.transform);
        influenceSlider.minValue = CameraUserSettings.MinimumLookaheadZoomInfluence;
        influenceSlider.maxValue = CameraUserSettings.MaximumLookaheadZoomInfluence;
        influenceSlider.value = CameraUserSettings.DefaultLookaheadZoomInfluence;
        TextMeshProUGUI influenceValue = CreateText(
            "缩放影响系数数值",
            influenceRow.transform,
            "+0.00",
            16f,
            Amber);
        influenceValue.alignment = TextAlignmentOptions.MidlineRight;
        influenceValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

        TextMeshProUGUI status = CreateText(
            "状态文本",
            content,
            "调整会立即应用并自动保存。",
            16f,
            Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Transform footer = CreateFooter(content);
        CreateSettingsButton("恢复默认按钮", footer, "恢复默认", 132f, 42f, false);
        return root;
    }

    /// <summary>构建左上角 HUD 设置页；坐标格式与 FPS 显隐均立即写入本地偏好。</summary>
    private static GameObject BuildCoordinateDisplaySettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.CoordinateDisplaySettings,
            out Transform content);
        CreateSettingsHeader(content, "显示设置");
        CreateSettingsHint(content, "调整会立即应用并自动保存。", 48f);

        CreateSettingsSection(content, "坐标显示", string.Empty);
        GameObject modeRow = CreateRow("坐标显示方式行", content, 54f);
        CreateRowLabel(modeRow.transform, "显示方式", 100f);
        CreateSettingsButton("世界坐标模式按钮", modeRow.transform, "世界坐标  X / Y", 252f, 46f, true);
        CreateSettingsButton("经纬度模式按钮", modeRow.transform, "经纬度  经 / 纬", 252f, 46f, false);

        TextMeshProUGUI status = CreateText(
            "状态文本",
            content,
            "当前显示：世界坐标（X / Y）",
            16f,
            Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        CreateSettingsSection(content, "FPS", string.Empty);
        GameObject fpsRow = CreateRow("FPS显示行", content, 54f);
        TextMeshProUGUI fpsLabel = CreateText("FPS显示标签", fpsRow.transform, "FPS", 17f, Cream);
        fpsLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Toggle fpsToggle = CreateToggle("FPS显示开关", fpsRow.transform);
        fpsToggle.isOn = PlayerWorldCoordinateDisplayPreferences.DefaultShowFps;

        CreateFrameRateSettingsControls(content);
        return root;
    }

    /// <summary>帧率控件共用正式下拉和输入框，内容增长由显示页滚动区域承载。</summary>
    private static void CreateFrameRateSettingsControls(Transform content)
    {
        CreateSettingsSection(content, "帧率限制", string.Empty);
        var options = new string[FrameRateUserSettings.Options.Count];
        for (int index = 0; index < options.Length; index++)
            options[index] = FrameRateUserSettings.Options[index].DisplayName;
        TMP_Dropdown dropdown = CreateSettingsDropdownRow(content, "帧数上限",
            FrameRateSettingsPanelController.DropdownName, options);
        LayoutElement dropdownLayout = dropdown.GetComponent<LayoutElement>();
        dropdownLayout.preferredWidth = 300f;
        dropdownLayout.preferredHeight = 60f;
        dropdownLayout.flexibleWidth = 1f;
        dropdown.transform.parent.GetComponent<LayoutElement>().preferredHeight = 64f;
        dropdown.SetValueWithoutNotify(FrameRateUserSettings.DefaultOptionIndex);
        GameObject row = CreateRow("自定义帧数行", content, 64f);
        CreateRowLabel(row.transform, "自定义帧数", 122f);
        TMP_InputField input = CreateInputField(FrameRateSettingsPanelController.InputName, row.transform, "1～1000");
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.characterLimit = 4;
        input.SetTextWithoutNotify(FrameRateUserSettings.DefaultFrameRate.ToString());
        input.interactable = false;
        LayoutElement inputLayout = input.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
        inputLayout.preferredWidth = 240f;
        inputLayout.preferredHeight = 60f;
        TextMeshProUGUI status = CreateText(FrameRateSettingsPanelController.StatusName, content,
            "当前帧数上限：60 FPS", 16f, Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
        CreateSettingsHint(content, "选择自定义后输入整数，按回车或移开焦点即可应用。", 54f);
        CreateSettingsHint(content, "帧数上限不保证实际帧数；手机还会受到屏幕刷新率限制。", 54f);
    }

    /// <summary>构建主菜单退出确认窗口；取消是默认焦点，确认按钮才真正关闭应用。</summary>
    private static GameObject BuildMainMenuExitConfirmation()
    {
        GameObject root = CreateModalPanelRoot(
            RuntimeUIPrefabKeys.MainMenuExitConfirmation,
            new Vector2(760f, 360f));
        Transform dialog = root.transform.Find("设置对话框");
        dialog.name = "退出确认对话框";
        ConfigureMainMenuModalBackground(root, dialog);

        CreateMainMenuSettingsHeader(
            dialog,
            "退出游戏",
            GameManager.MainMenuExitConfirmationCloseButtonKey);

        TextMeshProUGUI message = CreateText(
            "退出确认提示",
            dialog,
            "确定要退出游戏吗？",
            27f,
            Cream);
        message.alignment = TextAlignmentOptions.Center;
        message.enableWordWrapping = true;
        message.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;

        Transform footer = CreateFooter(dialog);
        footer.GetComponent<LayoutElement>().preferredHeight = 76f;
        SetButtonLabelSize(CreateButton(
            GameManager.MainMenuExitConfirmationCancelButtonKey,
            footer,
            "取消",
            170f,
            64f,
            false), 21f);
        Button confirmButton = CreateButton(
            GameManager.MainMenuExitConfirmationConfirmButtonKey,
            footer,
            "退出游戏",
            190f,
            64f,
            false);
        confirmButton.GetComponent<Image>().color = Danger;
        SetButtonLabelSize(confirmButton, 21f);
        return root;
    }

    /// <summary>主菜单弹窗只保留悬浮窗口本体；全屏根节点仅透明拦截背景输入。</summary>
    private static void ConfigureMainMenuModalBackground(
        GameObject root,
        Transform dialog)
    {
        Image rootBlocker = root.GetComponent<Image>();
        rootBlocker.color = Color.clear;
        rootBlocker.raycastTarget = true;

        Image dialogImage = dialog.GetComponent<Image>();
        dialogImage.color = MainMenuSettingsCanvas;
        dialog.SetAsLastSibling();
    }

    /// <summary>构建带应用与取消的内嵌自动保存草稿页。</summary>
    private static GameObject BuildAutoSaveSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.AutoSaveSettings,
            out Transform content);
        CreateSettingsHeader(content, "自动保存");
        CreateSettingsHint(content, "自动保存只在游戏世界中按现实时间运行，设置会立即保存。", 48f);

        GameObject modeRow = CreateRow("保存模式", content, 72f);
        CreateRowLabel(modeRow.transform, "保存模式", 130f);
        TMP_Dropdown dropdown = CreateDropdown("自动保存间隔下拉列表", modeRow.transform);
        ConfigureTouchSettingsDropdown(dropdown, 402f);

        GameObject inputRow = CreateRow("自定义间隔", content, 72f);
        CreateRowLabel(inputRow.transform, "间隔（分钟）", 130f);
        TMP_InputField input = CreateInputField("自动保存间隔输入框", inputRow.transform, "输入 1–1440");
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        LayoutElement inputLayout = input.gameObject.AddComponent<LayoutElement>();
        inputLayout.preferredWidth = 340f;
        inputLayout.preferredHeight = 64f;
        TextMeshProUGUI range = CreateText("范围提示", inputRow.transform, "1–1440", 15f, Muted);
        range.alignment = TextAlignmentOptions.MidlineRight;
        range.gameObject.AddComponent<LayoutElement>().preferredWidth = 62f;

        TextMeshProUGUI status = CreateText("状态文本", content, "当前设置：每 10 分钟自动保存。", 16f, Teal);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Transform footer = CreateFooter(content);
        footer.GetComponent<LayoutElement>().preferredHeight = 76f;
        SetButtonLabelSize(CreateSettingsButton("取消按钮", footer, "取消", 132f, 60f, false), 18f);
        SetButtonLabelSize(CreateSettingsButton("应用按钮", footer, "应用", 144f, 60f, true), 18f);
        return root;
    }

    /// <summary>构建带应用与取消的内嵌流送性能草稿页。</summary>
    private static GameObject BuildWorldStreamingSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.WorldStreamingSettings,
            out Transform content);
        CreateSettingsHeader(content, "区块流送性能");
        CreateSettingsHint(content,
            "自动适合多数设备；流畅优先减少 CPU 争用；高吞吐会在安全上限内使用多个后台线程。",
            68f);

        GameObject modeRow = CreateRow("性能模式行", content, 54f);
        CreateRowLabel(modeRow.transform, "性能模式", 122f);
        TMP_Dropdown dropdown = CreateDropdown("性能模式下拉列表", modeRow.transform);
        dropdown.gameObject.AddComponent<LayoutElement>().preferredWidth = 430f;

        TextMeshProUGUI explanation = CreateText(
            "模式说明", content,
            string.Empty,
            16f, Muted);
        explanation.gameObject.SetActive(false);

        TextMeshProUGUI status = CreateText(
            "状态文本", content, "当前：自动平衡。", 16f, Teal);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Transform footer = CreateFooter(content);
        CreateSettingsButton("取消按钮", footer, "取消", 104f, 42f, false);
        CreateSettingsButton("应用按钮", footer, "应用", 116f, 42f, true);
        return root;
    }

    /// <summary>构建带应用与取消的内嵌难度草稿页。</summary>
    private static GameObject BuildDifficultySettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.DifficultySettings,
            out Transform content);
        CreateSettingsHeader(content, "游戏难度");

        for (int i = 0; i < GameDifficultyCatalog.All.Count; i++)
        {
            GameDifficultyDefinition definition = GameDifficultyCatalog.All[i];
            CreateDifficultyOption(content, definition, i == 0);
        }

        TextMeshProUGUI status = CreateText("状态文本", content, "当前存档难度：难度 0", 16f, Teal);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        Transform footer = CreateFooter(content);
        CreateSettingsButton("取消按钮", footer, "取消", 104f, 42f, false);
        CreateSettingsButton("应用按钮", footer, "应用", 116f, 42f, true);
        return root;
    }

    /// <summary>构建铺满主内容区的内嵌按键绑定页。</summary>
    private static GameObject BuildInputBindingSettings()
    {
        GameObject root = CreateFixedSettingsPageRoot(RuntimeUIPrefabKeys.InputBindingSettings);
        Transform content = root.transform;

        // 固定区共 182，列表最小 180；操作选项与按键行共用滚动内容，新增设置不挤占窄屏高度。
        VerticalLayoutGroup pageLayout = root.GetComponent<VerticalLayoutGroup>();
        pageLayout.padding = new RectOffset(20, 20, 12, 12);
        pageLayout.spacing = 8f;
        CreateCompactInputBindingHeader(content);

        GameObject deviceTabs = CreateUIObject("设备分页", content);
        deviceTabs.AddComponent<LayoutElement>().preferredHeight = 36f;
        HorizontalLayoutGroup tabLayout = deviceTabs.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 10f;
        tabLayout.childAlignment = TextAnchor.MiddleCenter;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = false;
        tabLayout.childForceExpandHeight = false;
        CreateButton("键鼠分页按钮", deviceTabs.transform, "键鼠", 132f, 34f, true);
        CreateButton("手柄分页按钮", deviceTabs.transform, "手柄", 132f, 34f, false);
        CreateButton("触屏布局按钮", deviceTabs.transform, "触屏布局", 150f, 34f, false);

        Transform bindingContent = CreateBindingScrollView(content);
        CreateGameplayInputOptions(bindingContent);
        CreateCompactInputBindingFooter(content);
        return root;
    }

    /// <summary>通用操作选项放在绑定列表顶部，长页面由同一个滚动区域承载。</summary>
    private static void CreateGameplayInputOptions(Transform parent)
    {
        GameObject options = CreateUIObject("操作选项", parent);
        VerticalLayoutGroup layout = options.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        GameObject preciseRow = CreateRow("精确交互行", options.transform, 60f);
        preciseRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(4, 4, 0, 0);
        TextMeshProUGUI preciseLabel = CreateText(
            "精确交互标签", preciseRow.transform, "精确交互（需光标命中）", 17f, Cream);
        LayoutElement preciseLabelLayout = preciseLabel.gameObject.AddComponent<LayoutElement>();
        preciseLabelLayout.minWidth = 200f;
        preciseLabelLayout.flexibleWidth = 1f;
        Toggle preciseToggle = CreateToggle("精确交互", preciseRow.transform);
        preciseToggle.GetComponent<LayoutElement>().preferredHeight = 60f;
        preciseToggle.isOn = false;

        CreateFlickThresholdRow(options.transform, "甩动转移速度");
        CreateFlickThresholdRow(options.transform, "甩动转移距离");

        GameObject angleRow = CreateRow("甩动方向限制行", options.transform, 60f);
        angleRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(4, 4, 0, 0);
        TextMeshProUGUI angleLabel = CreateText(
            "甩动方向限制标签", angleRow.transform, "甩动方向限制", 17f, Cream);
        LayoutElement angleLabelLayout = angleLabel.gameObject.AddComponent<LayoutElement>();
        angleLabelLayout.minWidth = 200f;
        angleLabelLayout.flexibleWidth = 1f;
        Toggle angleToggle = CreateToggle("甩动方向限制", angleRow.transform);
        angleToggle.GetComponent<LayoutElement>().preferredHeight = 60f;
        angleToggle.isOn = InventoryFlickUserSettings.DefaultAngleLimitEnabled;

        TextMeshProUGUI hint = CreateText(
            "甩动设置说明", options.transform,
            "速度和距离：25%～200%，100% 为默认；数值越低越容易触发。\n开启方向限制时需朝目标容器甩动，关闭后仍按方向优先选择目标。",
            17f, Muted);
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.gameObject.AddComponent<LayoutElement>().minHeight = 52f;
    }

    /// <summary>复用正式滑条样式并显示玩家选择的百分比。</summary>
    private static void CreateFlickThresholdRow(Transform parent, string name)
    {
        GameObject row = CreateRow(name + "行", parent, 60f);
        CreateRowLabel(row.transform, name, 196f);
        Slider slider = CreateSlider(name, row.transform);
        slider.minValue = InventoryFlickUserSettings.MinimumThresholdPercent;
        slider.maxValue = InventoryFlickUserSettings.MaximumThresholdPercent;
        slider.wholeNumbers = true;
        slider.value = InventoryFlickUserSettings.DefaultThresholdPercent;
        slider.GetComponent<LayoutElement>().preferredHeight = 60f;
        TextMeshProUGUI value = CreateText(name + "_数值", row.transform, "100%", 17f, Amber);
        value.alignment = TextAlignmentOptions.MidlineRight;
        value.gameObject.AddComponent<LayoutElement>().preferredWidth = 64f;
    }

    /// <summary>标题、单行说明与控制方式共用一条横向顶栏，避免文字各占一整行。</summary>
    private static void CreateCompactInputBindingHeader(Transform parent)
    {
        GameObject header = CreateUIObject("按键绑定顶部栏", parent, typeof(Image));
        header.AddComponent<LayoutElement>().preferredHeight = 56f;
        Image background = header.GetComponent<Image>();
        background.color = SurfaceRaised;
        background.raycastTarget = false;

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 14, 5, 5);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        // 下拉框保持自身首选高度，不随顶部栏被拉高。
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText("标题文本", header.transform, "按键绑定", 22f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement titleElement = title.gameObject.AddComponent<LayoutElement>();
        titleElement.minWidth = 130f;
        titleElement.preferredWidth = 150f;

        TextMeshProUGUI hint = CreateText(
            "说明文本",
            header.transform,
            "先选择玩法控制方式；键鼠与手柄按键可在下方分别修改。",
            14f,
            Muted);
        hint.enableWordWrapping = false;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement hintElement = hint.gameObject.AddComponent<LayoutElement>();
        hintElement.minWidth = 140f;
        hintElement.preferredWidth = 300f;
        hintElement.flexibleWidth = 1f;

        TextMeshProUGUI modeLabel = CreateText(
            "控制方式标签",
            header.transform,
            "控制方式",
            15f,
            Cream);
        modeLabel.enableWordWrapping = false;
        modeLabel.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement modeLabelElement = modeLabel.gameObject.AddComponent<LayoutElement>();
        modeLabelElement.minWidth = 96f;
        modeLabelElement.preferredWidth = 110f;

        TMP_Dropdown controlModeDropdown = CreateDropdown(
            "控制模式下拉列表",
            header.transform);
        LayoutElement controlModeElement =
            controlModeDropdown.gameObject.AddComponent<LayoutElement>();
        controlModeElement.minWidth = 240f;
        controlModeElement.preferredWidth = 320f;
        controlModeElement.preferredHeight = 42f;
        controlModeElement.flexibleWidth = 1f;
        controlModeDropdown.ClearOptions();
        controlModeDropdown.AddOptions(new List<string>
        {
            "电脑键鼠控制",
            "手柄控制",
            "手机触屏控制"
        });
        controlModeDropdown.value = 0;
        controlModeDropdown.RefreshShownValue();
    }

    /// <summary>状态说明与恢复按钮共用一条横向底栏，状态过长时只做单行省略。</summary>
    private static void CreateCompactInputBindingFooter(Transform parent)
    {
        GameObject footer = CreateUIObject("按键绑定底部栏", parent);
        footer.AddComponent<LayoutElement>().preferredHeight = 42f;
        HorizontalLayoutGroup layout = footer.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI status = CreateText(
            "状态文本",
            footer.transform,
            "选择一项后按下新按键。",
            13f,
            Muted);
        status.enableWordWrapping = false;
        status.overflowMode = TextOverflowModes.Ellipsis;
        status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton("恢复默认按钮", footer.transform, "恢复默认", 112f, 36f, false);
    }

    private static GameObject BuildInputBindingRow()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.InputBindingRow, null, typeof(Image));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(680f, 44f);
        Image image = root.GetComponent<Image>();
        image.color = new Color(0.06f, 0.102f, 0.116f, 0.98f);
        image.raycastTarget = false;
        root.AddComponent<LayoutElement>().preferredHeight = 44f;

        HorizontalLayoutGroup layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 8, 5, 5);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        TextMeshProUGUI label = CreateText("操作名称", root.transform, "向上移动", 15f, Cream);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        TextMeshProUGUI binding = CreateText("绑定值", root.transform, "W", 14f, Amber);
        binding.alignment = TextAlignmentOptions.MidlineRight;
        binding.gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;
        CreateButton("修改按钮", root.transform, "修改", 86f, 34f, true);
        CreateButton("清除按钮", root.transform, "清除", 86f, 34f, false);
        CreateButton("重置按钮", root.transform, "重置", 86f, 34f, false);
        return root;
    }

    #endregion
}
