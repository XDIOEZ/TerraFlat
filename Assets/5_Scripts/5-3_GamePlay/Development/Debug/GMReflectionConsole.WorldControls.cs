using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Localization;
using FlatWorld.AIECS.Gameplay;
using FlatWorld.Gameplay.Events;
using FlatWorld.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 世界时间、视野与区块范围

    /// <summary>用独立滑条控制时间流速、当天时刻、相机视野和区块加载范围。</summary>
    private void CreateWorldRangeControls(Transform parent)
    {
        timeScaleSlider = CreateWorldRangeSlider(
            parent, "时间流速", "时间 流速 加速 减速 恢复 time scale speed",
            out timeScaleValueText, false);
        timeScaleSlider.onValueChanged.AddListener(SetTimeScaleFromSlider);

        dayTimeSlider = CreateWorldRangeSlider(
            parent, "当天时间", "时间 当天 时刻 跳转 白天 夜晚 clock time of day",
            out dayTimeValueText);
        dayTimeSlider.minValue = 0f;
        dayTimeSlider.maxValue = MinutesPerGameDay - 1f;
        dayTimeSlider.onValueChanged.AddListener(SetDayTimeFromSlider);

        cameraViewSlider = CreateWorldRangeSlider(
            parent, "视野缩放", "相机 视野 缩放 无限 view camera zoom",
            out cameraViewValueText);
        cameraViewSlider.onValueChanged.AddListener(SetCameraViewFromSlider);

        chunkLoadDistanceSlider = CreateWorldRangeSlider(
            parent, "区块加载范围", "区块 加载 范围 距离 chunk load distance",
            out chunkLoadDistanceValueText);
        chunkLoadDistanceSlider.minValue = 1f;
        chunkLoadDistanceSlider.maxValue = MaxGmChunkLoadDistance;
        chunkLoadDistanceSlider.onValueChanged.AddListener(SetChunkLoadDistanceFromSlider);
    }

    /// <summary>沿用项目通用滑动条 Prefab，放入世界页可滚动的整行控件。</summary>
    private Slider CreateWorldRangeSlider(
        Transform parent, string label, string keywords, out TextMeshProUGUI valueText,
        bool wholeNumbers = true)
    {
        GameObject row = CreateUiObject(label, parent);
        row.AddComponent<Image>().color = GmSurface;
        StyleGmOutline(row.AddComponent<Outline>());
        row.AddComponent<LayoutElement>().preferredHeight = 56f;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 0, 0);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        TextMeshProUGUI nameText = CreateText(row.transform, label, 15f, GmTextPrimary);
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.enableWordWrapping = false;
        nameText.raycastTarget = false;
        nameText.gameObject.AddComponent<LayoutElement>().preferredWidth = 140f;

        GameObject sliderObject = Instantiate(
            GameRes.ExistingInstance.GetPrefab(RuntimeUIPrefabKeys.SliderControl, false),
            row.transform, false);
        sliderObject.name = label + " Slider";
        LayoutElement sliderLayout = sliderObject.GetComponent<LayoutElement>();
        sliderLayout.minWidth = 80f;
        sliderLayout.preferredWidth = -1f;
        sliderLayout.flexibleWidth = 1f;
        Slider slider = sliderObject.GetComponent<Slider>();
        slider.wholeNumbers = wholeNumbers;
        slider.direction = Slider.Direction.LeftToRight;

        valueText = CreateText(row.transform, "--", 15f, GmTextPrimary);
        valueText.alignment = TextAlignmentOptions.Center;
        valueText.enableWordWrapping = false;
        valueText.raycastTarget = false;
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 94f;
        RegisterSearchEntry(GmPageId.World, label, keywords, (RectTransform)row.transform);
        return slider;
    }

    /// <summary>按 0.1 倍步长设置本地玩家的时间流速。</summary>
    private void SetTimeScaleFromSlider(float value)
    {
        Mod_PlayerAdminController controller = FindLocalPlayerModule<Mod_PlayerAdminController>();
        if (controller == null)
        {
            RefreshWorldRangeControls();
            return;
        }

        controller.TrySetTimeScale(Mathf.Round(value * 10f) / 10f);
        RefreshWorldRangeControls();
    }

    /// <summary>拖动时预览 00:00～23:59，松开指针后才提交跳时。</summary>
    private void SetDayTimeFromSlider(float value)
    {
        pendingDayTimeMinute = Mathf.Clamp(Mathf.RoundToInt(value), 0, MinutesPerGameDay - 1);
        SetLocalizedText(dayTimeValueText, FormatDayTime(pendingDayTimeMinute));
        if (!IsDayTimePointerPressed())
            CommitPendingDayTime();
    }

    /// <summary>通过现有时钟入口跳到当天目标时刻，不更改游戏天数。</summary>
    private void CommitPendingDayTime()
    {
        if (pendingDayTimeMinute < 0)
            return;

        int minute = pendingDayTimeMinute;
        pendingDayTimeMinute = -1;
        if (!GameNetwork.HasStateAuthority ||
            !TryGetActiveDayTime(out DayTimeSystem timeSystem, out string timeSource, out TimeData timeData))
        {
            SetStatus("当前世界时间不可修改；联机时请由房主操作。", Color.yellow);
            RefreshDayTimeControl();
            return;
        }

        float targetTime = timeData.DayLength * (minute / (float)MinutesPerGameDay);
        timeSystem.JumpToTime(timeSource, targetTime);
        RefreshDayTimeControl();
    }

    /// <summary>同时识别鼠标与触摸拖动，键盘改值直接提交。</summary>
    private static bool IsDayTimePointerPressed()
    {
        return Mouse.current?.leftButton.isPressed == true ||
               Touchscreen.current?.primaryTouch.press.isPressed == true;
    }

    /// <summary>场景或面板切换时取消尚未松开的预览值。</summary>
    private void CancelPendingDayTimeJump()
    {
        pendingDayTimeMinute = -1;
    }

    /// <summary>解析当前场景实际引用的世界时钟，避免在引用维度新建独立时间数据。</summary>
    private static bool TryGetActiveDayTime(
        out DayTimeSystem timeSystem, out string timeSource, out TimeData timeData)
    {
        timeSystem = null;
        timeSource = string.Empty;
        timeData = null;
        // 主菜单也会创建 GM 面板，此时世界时钟尚未加载，不应触发必需单例的报错。
        if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
            return false;

        timeSystem = DayTimeSystem.GetInstance();
        return timeSystem != null &&
               timeSystem.TryGetResolvedTimeData(
                   SceneManager.GetActiveScene().name, out timeSource, out timeData) &&
               timeData != null && timeData.DayLength > 0f &&
               !float.IsNaN(timeData.DayLength) && !float.IsInfinity(timeData.DayLength);
    }

    /// <summary>按当前天内进度显示 24 小时制时间。</summary>
    private static string FormatDayTime(int minute)
    {
        return $"{minute / 60:00}:{minute % 60:00}";
    }

    /// <summary>面板停留在世界页时，定期同步仍在流逝的游戏时钟。</summary>
    private void RefreshDayTimeControlIfNeeded()
    {
        if (dayTimeSlider == null || windowRoot == null || !windowRoot.activeSelf ||
            !gmPages.TryGetValue(GmPageId.World, out GmPageView page) ||
            page.Root == null || !page.Root.activeInHierarchy)
        {
            CancelPendingDayTimeJump();
            return;
        }

        if (pendingDayTimeMinute >= 0 && !IsDayTimePointerPressed())
            CommitPendingDayTime();

        if (Time.unscaledTime < nextDayTimeRefreshAt)
            return;

        nextDayTimeRefreshAt = Time.unscaledTime + DayTimeRefreshInterval;
        if (pendingDayTimeMinute >= 0 || IsDayTimePointerPressed())
            return;

        RefreshDayTimeControl();
    }

    /// <summary>从权威时钟刷新滑条；非房主只读，拖动期间由输入回调显示目标时刻。</summary>
    private void RefreshDayTimeControl()
    {
        bool ready = TryGetActiveDayTime(out _, out _, out TimeData timeData);
        dayTimeSlider.interactable = ready && GameNetwork.HasStateAuthority;
        if (!ready)
        {
            dayTimeSlider.SetValueWithoutNotify(0f);
            SetLocalizedText(dayTimeValueText, "--:--");
            return;
        }

        float progress = Mathf.Repeat(timeData.CurrentTime, timeData.DayLength) / timeData.DayLength;
        int minute = Mathf.Clamp(
            Mathf.FloorToInt(progress * MinutesPerGameDay), 0, MinutesPerGameDay - 1);
        dayTimeSlider.SetValueWithoutNotify(minute);
        SetLocalizedText(dayTimeValueText, FormatDayTime(minute));
    }

    /// <summary>最右档解锁无限缩放并立即拉远；离开最右档恢复普通视野上限。</summary>
    private void SetCameraViewFromSlider(float value)
    {
        Mod_Cam cameraModule = FindLocalPlayerModule<Mod_Cam>();
        if (cameraModule == null || !cameraModule.IsCameraReady)
        {
            RefreshWorldRangeControls();
            return;
        }

        bool unlimited = value >= cameraViewSlider.maxValue;
        if (unlimited)
        {
            cameraModule.SetViewLimitAndSize(
                Mathf.Max(cameraModule.CurrentOrthographicSize, cameraModule.MaxPovValue * 5f),
                true,
                MaxGmFiniteCameraView);
        }
        else
        {
            cameraModule.SetViewLimitAndSize(value, false, MaxGmFiniteCameraView);
        }

        RefreshWorldRangeControls();
    }

    /// <summary>区块滑条只改变加载窗口，不修改相机正交尺寸。</summary>
    private void SetChunkLoadDistanceFromSlider(float value)
    {
        Mod_ChunkLoader loader = FindLocalPlayerModule<Mod_ChunkLoader>();
        if (loader == null)
        {
            RefreshWorldRangeControls();
            return;
        }

        loader.SetLoadChunkDistance(Mathf.RoundToInt(value));
        RefreshWorldRangeControls();
    }

    /// <summary>面板打开或切换到世界页时读取时间、相机和区块模块的当前状态。</summary>
    private void RefreshWorldRangeControls()
    {
        RefreshDayTimeControl();

        Player localPlayer = FindLocalPlayer();
        Mod_PlayerAdminController controller = localPlayer != null
            ? localPlayer.GetComponentInChildren<Mod_PlayerAdminController>(true)
            : null;
        timeScaleSlider.onValueChanged.RemoveListener(SetTimeScaleFromSlider);
        timeScaleSlider.interactable = controller != null;
        if (controller != null)
        {
            timeScaleSlider.minValue = controller.minTimeScale;
            timeScaleSlider.maxValue = controller.maxTimeScale;
            timeScaleSlider.SetValueWithoutNotify(controller.timeScale);
            SetLocalizedFormat(timeScaleValueText, "{0:0.0}x", controller.timeScale);
        }
        else
        {
            SetLocalizedText(timeScaleValueText, "--");
        }
        timeScaleSlider.onValueChanged.AddListener(SetTimeScaleFromSlider);

        Mod_Cam cameraModule = localPlayer != null
            ? localPlayer.GetComponentInChildren<Mod_Cam>(true)
            : null;
        cameraViewSlider.onValueChanged.RemoveListener(SetCameraViewFromSlider);
        bool cameraReady = cameraModule != null && cameraModule.IsCameraReady;
        cameraViewSlider.interactable = cameraReady;
        if (cameraReady)
        {
            cameraViewSlider.minValue = cameraModule.MinPovValue;
            cameraViewSlider.maxValue = MaxGmFiniteCameraView + 1f;
            cameraViewSlider.SetValueWithoutNotify(cameraModule.IsUnlimitedViewEnabled
                ? cameraViewSlider.maxValue
                : Mathf.Min(MaxGmFiniteCameraView, Mathf.Round(cameraModule.CurrentOrthographicSize)));
            if (cameraModule.IsUnlimitedViewEnabled)
                SetLocalizedText(cameraViewValueText, "无限");
            else
                SetLocalizedFormat(cameraViewValueText, "{0:0}", cameraModule.CurrentOrthographicSize);
        }
        else
        {
            SetLocalizedText(cameraViewValueText, "--");
        }
        cameraViewSlider.onValueChanged.AddListener(SetCameraViewFromSlider);

        Mod_ChunkLoader loader = localPlayer != null
            ? localPlayer.GetComponentInChildren<Mod_ChunkLoader>(true)
            : null;
        chunkLoadDistanceSlider.onValueChanged.RemoveListener(SetChunkLoadDistanceFromSlider);
        chunkLoadDistanceSlider.interactable = loader != null;
        if (loader != null)
        {
            chunkLoadDistanceSlider.maxValue = Mathf.Max(
                MaxGmChunkLoadDistance, loader.CurrentLoadChunkDistance);
            chunkLoadDistanceSlider.SetValueWithoutNotify(loader.CurrentLoadChunkDistance);
            SetLocalizedFormat(chunkLoadDistanceValueText, "{0} 区块", loader.CurrentLoadChunkDistance);
        }
        else
        {
            SetLocalizedText(chunkLoadDistanceValueText, "--");
        }
        chunkLoadDistanceSlider.onValueChanged.AddListener(SetChunkLoadDistanceFromSlider);
    }

    /// <summary>世界页只控制本机玩家，不把滑条绑定到联机远程玩家副本。</summary>
    private static T FindLocalPlayerModule<T>() where T : Component
    {
        Player localPlayer = FindLocalPlayer();
        return localPlayer != null ? localPlayer.GetComponentInChildren<T>(true) : null;
    }

    private static Player FindLocalPlayer()
    {
        return FindObjectsOfType<Player>(true)
            .FirstOrDefault(player => player.IsLocalProfile);
    }

    #endregion

    #region 区块流送预算

    /// <summary>开发人员可分别调整结果提交、基础地形和后续表现的数量与耗时上限。</summary>
    private void CreateChunkStreamingBudgetControls(Transform parent)
    {
        AddPageIntro(parent, "区块每帧预算",
            "先提交生成结果，再显示地面和补齐细节。每项同时受数量与毫秒限制；单个区块会完整执行。数量滑条按指数增长，最高 1024。");

        string[] labels =
        {
            "提交数量/帧", "提交耗时/帧", "地面数量/帧", "地面耗时/帧",
            "细节步骤/帧", "细节耗时/帧"
        };
        string[] keywords =
        {
            "区块 生成结果 提交 数量 commit count",
            "区块 生成结果 提交 毫秒 时间 commit ms",
            "区块 基础地形 显示 数量 presentation start count",
            "区块 基础地形 显示 毫秒 presentation start ms",
            "区块 草地 自然物 导航 细节 步骤 continuation count",
            "区块 草地 自然物 导航 细节 毫秒 continuation ms"
        };
        for (int i = 0; i < chunkStreamingBudgetSliders.Length; i++)
        {
            int slot = i;
            Slider slider = CreateWorldRangeSlider(parent, labels[i], keywords[i],
                out chunkStreamingBudgetValues[i], false);
            slider.minValue = i % 2 == 0 ? 0f : ChunkMgr.MinRuntimeChunkWorkMillisecondsPerFrame;
            slider.maxValue = i % 2 == 0 ? 10f : ChunkMgr.MaxRuntimeChunkWorkMillisecondsPerFrame;
            slider.onValueChanged.AddListener(value => SetChunkStreamingBudgetFromSlider(slot, value));
            chunkStreamingBudgetSliders[i] = slider;

            EventTrigger trigger = slider.GetComponent<EventTrigger>() ??
                                   slider.gameObject.AddComponent<EventTrigger>();
            foreach (EventTriggerType eventType in new[]
                         { EventTriggerType.PointerUp, EventTriggerType.Deselect })
            {
                EventTrigger.Entry entry = new() { eventID = eventType };
                entry.callback.AddListener(_ => GMConsolePreferences.SavePendingChanges());
                trigger.triggers.Add(entry);
            }
        }

        chunkStreamingStatusText = AddPageHint(parent, "生成排队 -- | 生成中 -- | 待提交 -- | 待显示 --", 32f);
    }

    /// <summary>滑动时即时写入当前 ChunkMgr，数量轴用 2 的幂覆盖 1～1024。</summary>
    private void SetChunkStreamingBudgetFromSlider(int slot, float value)
    {
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null)
        {
            RefreshChunkStreamingBudgetControls();
            return;
        }

        int count = Mathf.Clamp(Mathf.RoundToInt(Mathf.Pow(2f, value)),
            1, ChunkMgr.MaxRuntimeChunkWorkCountPerFrame);
        float milliseconds = Mathf.Round(value * 2f) / 2f;
        switch (slot)
        {
            case 0: chunkManager.RuntimeChunkCommitBudget = count; break;
            case 1: chunkManager.RuntimeChunkCommitMillisecondsBudget = milliseconds; break;
            case 2: chunkManager.RuntimeChunkPresentationStartBudget = count; break;
            case 3: chunkManager.RuntimeChunkPresentationStartMillisecondsBudget = milliseconds; break;
            case 4: chunkManager.RuntimeChunkPresentationContinuationBudget = count; break;
            case 5: chunkManager.RuntimeChunkPresentationContinuationMillisecondsBudget = milliseconds; break;
        }

        GMConsolePreferences.SetChunkStreamingBudgets(chunkManager);
        RefreshChunkStreamingBudgetControls();
    }

    /// <summary>从运行中管理器读取真实预算，切换世界和重新打开 GM 时不会显示旧滑条值。</summary>
    private void RefreshChunkStreamingBudgetControls()
    {
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkStreamingBudgetSliders[0] == null)
            return;

        for (int i = 0; i < chunkStreamingBudgetSliders.Length; i++)
        {
            Slider slider = chunkStreamingBudgetSliders[i];
            slider.interactable = chunkManager != null;
            if (chunkManager == null)
            {
                SetLocalizedText(chunkStreamingBudgetValues[i], "--");
                continue;
            }

            float budget = i switch
            {
                0 => chunkManager.RuntimeChunkCommitBudget,
                1 => chunkManager.RuntimeChunkCommitMillisecondsBudget,
                2 => chunkManager.RuntimeChunkPresentationStartBudget,
                3 => chunkManager.RuntimeChunkPresentationStartMillisecondsBudget,
                4 => chunkManager.RuntimeChunkPresentationContinuationBudget,
                _ => chunkManager.RuntimeChunkPresentationContinuationMillisecondsBudget
            };
            bool isCount = i % 2 == 0;
            slider.SetValueWithoutNotify(isCount ? Mathf.Log(budget, 2f) : budget);
            if (isCount)
                SetLocalizedFormat(chunkStreamingBudgetValues[i], "{0:0}", budget);
            else
                SetLocalizedFormat(chunkStreamingBudgetValues[i], "{0:0.0} ms", budget);
        }
        RefreshChunkStreamingStatus();
    }

    /// <summary>只在世界页打开时刷新队列数，方便判断该调哪一段预算。</summary>
    private void RefreshChunkStreamingStatusIfNeeded()
    {
        if (windowRoot == null || !windowRoot.activeSelf || activeGmPage != GmPageId.World ||
            !IsPageSectionInView(GmPageId.World) || Time.unscaledTime < nextChunkStreamingStatusRefreshAt)
            return;

        nextChunkStreamingStatusRefreshAt = Time.unscaledTime + 0.4f;
        RefreshChunkStreamingStatus();
    }

    private void RefreshChunkStreamingStatus()
    {
        if (chunkStreamingStatusText == null)
            return;

        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager?.RuntimeChunks == null)
        {
            SetLocalizedText(chunkStreamingStatusText, "生成排队 -- | 生成中 -- | 待提交 -- | 待显示 --");
            return;
        }

        SetLocalizedFormat(
            chunkStreamingStatusText,
            "生成排队 {0} | 生成中 {1} | 待提交 {2} | 待显示 {3}",
            chunkManager.RuntimeChunks.QueuedGenerationCount,
            chunkManager.RuntimeChunks.ActiveGenerationCount,
            chunkManager.RuntimeChunks.PendingCommitCount,
            chunkManager.PendingRuntimeChunkPresentationCount);
    }

    #endregion

}
