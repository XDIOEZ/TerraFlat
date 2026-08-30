using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Gameplay.Quests;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 任务分页状态

    /// <summary>GM 任务分页中的一条只读状态与操作按钮。</summary>
    private sealed class GmQuestRowView
    {
        public QuestDefinition Definition;
        public TextMeshProUGUI StatusText;
        public Button AcceptButton;
        public Button ClaimButton;
    }

    private readonly List<QuestDefinition> availableDebugQuests = new();
    private readonly List<GmQuestRowView> gmQuestRows = new();
    private Transform gmQuestListContent;
    private LayoutElement gmQuestListLayout;
    private RectTransform gmQuestToolbarTarget;
    private TextMeshProUGUI gmQuestSummaryText;
    private PlayerQuestRuntime boundGmQuestRuntime;

    #endregion

    #region 分页构建

    /// <summary>创建独立任务分页；正式任务状态只通过 PlayerQuestRuntime 公共 API 修改。</summary>
    private void BuildQuestPage()
    {
        GmPageView page = CreatePage(GmPageId.Quests);
        AddPageIntro(
            page.Content,
            "任务测试",
            "仅列出 debugOnly 任务。可以单独或批量开启、刷新状态型目标，并交付已完成的手动任务。 ");

        GameObject toolbar = CreateUiObject("Quest Toolbar", page.Content);
        gmQuestToolbarTarget = toolbar.GetComponent<RectTransform>();
        toolbar.AddComponent<LayoutElement>().preferredHeight = 40f;
        HorizontalLayoutGroup toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 8f;
        toolbarLayout.childAlignment = TextAnchor.MiddleLeft;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;

        gmQuestSummaryText = CreateText(
            toolbar.transform,
            "正在读取测试任务目录…",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        gmQuestSummaryText.enableWordWrapping = false;
        gmQuestSummaryText.overflowMode = TextOverflowModes.Ellipsis;
        gmQuestSummaryText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        Button acceptAllButton = CreateButton(
            toolbar.transform,
            "全部开启",
            AcceptAllDebugQuests,
            92f,
            34f);
        acceptAllButton.GetComponent<Image>().color = new Color(0.66f, 0.32f, 0.15f, 1f);
        CreateButton(toolbar.transform, "刷新目标", RefreshDebugQuestObjectives, 92f, 34f);

        GameObject listObject = CreateUiObject("Debug Quest List", page.Content);
        gmQuestListContent = listObject.transform;
        gmQuestListLayout = listObject.AddComponent<LayoutElement>();
        VerticalLayoutGroup listLayout = listObject.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 8f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        RefreshQuestPage();
    }

    /// <summary>重新读取目录并重建低频 GM 列表，避免把调试任务硬编码进按钮。</summary>
    private void RefreshQuestPage()
    {
        BindQuestRuntime();
        availableDebugQuests.Clear();
        if (QuestCatalog.IsReady)
        {
            availableDebugQuests.AddRange(
                QuestCatalog.All
                    .Where(definition => definition != null && definition.DebugOnly)
                    .OrderBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase));
        }

        RebuildQuestRows();
    }

    private void RebuildQuestRows()
    {
        if (gmQuestListContent == null)
            return;

        RemoveSearchEntriesForPage(GmPageId.Quests);
        ClearChildren(gmQuestListContent);
        gmQuestRows.Clear();
        RegisterSearchEntry(
            GmPageId.Quests,
            "全部开启测试任务",
            "任务 quest debug 批量 接取 开启",
            gmQuestToolbarTarget);

        if (availableDebugQuests.Count == 0)
        {
            AddPageHint(
                gmQuestListContent,
                QuestCatalog.IsReady ? "当前目录没有 debugOnly 测试任务。" : "任务目录尚未加载完成。",
                48f);
            SetQuestListHeight(48f);
            RefreshQuestRowStates();
            return;
        }

        for (int index = 0; index < availableDebugQuests.Count; index++)
            CreateQuestRow(availableDebugQuests[index]);

        SetQuestListHeight(availableDebugQuests.Count * 112f + (availableDebugQuests.Count - 1) * 8f);
        RefreshQuestRowStates();
        LayoutRebuilder.MarkLayoutForRebuild(gmQuestListContent as RectTransform);
    }

    private void CreateQuestRow(QuestDefinition definition)
    {
        GameObject row = CreateUiObject($"Quest {definition.Id}", gmQuestListContent);
        row.AddComponent<LayoutElement>().preferredHeight = 112f;
        row.AddComponent<Image>().color = new Color(0.043f, 0.112f, 0.139f, 1f);
        Outline outline = row.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);

        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(10, 10, 8, 8);
        rowLayout.spacing = 10f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        GameObject textColumn = CreateUiObject("Quest Text", row.transform);
        textColumn.AddComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup textLayout = textColumn.AddComponent<VerticalLayoutGroup>();
        textLayout.spacing = 2f;
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(
            textColumn.transform,
            string.IsNullOrWhiteSpace(definition.Title) ? definition.Id : definition.Title,
            14f,
            new Color(0.95f, 0.91f, 0.84f));
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        TextMeshProUGUI description = CreateText(
            textColumn.transform,
            definition.Description ?? string.Empty,
            11f,
            new Color(0.66f, 0.71f, 0.71f));
        description.enableWordWrapping = true;
        description.overflowMode = TextOverflowModes.Ellipsis;
        description.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        TextMeshProUGUI idText = CreateText(
            textColumn.transform,
            definition.Id,
            10f,
            new Color(0.45f, 0.58f, 0.62f));
        idText.enableWordWrapping = false;
        idText.overflowMode = TextOverflowModes.Ellipsis;
        idText.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

        TextMeshProUGUI status = CreateText(
            row.transform,
            "未开启",
            11f,
            new Color(0.66f, 0.71f, 0.71f));
        status.enableWordWrapping = true;
        status.overflowMode = TextOverflowModes.Ellipsis;
        status.alignment = TextAlignmentOptions.MidlineLeft;
        status.gameObject.AddComponent<LayoutElement>().preferredWidth = 230f;

        GameObject actions = CreateUiObject("Quest Actions", row.transform);
        actions.AddComponent<LayoutElement>().preferredWidth = 92f;
        VerticalLayoutGroup actionsLayout = actions.AddComponent<VerticalLayoutGroup>();
        actionsLayout.spacing = 6f;
        actionsLayout.childAlignment = TextAnchor.MiddleCenter;
        actionsLayout.childControlWidth = true;
        actionsLayout.childControlHeight = true;
        actionsLayout.childForceExpandWidth = true;
        actionsLayout.childForceExpandHeight = false;

        string questId = definition.Id;
        Button acceptButton = CreateButton(
            actions.transform,
            "开启任务",
            () => AcceptDebugQuest(questId),
            88f,
            36f);
        Button claimButton = CreateButton(
            actions.transform,
            "交付任务",
            () => ClaimDebugQuest(questId),
            88f,
            36f);

        gmQuestRows.Add(new GmQuestRowView
        {
            Definition = definition,
            StatusText = status,
            AcceptButton = acceptButton,
            ClaimButton = claimButton
        });
        RegisterSearchEntry(
            GmPageId.Quests,
            string.IsNullOrWhiteSpace(definition.Title) ? definition.Id : definition.Title,
            $"{definition.Id} quest 任务 debug 测试",
            row.transform as RectTransform);
    }

    private void SetQuestListHeight(float height)
    {
        if (gmQuestListLayout == null)
            return;

        gmQuestListLayout.minHeight = height;
        gmQuestListLayout.preferredHeight = height;
    }

    #endregion

    #region 运行时绑定与状态

    private void BindQuestRuntime()
    {
        Transform playerTransform = GetLocalPlayerTransform();
        Player player = playerTransform != null
            ? playerTransform.GetComponent<Player>() ?? playerTransform.GetComponentInParent<Player>()
            : null;
        PlayerQuestRuntime runtime = null;
        if (player != null && QuestManager.Instance != null)
            QuestManager.Instance.TryGetRuntime(player, out runtime);

        if (ReferenceEquals(boundGmQuestRuntime, runtime))
            return;

        if (boundGmQuestRuntime != null)
            boundGmQuestRuntime.QuestChanged -= HandleGmQuestChanged;
        boundGmQuestRuntime = runtime;
        if (boundGmQuestRuntime != null)
            boundGmQuestRuntime.QuestChanged += HandleGmQuestChanged;
    }

    private void RefreshQuestRowStates()
    {
        int acceptedCount = 0;
        int readyCount = 0;
        for (int index = 0; index < gmQuestRows.Count; index++)
        {
            GmQuestRowView row = gmQuestRows[index];
            QuestSnapshot snapshot = null;
            bool hasSnapshot = boundGmQuestRuntime != null &&
                               boundGmQuestRuntime.TryGetSnapshot(row.Definition.Id, out snapshot);
            if (hasSnapshot)
                acceptedCount++;
            if (snapshot?.Status == QuestStatus.ReadyToClaim)
                readyCount++;

            row.StatusText.text = GetQuestStatusText(row.Definition, snapshot);
            row.StatusText.color = GetQuestStatusColor(snapshot);
            row.AcceptButton.interactable = boundGmQuestRuntime != null && !hasSnapshot;
            row.ClaimButton.interactable = snapshot?.Status == QuestStatus.ReadyToClaim;
        }

        if (gmQuestSummaryText == null)
            return;

        gmQuestSummaryText.text = boundGmQuestRuntime == null
            ? $"测试任务 {availableDebugQuests.Count} · 请先进入游戏世界"
            : $"测试任务 {availableDebugQuests.Count} · 已开启 {acceptedCount} · 可交付 {readyCount}";
    }

    private static string GetQuestStatusText(QuestDefinition definition, QuestSnapshot snapshot)
    {
        if (snapshot == null)
            return "未开启";
        if (snapshot.Status == QuestStatus.ReadyToClaim)
            return "目标完成\n等待手动交付";
        if (snapshot.Status == QuestStatus.Completed)
            return "已完成";

        QuestStageDefinition stage = definition.Stages?.FirstOrDefault(value =>
            string.Equals(value.Id, snapshot.CurrentStageId, StringComparison.OrdinalIgnoreCase));
        if (stage?.Objectives == null || stage.Objectives.Count == 0)
            return $"进行中\n阶段：{snapshot.CurrentStageId}";

        string[] objectives = stage.Objectives.Select(objective =>
        {
            string key = $"{stage.Id}/{objective.Id}";
            float current = 0f;
            snapshot.ObjectiveProgress?.TryGetValue(key, out current);
            string label = string.IsNullOrWhiteSpace(objective.Label) ? objective.Id : objective.Label;
            return $"{label} {current:0.##}/{objective.Required:0.##}";
        }).ToArray();
        return "进行中\n" + string.Join("；", objectives);
    }

    private static Color GetQuestStatusColor(QuestSnapshot snapshot)
    {
        return snapshot?.Status switch
        {
            QuestStatus.Active => new Color(0.42f, 0.83f, 0.90f),
            QuestStatus.ReadyToClaim => new Color(0.95f, 0.69f, 0.29f),
            QuestStatus.Completed => new Color(0.43f, 0.82f, 0.55f),
            _ => new Color(0.66f, 0.71f, 0.71f)
        };
    }

    private void HandleGmQuestChanged(QuestSnapshot _)
    {
        RefreshQuestRowStates();
    }

    private void HandleQuestPageSceneChanged()
    {
        UnbindQuestRuntime();
        RefreshQuestRowStates();
    }

    private void DisposeQuestPage()
    {
        UnbindQuestRuntime();
    }

    private void UnbindQuestRuntime()
    {
        if (boundGmQuestRuntime != null)
            boundGmQuestRuntime.QuestChanged -= HandleGmQuestChanged;
        boundGmQuestRuntime = null;
    }

    #endregion

    #region GM 任务操作

    private void AcceptDebugQuest(string questId)
    {
        if (!TryGetQuestRuntime(out PlayerQuestRuntime runtime, out string error))
        {
            SetStatus(error, Color.yellow);
            return;
        }
        if (!QuestCatalog.TryGet(questId, out QuestDefinition definition) || !definition.DebugOnly)
        {
            SetStatus($"拒绝开启非测试任务：{questId}", Color.yellow);
            return;
        }
        if (!runtime.AcceptQuest(questId, out error))
        {
            SetStatus($"开启任务失败：{error}", Color.yellow);
            RefreshQuestRowStates();
            return;
        }

        SetStatus($"已开启测试任务：{definition.Title}", new Color(0.35f, 0.95f, 0.85f));
        RefreshQuestRowStates();
    }

    private void AcceptAllDebugQuests()
    {
        if (!TryGetQuestRuntime(out PlayerQuestRuntime runtime, out string error))
        {
            SetStatus(error, Color.yellow);
            return;
        }

        int accepted = 0;
        int skipped = 0;
        int failed = 0;
        string firstFailure = null;
        foreach (QuestDefinition definition in availableDebugQuests)
        {
            if (runtime.TryGetSnapshot(definition.Id, out _))
            {
                skipped++;
                continue;
            }

            if (runtime.AcceptQuest(definition.Id, out string acceptError))
                accepted++;
            else
            {
                failed++;
                firstFailure ??= acceptError;
            }
        }

        Color color = failed == 0 ? new Color(0.35f, 0.95f, 0.85f) : Color.yellow;
        string failureSuffix = string.IsNullOrWhiteSpace(firstFailure) ? string.Empty : $"；首个失败：{firstFailure}";
        SetStatus($"测试任务批量开启：成功 {accepted}，跳过 {skipped}，失败 {failed}{failureSuffix}", color);
        RefreshQuestRowStates();
    }

    private void ClaimDebugQuest(string questId)
    {
        if (!TryGetQuestRuntime(out PlayerQuestRuntime runtime, out string error))
        {
            SetStatus(error, Color.yellow);
            return;
        }
        if (!QuestCatalog.TryGet(questId, out QuestDefinition definition) || !definition.DebugOnly)
        {
            SetStatus($"拒绝交付非测试任务：{questId}", Color.yellow);
            return;
        }
        if (!runtime.ClaimQuest(questId, out error))
        {
            SetStatus($"交付任务失败：{error}", Color.yellow);
            RefreshQuestRowStates();
            return;
        }

        SetStatus($"已交付测试任务：{definition.Title}", new Color(0.35f, 0.95f, 0.85f));
        RefreshQuestRowStates();
    }

    private void RefreshDebugQuestObjectives()
    {
        if (!TryGetQuestRuntime(out PlayerQuestRuntime runtime, out string error))
        {
            SetStatus(error, Color.yellow);
            return;
        }

        runtime.Refresh();
        SetStatus("已刷新任务状态目标与自动交付状态。", new Color(0.35f, 0.95f, 0.85f));
        RefreshQuestRowStates();
    }

    private bool TryGetQuestRuntime(out PlayerQuestRuntime runtime, out string error)
    {
        BindQuestRuntime();
        runtime = boundGmQuestRuntime;
        if (runtime != null && runtime.IsEnabled)
        {
            error = null;
            return true;
        }

        error = "未找到已进入世界的本地玩家任务运行时。";
        return false;
    }

    #endregion
}
