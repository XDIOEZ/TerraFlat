using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region Buff 目录与目标列表

    private enum BuffTargetingMode
    {
        None,
        Apply,
        Clear
    }

    private const int MaxBuffApplicationsPerClick = 99;
    private const float BuffTargetRefreshInterval = 0.5f;

    /// <summary>运行时 Buff 目标列表中的一个索引按钮与其对应组件。</summary>
    private sealed class BuffTargetEntry
    {
        public BuffManager Manager;
        public Button Button;
    }

    private readonly List<BuffDefinition> availableBuffDefinitions = new();
    private readonly List<BuffManager> availableBuffTargets = new();
    private readonly List<BuffTargetEntry> buffTargetEntries = new();

    private TMP_InputField buffIdInput;
    private TMP_InputField buffDurationInput;
    private TMP_InputField buffApplicationCountInput;
    private TextMeshProUGUI buffDefinitionHintText;
    private TextMeshProUGUI buffTargetListSummaryText;
    private TextMeshProUGUI buffTargetingHintText;
    private Button buffApplyButton;
    private Button buffCancelButton;
    private Button buffClearButton;
    private Transform buffTargetListContent;
    private ScrollRect buffTargetListScroll;
    private int selectedBuffDefinitionIndex = -1;
    private BuffTargetingMode buffTargetingMode;
    private string pendingBuffId;
    private float? pendingBuffDurationSeconds;
    private int pendingBuffApplicationCount = 1;
    private float nextBuffTargetRefreshTime;

    #endregion

    private void BuildBuffPage()
    {
        GmPageView page = CreatePage(GmPageId.Buff);
        AddPageIntro(
            page.Content,
            "Buff 分发",
            "选择已加载的 Buff，设置限时 Buff 的持续时间与施加次数；然后点击下方目标列表中的索引按钮施加。 ");

        GameObject selectionRow = CreateUiObject("Buff Definition Row", page.Content);
        selectionRow.AddComponent<LayoutElement>().preferredHeight = 40f;
        HorizontalLayoutGroup selectionLayout = selectionRow.AddComponent<HorizontalLayoutGroup>();
        selectionLayout.spacing = 8f;
        selectionLayout.childAlignment = TextAnchor.MiddleLeft;
        selectionLayout.childControlWidth = true;
        selectionLayout.childControlHeight = true;
        selectionLayout.childForceExpandWidth = false;

        CreateBuffFieldLabel(selectionRow.transform, "Buff ID", 58f);
        buffIdInput = CreateInputField(selectionRow.transform, "输入已注册的 Buff ID", 260f, false);
        LayoutElement idLayout = buffIdInput.GetComponent<LayoutElement>();
        idLayout.minWidth = 200f;
        idLayout.flexibleWidth = 1f;
        buffIdInput.onValueChanged.AddListener(_ => RefreshBuffDefinitionPreview());
        CreateButton(selectionRow.transform, "‹", () => CycleBuffDefinition(-1), 38f, 34f);
        CreateButton(selectionRow.transform, "›", () => CycleBuffDefinition(1), 38f, 34f);
        CreateButton(selectionRow.transform, "刷新目录", RefreshBuffDefinitions, 88f, 34f);

        GameObject parametersRow = CreateUiObject("Buff Parameters Row", page.Content);
        parametersRow.AddComponent<LayoutElement>().preferredHeight = 40f;
        HorizontalLayoutGroup parametersLayout = parametersRow.AddComponent<HorizontalLayoutGroup>();
        parametersLayout.spacing = 8f;
        parametersLayout.childAlignment = TextAnchor.MiddleLeft;
        parametersLayout.childControlWidth = true;
        parametersLayout.childControlHeight = true;
        parametersLayout.childForceExpandWidth = false;

        CreateBuffFieldLabel(parametersRow.transform, "持续时间（秒）", 92f);
        buffDurationInput = CreateInputField(parametersRow.transform, "留空使用 JSON 默认", 170f, false);
        CreateBuffFieldLabel(parametersRow.transform, "施加次数", 58f);
        buffApplicationCountInput = CreateInputField(parametersRow.transform, "1", 72f, true);
        buffApplicationCountInput.text = "1";
        TextMeshProUGUI parameterHint = CreateText(
            parametersRow.transform,
            "永久 Buff 固定为永久；次数遵循 Buff 自身叠加规则。",
            11f,
            new Color(0.62f, 0.69f, 0.70f));
        parameterHint.enableWordWrapping = false;
        parameterHint.overflowMode = TextOverflowModes.Ellipsis;
        parameterHint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        buffDefinitionHintText = AddPageHint(page.Content, "正在读取 Buff 目录…", 52f);
        BuildBuffTargetList(page.Content);

        Transform actionGrid = CreateActionGrid(page.Content, 3, 338f, 40f, 3);
        buffApplyButton = CreateSearchableButton(
            actionGrid,
            GmPageId.Buff,
            "确认并点选施加",
            "Buff 添加 分发 施加 目标 左键",
            BeginBuffApplyTargeting,
            40f);
        buffCancelButton = CreateSearchableButton(
            actionGrid,
            GmPageId.Buff,
            "取消",
            "Buff 取消 停止 目标列表 分发",
            CancelBuffTargeting,
            40f);
        buffClearButton = CreateSearchableButton(
            actionGrid,
            GmPageId.Buff,
            "清除 Buff",
            "Buff 清除 清空 目标列表 索引",
            ToggleClearBuffTargeting,
            40f);

        buffTargetingHintText = AddPageHint(
            page.Content,
            "当前未启用 Buff 点选操作。",
            34f);

        RefreshBuffDefinitions();
        RefreshBuffTargetList();
        RefreshBuffTargetingControls();
    }

    /// <summary>创建当前加载场景中可接受 Buff 的对象滚动列表。</summary>
    private void BuildBuffTargetList(Transform parent)
    {
        GameObject header = CreateUiObject("Buff Target List Header", parent);
        header.AddComponent<LayoutElement>().preferredHeight = 30f;

        HorizontalLayoutGroup headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;

        TextMeshProUGUI title = CreateText(
            header.transform,
            "可接受 Buff 的运行对象（点击索引）",
            13f,
            new Color(0.82f, 0.82f, 0.78f));
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        buffTargetListSummaryText = CreateText(
            header.transform,
            "正在读取目标…",
            11f,
            new Color(0.62f, 0.69f, 0.70f));
        buffTargetListSummaryText.alignment = TextAlignmentOptions.Right;
        buffTargetListSummaryText.enableWordWrapping = false;
        buffTargetListSummaryText.overflowMode = TextOverflowModes.Ellipsis;
        buffTargetListSummaryText.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;

        CreateButton(header.transform, "刷新目标", () => RefreshBuffTargetList(), 84f, 28f);

        GameObject listRoot = CreateUiObject("Buff Target List", parent);
        listRoot.AddComponent<Image>().color = new Color(0.028f, 0.071f, 0.094f, 1f);
        Outline outline = listRoot.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);
        listRoot.AddComponent<LayoutElement>().preferredHeight = 206f;
        buffTargetListContent = ConfigureBuffTargetScroll(listRoot, 7f, out buffTargetListScroll);
    }

    /// <summary>配置带遮罩和滚动条的 Buff 目标列表。</summary>
    private static Transform ConfigureBuffTargetScroll(
        GameObject root,
        float inset,
        out ScrollRect scroll)
    {
        scroll = root.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        GameObject viewport = CreateUiObject("Viewport", root.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(inset, inset);
        viewportRect.offsetMax = new Vector2(-inset - 18f, -inset);
        viewport.AddComponent<RectMask2D>();
        scroll.viewport = viewportRect;

        GameObject content = CreateUiObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(0, 0, 0, 4);
        contentLayout.spacing = 6f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;

        GameObject scrollbarObject = CreateUiObject("Scrollbar", root.transform);
        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-14f, inset);
        scrollbarRect.offsetMax = new Vector2(-4f, -inset);
        Image scrollbarBackground = scrollbarObject.AddComponent<Image>();
        scrollbarBackground.color = new Color(0.08f, 0.13f, 0.15f, 1f);

        GameObject slidingArea = CreateUiObject("Sliding Area", scrollbarObject.transform);
        RectTransform slidingRect = slidingArea.GetComponent<RectTransform>();
        slidingRect.anchorMin = Vector2.zero;
        slidingRect.anchorMax = Vector2.one;
        slidingRect.offsetMin = new Vector2(2f, 2f);
        slidingRect.offsetMax = new Vector2(-2f, -2f);

        GameObject handle = CreateUiObject("Handle", slidingArea.transform);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = new Color(0.42f, 0.54f, 0.56f, 1f);

        Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return content.transform;
    }

    private static TextMeshProUGUI CreateBuffFieldLabel(Transform parent, string label, float width)
    {
        TextMeshProUGUI text = CreateText(parent, label, 13f, new Color(0.82f, 0.82f, 0.78f));
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
        return text;
    }

    private void RefreshBuffDefinitions()
    {
        string requestedId = buffIdInput != null ? buffIdInput.text?.Trim() : string.Empty;
        availableBuffDefinitions.Clear();

        GameRes gameRes = GameRes.Instance;
        if (gameRes != null)
        {
            foreach (BuffDefinition definition in gameRes.BuffDefinitions.Values)
            {
                if (definition != null && !string.IsNullOrWhiteSpace(definition.Id))
                    availableBuffDefinitions.Add(definition);
            }
        }

        availableBuffDefinitions.Sort(CompareBuffDefinitions);
        selectedBuffDefinitionIndex = FindBuffDefinitionIndex(requestedId);

        if (string.IsNullOrWhiteSpace(requestedId) && availableBuffDefinitions.Count > 0)
        {
            selectedBuffDefinitionIndex = 0;
            buffIdInput?.SetTextWithoutNotify(availableBuffDefinitions[0].Id);
        }
        else if (selectedBuffDefinitionIndex < 0 && availableBuffDefinitions.Count > 0)
        {
            selectedBuffDefinitionIndex = 0;
        }

        RefreshBuffDefinitionPreview();
    }

    private static int CompareBuffDefinitions(BuffDefinition left, BuffDefinition right)
    {
        string leftName = string.IsNullOrWhiteSpace(left?.DisplayName) ? left?.Id : left.DisplayName;
        string rightName = string.IsNullOrWhiteSpace(right?.DisplayName) ? right?.Id : right.DisplayName;
        int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(leftName, rightName);
        return nameComparison != 0
            ? nameComparison
            : StringComparer.OrdinalIgnoreCase.Compare(left?.Id, right?.Id);
    }

    private int FindBuffDefinitionIndex(string buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
            return -1;

        for (int i = 0; i < availableBuffDefinitions.Count; i++)
        {
            if (string.Equals(availableBuffDefinitions[i].Id, buffId.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void CycleBuffDefinition(int delta)
    {
        RefreshBuffDefinitions();
        if (availableBuffDefinitions.Count == 0)
        {
            SetStatus("当前没有已加载的 Buff 定义。", Color.yellow);
            return;
        }

        int currentIndex = FindBuffDefinitionIndex(buffIdInput?.text);
        if (currentIndex < 0)
            currentIndex = selectedBuffDefinitionIndex >= 0 ? selectedBuffDefinitionIndex : 0;

        selectedBuffDefinitionIndex = (currentIndex + delta) % availableBuffDefinitions.Count;
        if (selectedBuffDefinitionIndex < 0)
            selectedBuffDefinitionIndex += availableBuffDefinitions.Count;

        buffIdInput.SetTextWithoutNotify(availableBuffDefinitions[selectedBuffDefinitionIndex].Id);
        RefreshBuffDefinitionPreview();
    }

    private void RefreshBuffDefinitionPreview()
    {
        if (buffDefinitionHintText == null)
            return;

        string buffId = buffIdInput?.text?.Trim();
        BuffDefinition definition = GameRes.Instance?.GetBuffDefinition(buffId);
        if (definition == null)
        {
            buffDefinitionHintText.text = availableBuffDefinitions.Count == 0
                ? "尚未加载 Buff 目录。请进入游戏世界后点击“刷新目录”。"
                : "未找到该 Buff ID。可直接输入已注册的 MOD Buff ID，或用左右按钮浏览目录。";
            buffDefinitionHintText.color = Color.yellow;
            return;
        }

        string duration = definition.IsPermanent
            ? "永久"
            : $"{definition.DurationSeconds.GetValueOrDefault():0.##} 秒";
        string stackMode = definition.StackMode switch
        {
            BuffStackMode.ExtendDuration => "延长持续时间",
            BuffStackMode.RefreshDuration => "刷新持续时间",
            _ => "忽略重复施加"
        };
        string description = string.IsNullOrWhiteSpace(definition.Description)
            ? "无 Buff 说明。"
            : definition.Description;
        buffDefinitionHintText.text =
            $"<b>{definition.DisplayName}</b>  ({definition.Id}) · 持续 {duration} · {stackMode} · 效果 {definition.Effects.Count} 个\n{description}";
        buffDefinitionHintText.color = new Color(0.66f, 0.71f, 0.71f);
    }

    /// <summary>窗口打开期间定时同步当前加载场景中的 Buff 接收对象。</summary>
    private void UpdateBuffTargetListIfNeeded()
    {
        if (buffTargetListContent == null || !buffTargetListContent.gameObject.activeInHierarchy)
            return;

        if (Time.unscaledTime < nextBuffTargetRefreshTime)
            return;

        nextBuffTargetRefreshTime = Time.unscaledTime + BuffTargetRefreshInterval;
        RefreshBuffTargetList(false);
    }

    /// <summary>重新扫描并刷新可接受 Buff 的目标索引按钮。</summary>
    private void RefreshBuffTargetList(bool forceRebuild = true)
    {
        List<BuffManager> discoveredTargets = FindBuffManagersInLoadedScenes();
        bool targetsChanged = forceRebuild || availableBuffTargets.Count != discoveredTargets.Count;

        if (!targetsChanged)
        {
            for (int i = 0; i < discoveredTargets.Count; i++)
            {
                if (availableBuffTargets[i] != discoveredTargets[i])
                {
                    targetsChanged = true;
                    break;
                }
            }
        }

        if (targetsChanged && buffTargetListContent != null)
        {
            availableBuffTargets.Clear();
            availableBuffTargets.AddRange(discoveredTargets);
            buffTargetEntries.Clear();
            ClearChildren(buffTargetListContent);

            if (availableBuffTargets.Count == 0)
            {
                AddPageHint(
                    buffTargetListContent,
                    "当前加载场景没有运行中的 BuffManager 目标。",
                    34f);
            }
            else
            {
                for (int i = 0; i < availableBuffTargets.Count; i++)
                {
                    int targetIndex = i;
                    BuffManager target = availableBuffTargets[i];
                    Button button = CreateButton(
                        buffTargetListContent,
                        FormatBuffTargetLabel(targetIndex, target),
                        () => HandleBuffTargetButtonClicked(targetIndex),
                        0f,
                        34f);
                    buffTargetEntries.Add(new BuffTargetEntry
                    {
                        Manager = target,
                        Button = button
                    });
                }
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(buffTargetListContent as RectTransform);
            if (buffTargetListScroll != null)
                buffTargetListScroll.verticalNormalizedPosition = 1f;
        }
        else if (targetsChanged)
        {
            availableBuffTargets.Clear();
            availableBuffTargets.AddRange(discoveredTargets);
        }

        for (int i = 0; i < buffTargetEntries.Count; i++)
        {
            BuffTargetEntry entry = buffTargetEntries[i];
            if (entry?.Button != null && entry.Manager != null)
                SetBuffTargetButtonLabel(entry.Button, i, entry.Manager);
        }

        if (buffTargetListSummaryText != null)
        {
            buffTargetListSummaryText.text = availableBuffTargets.Count > 0
                ? $"当前目标：{availableBuffTargets.Count} 个"
                : "当前目标：0 个";
        }

        nextBuffTargetRefreshTime = Time.unscaledTime + BuffTargetRefreshInterval;
    }

    /// <summary>扫描所有已加载场景，只保留激活且可运行的 BuffManager。</summary>
    private static List<BuffManager> FindBuffManagersInLoadedScenes()
    {
        BuffManager[] managers = FindObjectsOfType<BuffManager>(true);
        List<BuffManager> targets = new(managers.Length);
        for (int i = 0; i < managers.Length; i++)
        {
            BuffManager manager = managers[i];
            if (manager == null || !manager.isActiveAndEnabled || !manager.gameObject.scene.IsValid())
                continue;

            targets.Add(manager);
        }

        targets.Sort(CompareBuffManagers);
        return targets;
    }

    private static int CompareBuffManagers(BuffManager left, BuffManager right)
    {
        int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(
            GetBuffTargetName(left),
            GetBuffTargetName(right));
        return nameComparison != 0
            ? nameComparison
            : left.GetInstanceID().CompareTo(right.GetInstanceID());
    }

    private static string FormatBuffTargetLabel(int index, BuffManager manager)
    {
        int activeBuffCount = manager?.ActiveBuffs?.Count ?? 0;
        return $"[{index}] {GetBuffTargetName(manager)}  ·  当前 Buff {activeBuffCount} 个";
    }

    private static void SetBuffTargetButtonLabel(Button button, int index, BuffManager manager)
    {
        TextMeshProUGUI text = button != null
            ? button.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        if (text != null)
            text.text = FormatBuffTargetLabel(index, manager);
    }

    /// <summary>点击目标索引后按当前模式施加或清除 Buff。</summary>
    private void HandleBuffTargetButtonClicked(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= availableBuffTargets.Count)
        {
            RefreshBuffTargetList();
            SetStatus("Buff 目标列表已更新，请重新点击目标索引。", Color.yellow);
            return;
        }

        BuffManager target = availableBuffTargets[targetIndex];
        if (target == null || !target.isActiveAndEnabled || !target.gameObject.scene.IsValid())
        {
            RefreshBuffTargetList();
            SetStatus("该 Buff 目标已失效，目标列表已刷新。", Color.yellow);
            return;
        }

        if (buffTargetingMode == BuffTargetingMode.Clear)
        {
            ClearBuffsFromTarget(target);
            return;
        }

        if (buffTargetingMode == BuffTargetingMode.None)
        {
            if (!TryPreparePendingBuffApplication())
                return;

            StartBuffTargeting(BuffTargetingMode.Apply);
            ApplyBuffToTarget(target);
            StopBuffTargeting();
            return;
        }

        ApplyBuffToTarget(target);
    }

    private bool TryPreparePendingBuffApplication()
    {
        string buffId = buffIdInput?.text?.Trim();
        BuffDefinition definition = GameRes.Instance?.GetBuffDefinition(buffId);
        if (definition == null)
        {
            SetStatus("请先选择一个已加载的 Buff。", Color.yellow);
            return false;
        }

        if (!TryReadBuffDurationOverride(out float? durationOverride, out string durationError))
        {
            SetStatus(durationError, Color.yellow);
            return false;
        }

        if (durationOverride.HasValue && definition.IsPermanent)
        {
            SetStatus("永久 Buff 不能覆盖持续时间；请清空持续时间输入框后再确认。", Color.yellow);
            return false;
        }

        if (!TryReadBuffApplicationCount(out int applicationCount, out string countError))
        {
            SetStatus(countError, Color.yellow);
            return false;
        }

        pendingBuffId = definition.Id;
        pendingBuffDurationSeconds = durationOverride;
        pendingBuffApplicationCount = applicationCount;
        return true;
    }

    private void BeginBuffApplyTargeting()
    {
        if (!TryPreparePendingBuffApplication())
            return;

        StartBuffTargeting(BuffTargetingMode.Apply);
    }

    private bool TryReadBuffDurationOverride(out float? durationOverride, out string error)
    {
        durationOverride = null;
        error = null;
        string value = buffDurationInput?.text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!TryParseFiniteFloat(value, out float seconds) || seconds <= 0f)
        {
            error = "持续时间必须是大于 0 的数字，或留空以使用 Buff JSON 的默认值。";
            return false;
        }

        durationOverride = seconds;
        return true;
    }

    private bool TryReadBuffApplicationCount(out int count, out string error)
    {
        count = 1;
        error = null;
        string value = buffApplicationCountInput?.text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return true;

        bool parsed = int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out count) ||
                      int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
        if (!parsed || count < 1 || count > MaxBuffApplicationsPerClick)
        {
            error = $"施加次数必须是 1 到 {MaxBuffApplicationsPerClick} 的整数。";
            return false;
        }

        return true;
    }

    private static bool TryParseFiniteFloat(string value, out float result)
    {
        bool parsed = float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result) ||
                      float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        return parsed && !float.IsNaN(result) && !float.IsInfinity(result);
    }

    private void ToggleClearBuffTargeting()
    {
        if (buffTargetingMode == BuffTargetingMode.Clear)
        {
            StopBuffTargeting("清除 Buff 目标操作已关闭。");
            return;
        }

        pendingBuffId = null;
        pendingBuffDurationSeconds = null;
        pendingBuffApplicationCount = 1;
        StartBuffTargeting(BuffTargetingMode.Clear);
    }

    private void CancelBuffTargeting()
    {
        if (buffTargetingMode == BuffTargetingMode.None)
        {
            SetStatus("当前没有进行中的 Buff 点选操作。", new Color(0.66f, 0.71f, 0.71f));
            return;
        }

        StopBuffTargeting("Buff 目标操作已取消。");
    }

    private void StartBuffTargeting(BuffTargetingMode mode)
    {
        buffTargetingMode = mode;
        RefreshBuffTargetingControls();
        if (mode == BuffTargetingMode.Apply)
        {
            string durationText = pendingBuffDurationSeconds.HasValue
                ? $"，持续 {pendingBuffDurationSeconds.Value:0.##} 秒"
                : string.Empty;
            SetStatus(
                $"已进入 Buff 分发模式：{pendingBuffId} ×{pendingBuffApplicationCount}{durationText}。请点击下方目标索引；点击“取消”可关闭。",
                new Color(0.35f, 0.95f, 0.85f));
        }
        else
        {
            SetStatus(
                "已进入清除 Buff 模式：请点击下方目标索引；再次点击“清除 Buff”可退出。",
                new Color(1f, 0.71f, 0.30f));
        }
    }

    private void StopBuffTargeting(string statusMessage = null)
    {
        buffTargetingMode = BuffTargetingMode.None;
        pendingBuffId = null;
        pendingBuffDurationSeconds = null;
        pendingBuffApplicationCount = 1;
        RefreshBuffTargetingControls();

        if (!string.IsNullOrWhiteSpace(statusMessage))
            SetStatus(statusMessage, new Color(0.66f, 0.71f, 0.71f));
    }

    private void HandleBuffTargetingSceneChanged()
    {
        if (buffTargetingMode != BuffTargetingMode.None)
            StopBuffTargeting("场景已切换，Buff 目标操作已自动取消。");

        RefreshBuffDefinitions();
        RefreshBuffTargetList();
    }

    private void DisposeBuffTargeting()
    {
        buffTargetingMode = BuffTargetingMode.None;
        pendingBuffId = null;
        pendingBuffDurationSeconds = null;
        pendingBuffApplicationCount = 1;
    }

    private void ClearBuffsFromTarget(BuffManager target)
    {
        string targetName = GetBuffTargetName(target);
        int clearedCount = target.ActiveBuffs?.Count ?? 0;
        target.ClearAllBuffs();
        SetStatus(
            clearedCount > 0
                ? $"已清除 {targetName} 身上的 {clearedCount} 个 Buff。"
                : $"{targetName} 当前没有可清除的 Buff。",
            new Color(1f, 0.71f, 0.30f));
    }

    private void ApplyBuffToTarget(BuffManager target)
    {
        string targetName = GetBuffTargetName(target);
        if (string.IsNullOrWhiteSpace(pendingBuffId))
        {
            StopBuffTargeting("Buff 分发参数已失效，操作已关闭。");
            return;
        }

        int appliedCount = 0;
        for (int i = 0; i < pendingBuffApplicationCount; i++)
        {
            if (target.AddBuff(pendingBuffId))
                appliedCount++;
        }

        if (appliedCount == 0)
        {
            SetStatus($"未能向 {targetName} 施加 Buff：{pendingBuffId}。", Color.yellow);
            return;
        }

        if (pendingBuffDurationSeconds.HasValue &&
            !target.TrySetBuffDuration(pendingBuffId, pendingBuffDurationSeconds.Value))
        {
            SetStatus(
                $"已向 {targetName} 施加 {pendingBuffId}，但无法覆盖持续时间。",
                Color.yellow);
            return;
        }

        string durationText = pendingBuffDurationSeconds.HasValue
            ? $"，持续 {pendingBuffDurationSeconds.Value:0.##} 秒"
            : string.Empty;
        SetStatus(
            $"已向 {targetName} 施加 {pendingBuffId} ×{appliedCount}{durationText}。",
            new Color(0.35f, 0.95f, 0.85f));
    }

    private static string GetBuffTargetName(BuffManager manager)
    {
        if (manager == null)
            return "未知目标";

        Item item = manager.GetComponentInParent<Item>();
        if (item != null && !string.IsNullOrWhiteSpace(item.name))
            return item.name;

        return manager.transform.root != null ? manager.transform.root.name : manager.name;
    }

    private void RefreshBuffTargetingControls()
    {
        bool applying = buffTargetingMode == BuffTargetingMode.Apply;
        bool clearing = buffTargetingMode == BuffTargetingMode.Clear;

        SetBuffButtonPresentation(
            buffApplyButton,
            applying ? "批量分发中" : "批量分发",
            applying ? new Color(0.18f, 0.48f, 0.33f, 1f) : new Color(0.66f, 0.32f, 0.15f, 1f));
        SetBuffButtonPresentation(
            buffCancelButton,
            buffTargetingMode == BuffTargetingMode.None ? "取消" : "取消分发操作",
            new Color(0.094f, 0.212f, 0.251f, 1f));
        SetBuffButtonPresentation(
            buffClearButton,
            clearing ? "停止清除 Buff" : "清除 Buff",
            clearing ? new Color(0.55f, 0.22f, 0.12f, 1f) : new Color(0.42f, 0.16f, 0.14f, 1f));

        if (buffTargetingHintText == null)
            return;

        if (applying)
        {
            buffTargetingHintText.text =
                $"分发模式已开启：{pendingBuffId} ×{pendingBuffApplicationCount}。点击下方目标索引可连续施加。";
            buffTargetingHintText.color = new Color(0.35f, 0.95f, 0.85f);
        }
        else if (clearing)
        {
            buffTargetingHintText.text =
                "清除模式已开启：点击下方目标索引会清除其全部 Buff；再次点击“清除 Buff”即可退出。";
            buffTargetingHintText.color = new Color(1f, 0.71f, 0.30f);
        }
        else
        {
            buffTargetingHintText.text =
                "选择 Buff 后可直接点击下方目标索引；需要连续施加时点击“批量分发”。";
            buffTargetingHintText.color = new Color(0.66f, 0.71f, 0.71f);
        }
    }

    private static void SetBuffButtonPresentation(Button button, string label, Color color)
    {
        if (button == null)
            return;

        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
            text.text = label;

        Image image = button.GetComponent<Image>();
        if (image != null)
            image.color = color;
    }
}
