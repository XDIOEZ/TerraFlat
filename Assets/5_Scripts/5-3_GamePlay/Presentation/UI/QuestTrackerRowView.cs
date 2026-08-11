using System;
using System.Globalization;
using System.Text;
using FlatWorld.Gameplay.Quests;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 简洁任务追踪器的单条只读视图。它只消费 QuestSnapshot 与任务定义，负责解析本地化标题、说明、
/// 当前阶段目标和聚合进度；不持有任务存档，也不直接接取、推进或交付任务。
/// </summary>
[DisallowMultipleComponent]
public sealed class QuestTrackerRowView : MonoBehaviour
{
    #region 节点契约与视觉

    private const string StatusLineNodeName = "状态线";
    private const string TitleNodeName = "任务标题";
    private const string StatusNodeName = "任务状态";
    private const string DescriptionNodeName = "任务说明";
    private const string ObjectiveNodeName = "目标文本";
    private const string ProgressFillNodeName = "进度填充";
    private const int MaximumDisplayedObjectives = 2;

    private static readonly Color ActiveColor = new(0.83f, 0.49f, 0.23f, 1f);
    private static readonly Color ReadyColor = new(0.26f, 0.61f, 0.57f, 1f);
    private static readonly Color CompletedColor = new(0.66f, 0.72f, 0.73f, 1f);

    private readonly StringBuilder objectiveBuilder = new(96);
    private Image statusLine;
    private Image progressFill;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI descriptionText;
    private TextMeshProUGUI objectiveText;
    private string questId;

    public string QuestId => questId;

    #endregion

    #region 生命周期与绑定

    private void Awake()
    {
        ResolveNodes();
    }

    /// <summary>把不可变快照与当前定义写入已有视觉节点。</summary>
    public void Bind(QuestDefinition definition, QuestSnapshot snapshot)
    {
        ResolveNodes();
        if (snapshot == null)
        {
            Clear();
            return;
        }

        questId = snapshot.QuestId;
        if (titleText != null)
        {
            string fallbackTitle = definition?.Title;
            if (string.IsNullOrWhiteSpace(fallbackTitle))
                fallbackTitle = string.IsNullOrWhiteSpace(snapshot.Title) ? snapshot.QuestId : snapshot.Title;
            titleText.text = GetContentText(definition?.TitleKey, fallbackTitle);
        }

        if (descriptionText != null)
            descriptionText.text = GetContentText(definition?.DescriptionKey, definition?.Description);

        if (statusText != null)
            statusText.text = GetStatusText(snapshot.Status);

        QuestStageDefinition stage = FindCurrentStage(definition, snapshot.CurrentStageId);
        if (objectiveText != null)
            objectiveText.text = BuildObjectiveText(stage, snapshot);

        float progress = CalculateStageProgress(stage, snapshot);
        if (progressFill != null)
            progressFill.fillAmount = progress;

        Color stateColor = GetStatusColor(snapshot.Status);
        if (statusLine != null)
            statusLine.color = stateColor;
        if (statusText != null)
            statusText.color = stateColor;
        if (progressFill != null)
            progressFill.color = stateColor;
    }

    /// <summary>清空对象池行，避免列表缩短后残留旧任务内容。</summary>
    public void Clear()
    {
        questId = null;
        if (titleText != null)
            titleText.text = string.Empty;
        if (statusText != null)
            statusText.text = string.Empty;
        if (descriptionText != null)
            descriptionText.text = string.Empty;
        if (objectiveText != null)
            objectiveText.text = string.Empty;
        if (progressFill != null)
            progressFill.fillAmount = 0f;
    }

    #endregion

    #region 文本与进度

    private string BuildObjectiveText(QuestStageDefinition stage, QuestSnapshot snapshot)
    {
        if (snapshot.Status == QuestStatus.ReadyToClaim)
            return FlatWorldLocalizationService.GetUiText("任务目标已完成");
        if (stage?.Objectives == null || stage.Objectives.Count == 0)
            return FlatWorldLocalizationService.GetUiText("暂无任务目标");

        objectiveBuilder.Clear();
        int displayCount = Mathf.Min(stage.Objectives.Count, MaximumDisplayedObjectives);
        for (int index = 0; index < displayCount; index++)
        {
            QuestObjectiveDefinition objective = stage.Objectives[index];
            if (index > 0)
                objectiveBuilder.Append("  ·  ");

            string fallbackLabel = string.IsNullOrWhiteSpace(objective.Label)
                ? objective.Id
                : objective.Label;
            objectiveBuilder.Append(GetContentText(objective.LabelKey, fallbackLabel));
            objectiveBuilder.Append("  ");

            string progressKey = $"{stage.Id}/{objective.Id}";
            float current = 0f;
            snapshot.ObjectiveProgress?.TryGetValue(progressKey, out current);
            objectiveBuilder.Append(FormatNumber(Mathf.Clamp(current, 0f, objective.Required)));
            objectiveBuilder.Append('/');
            objectiveBuilder.Append(FormatNumber(objective.Required));
        }

        if (stage.Objectives.Count > displayCount)
        {
            objectiveBuilder.Append("  ·  +");
            objectiveBuilder.Append(stage.Objectives.Count - displayCount);
        }

        return objectiveBuilder.ToString();
    }

    private static float CalculateStageProgress(QuestStageDefinition stage, QuestSnapshot snapshot)
    {
        if (snapshot.Status != QuestStatus.Active)
            return 1f;
        if (stage?.Objectives == null || stage.Objectives.Count == 0)
            return 0f;

        bool anyMode = string.Equals(
            stage.CompletionMode,
            QuestCompletionModes.Any,
            StringComparison.OrdinalIgnoreCase);
        float aggregate = 0f;
        for (int index = 0; index < stage.Objectives.Count; index++)
        {
            QuestObjectiveDefinition objective = stage.Objectives[index];
            string progressKey = $"{stage.Id}/{objective.Id}";
            float current = 0f;
            snapshot.ObjectiveProgress?.TryGetValue(progressKey, out current);
            float normalized = objective.Required <= 0f
                ? 0f
                : Mathf.Clamp01(current / objective.Required);
            if (anyMode)
                aggregate = Mathf.Max(aggregate, normalized);
            else
                aggregate += normalized;
        }

        return anyMode ? aggregate : aggregate / stage.Objectives.Count;
    }

    private static QuestStageDefinition FindCurrentStage(
        QuestDefinition definition,
        string currentStageId)
    {
        if (definition?.Stages == null)
            return null;

        for (int index = 0; index < definition.Stages.Count; index++)
        {
            QuestStageDefinition stage = definition.Stages[index];
            if (stage != null && string.Equals(
                    stage.Id,
                    currentStageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return stage;
            }
        }

        return null;
    }

    private static string GetContentText(string key, string fallback)
    {
        string safeFallback = fallback ?? string.Empty;
        return string.IsNullOrWhiteSpace(key)
            ? safeFallback
            : FlatWorldLocalizationService.Get(key, safeFallback);
    }

    private static string GetStatusText(QuestStatus status)
    {
        return status switch
        {
            QuestStatus.ReadyToClaim => FlatWorldLocalizationService.GetUiText("可领取"),
            QuestStatus.Completed => FlatWorldLocalizationService.GetUiText("已完成"),
            _ => FlatWorldLocalizationService.GetUiText("进行中")
        };
    }

    private static Color GetStatusColor(QuestStatus status)
    {
        return status switch
        {
            QuestStatus.ReadyToClaim => ReadyColor,
            QuestStatus.Completed => CompletedColor,
            _ => ActiveColor
        };
    }

    private static string FormatNumber(float value)
    {
        float rounded = Mathf.Round(value);
        return Mathf.Approximately(value, rounded)
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    #endregion

    #region 节点解析

    private void ResolveNodes()
    {
        statusLine ??= FindChildComponent<Image>(StatusLineNodeName);
        progressFill ??= FindChildComponent<Image>(ProgressFillNodeName);
        titleText ??= FindChildComponent<TextMeshProUGUI>(TitleNodeName);
        statusText ??= FindChildComponent<TextMeshProUGUI>(StatusNodeName);
        descriptionText ??= FindChildComponent<TextMeshProUGUI>(DescriptionNodeName);
        objectiveText ??= FindChildComponent<TextMeshProUGUI>(ObjectiveNodeName);
    }

    private T FindChildComponent<T>(string childName) where T : Component
    {
        T[] components = GetComponentsInChildren<T>(true);
        for (int index = 0; index < components.Length; index++)
        {
            if (components[index] != null && components[index].name == childName)
                return components[index];
        }

        return null;
    }

    #endregion
}
