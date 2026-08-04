using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    private enum BuffTargetingMode
    {
        None,
        Apply,
        Clear
    }

    private const int MaxBuffApplicationsPerClick = 99;

    private readonly List<BuffDefinition> availableBuffDefinitions = new();

    private GameController buffTargetingController;
    private TMP_InputField buffIdInput;
    private TMP_InputField buffDurationInput;
    private TMP_InputField buffApplicationCountInput;
    private TextMeshProUGUI buffDefinitionHintText;
    private TextMeshProUGUI buffTargetingHintText;
    private Button buffApplyButton;
    private Button buffCancelButton;
    private Button buffClearButton;
    private int selectedBuffDefinitionIndex = -1;
    private BuffTargetingMode buffTargetingMode;
    private string pendingBuffId;
    private float? pendingBuffDurationSeconds;
    private int pendingBuffApplicationCount = 1;
    private float nextBuffInputResolveTime;

    private void BuildBuffPage()
    {
        GmPageView page = CreatePage(GmPageId.Buff);
        AddPageIntro(
            page.Content,
            "Buff 分发",
            "选择已加载的 Buff，设置限时 Buff 的持续时间与施加次数；确认后用左键点选带有 BuffManager 的玩家或生物。 ");

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
            "Buff 取消 停止 左键分发",
            CancelBuffTargeting,
            40f);
        buffClearButton = CreateSearchableButton(
            actionGrid,
            GmPageId.Buff,
            "清除 Buff",
            "Buff 清除 清空 目标 左键",
            ToggleClearBuffTargeting,
            40f);

        buffTargetingHintText = AddPageHint(
            page.Content,
            "当前未启用 Buff 点选操作。",
            34f);

        RefreshBuffDefinitions();
        RefreshBuffTargetingControls();
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

    private void BeginBuffApplyTargeting()
    {
        string buffId = buffIdInput?.text?.Trim();
        BuffDefinition definition = GameRes.Instance?.GetBuffDefinition(buffId);
        if (definition == null)
        {
            SetStatus("请先选择一个已加载的 Buff。", Color.yellow);
            return;
        }

        if (!TryReadBuffDurationOverride(out float? durationOverride, out string durationError))
        {
            SetStatus(durationError, Color.yellow);
            return;
        }

        if (durationOverride.HasValue && definition.IsPermanent)
        {
            SetStatus("永久 Buff 不能覆盖持续时间；请清空持续时间输入框后再确认。", Color.yellow);
            return;
        }

        if (!TryReadBuffApplicationCount(out int applicationCount, out string countError))
        {
            SetStatus(countError, Color.yellow);
            return;
        }

        pendingBuffId = definition.Id;
        pendingBuffDurationSeconds = durationOverride;
        pendingBuffApplicationCount = applicationCount;
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
            StopBuffTargeting("清除 Buff 点选操作已关闭。");
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

        StopBuffTargeting("Buff 点选操作已取消。");
    }

    private void StartBuffTargeting(BuffTargetingMode mode)
    {
        buffTargetingMode = mode;
        if (!EnsureBuffTargetingInputController())
        {
            buffTargetingMode = BuffTargetingMode.None;
            RefreshBuffTargetingControls();
            SetStatus("未找到本地玩家输入控制器，无法进入 Buff 点选操作。", Color.yellow);
            return;
        }

        RefreshBuffTargetingControls();
        if (mode == BuffTargetingMode.Apply)
        {
            string durationText = pendingBuffDurationSeconds.HasValue
                ? $"，持续 {pendingBuffDurationSeconds.Value:0.##} 秒"
                : string.Empty;
            SetStatus(
                $"已进入 Buff 分发模式：{pendingBuffId} ×{pendingBuffApplicationCount}{durationText}。请用左键点选世界对象；点击“取消”可关闭。",
                new Color(0.35f, 0.95f, 0.85f));
        }
        else
        {
            SetStatus(
                "已进入清除 Buff 模式：请用左键点选世界对象；再次点击“清除 Buff”可退出。",
                new Color(1f, 0.71f, 0.30f));
        }
    }

    private void StopBuffTargeting(string statusMessage = null)
    {
        buffTargetingMode = BuffTargetingMode.None;
        pendingBuffId = null;
        pendingBuffDurationSeconds = null;
        pendingBuffApplicationCount = 1;
        UnbindBuffTargetingInputController();
        RefreshBuffTargetingControls();

        if (!string.IsNullOrWhiteSpace(statusMessage))
            SetStatus(statusMessage, new Color(0.66f, 0.71f, 0.71f));
    }

    private bool EnsureBuffTargetingInputController()
    {
        Transform localPlayer = GetLocalPlayerTransform();
        GameController controller = localPlayer != null
            ? localPlayer.GetComponentInChildren<GameController>(true)
            : null;
        controller ??= FindFirstComponent("GameController") as GameController;
        if (controller == null)
            return false;

        if (buffTargetingController == controller)
            return true;

        UnbindBuffTargetingInputController();
        buffTargetingController = controller;
        buffTargetingController.LeftClick.DynamicCalls += HandleBuffTargetingClick;
        return true;
    }

    private void UnbindBuffTargetingInputController()
    {
        if (buffTargetingController != null && buffTargetingController.LeftClick != null)
            buffTargetingController.LeftClick.DynamicCalls -= HandleBuffTargetingClick;

        buffTargetingController = null;
    }

    private void UpdateBuffTargetingInput()
    {
        if (buffTargetingMode == BuffTargetingMode.None || buffTargetingController != null)
            return;

        if (Time.unscaledTime < nextBuffInputResolveTime)
            return;

        nextBuffInputResolveTime = Time.unscaledTime + 0.5f;
        EnsureBuffTargetingInputController();
    }

    private void HandleBuffTargetingSceneChanged()
    {
        if (buffTargetingMode != BuffTargetingMode.None)
            StopBuffTargeting("场景已切换，Buff 点选操作已自动取消。");
        else
            UnbindBuffTargetingInputController();

        nextBuffInputResolveTime = 0f;
        RefreshBuffDefinitions();
    }

    private void DisposeBuffTargeting()
    {
        buffTargetingMode = BuffTargetingMode.None;
        pendingBuffId = null;
        pendingBuffDurationSeconds = null;
        pendingBuffApplicationCount = 1;
        UnbindBuffTargetingInputController();
    }

    private void HandleBuffTargetingClick()
    {
        if (buffTargetingMode == BuffTargetingMode.None || buffTargetingController == null)
            return;

        Vector3 worldPosition;
        try
        {
            worldPosition = buffTargetingController.GetMouseWorldPosition();
        }
        catch (Exception exception)
        {
            SetStatus($"无法读取鼠标世界坐标：{exception.Message}", Color.yellow);
            return;
        }

        BuffManager target = FindBuffManagerAt(worldPosition);
        if (target == null)
        {
            SetStatus("未点选到支持 Buff 的游戏对象；目标需要带有 BuffManager。", Color.yellow);
            return;
        }

        string targetName = GetBuffTargetName(target);
        if (buffTargetingMode == BuffTargetingMode.Clear)
        {
            int clearedCount = target.ActiveBuffs?.Count ?? 0;
            target.ClearAllBuffs();
            SetStatus(
                clearedCount > 0
                    ? $"已清除 {targetName} 身上的 {clearedCount} 个 Buff。"
                    : $"{targetName} 当前没有可清除的 Buff。",
                new Color(1f, 0.71f, 0.30f));
            return;
        }

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

    private static BuffManager FindBuffManagerAt(Vector2 worldPosition)
    {
        Collider2D[] colliders = Physics2D.OverlapPointAll(worldPosition);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null)
                continue;

            Item item = collider.GetComponentInParent<Item>();
            BuffManager manager = item != null
                ? item.GetComponentInChildren<BuffManager>(true)
                : collider.GetComponentInParent<BuffManager>();
            if (manager != null && manager.isActiveAndEnabled)
                return manager;
        }

        return null;
    }

    private static string GetBuffTargetName(BuffManager manager)
    {
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
            applying ? "正在点选施加" : "确认并点选施加",
            applying ? new Color(0.18f, 0.48f, 0.33f, 1f) : new Color(0.66f, 0.32f, 0.15f, 1f));
        SetBuffButtonPresentation(
            buffCancelButton,
            buffTargetingMode == BuffTargetingMode.None ? "取消" : "取消点选操作",
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
                $"分发模式已开启：{pendingBuffId} ×{pendingBuffApplicationCount}。左键点选可持续对多个目标施加；可关闭 GM 面板后操作。";
            buffTargetingHintText.color = new Color(0.35f, 0.95f, 0.85f);
        }
        else if (clearing)
        {
            buffTargetingHintText.text =
                "清除模式已开启：左键点选目标会清除其全部 Buff；再次点击“清除 Buff”即可退出。";
            buffTargetingHintText.color = new Color(1f, 0.71f, 0.30f);
        }
        else
        {
            buffTargetingHintText.text = "当前未启用 Buff 点选操作。";
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
