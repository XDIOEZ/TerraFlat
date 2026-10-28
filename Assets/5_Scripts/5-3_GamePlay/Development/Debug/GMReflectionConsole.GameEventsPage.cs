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
    #region 游戏事件分页

    private void BindGameEventManager()
    {
        GameEventManager current = GameEventManager.Instance;
        if (ReferenceEquals(boundGameEventManager, current))
            return;

        UnbindGameEventManager();
        boundGameEventManager = current;
        if (boundGameEventManager == null)
            return;

        boundGameEventManager.EventStarted += HandleGameEventStateChanged;
        boundGameEventManager.EventEnded += HandleGameEventStateChanged;
    }

    private void UnbindGameEventManager()
    {
        if (boundGameEventManager == null)
            return;

        boundGameEventManager.EventStarted -= HandleGameEventStateChanged;
        boundGameEventManager.EventEnded -= HandleGameEventStateChanged;
        boundGameEventManager = null;
    }

    private void HandleGameEventStateChanged(GameEventRuntimeNotification notification)
    {
        RequestGameEventPageRefresh();
    }

    private void RequestGameEventPageRefresh()
    {
        if (gameEventRefreshCoroutine == null && isActiveAndEnabled)
            gameEventRefreshCoroutine = StartCoroutine(RebuildGameEventPageNextFrame());
    }

    private IEnumerator RebuildGameEventPageNextFrame()
    {
        yield return null;
        gameEventRefreshCoroutine = null;
        RebuildGameEventPage();
    }

    private void RebuildGameEventPage()
    {
        if (gameEventPageContent == null)
            return;

        RemoveSearchEntriesForPage(GmPageId.GameEvents);
        ClearChildren(gameEventPageContent);
        AddPageIntro(gameEventPageContent, "游戏事件", "查看 JSON 配置中的全局事件，并在当前主机世界中手动触发或结束事件。 ");

        BindGameEventManager();
        GameEventManager manager = boundGameEventManager;
        int definitionCount = manager?.Definitions.Count ?? 0;
        int activeCount = manager?.ActiveEvents.Count ?? 0;

        GameObject toolbar = CreateUiObject("Game Event Toolbar", gameEventPageContent);
        toolbar.AddComponent<LayoutElement>().preferredHeight = 38f;
        HorizontalLayoutGroup toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 8f;
        toolbarLayout.childAlignment = TextAnchor.MiddleLeft;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;

        TextMeshProUGUI countText = CreateText(
            toolbar.transform,
            "已加载 {0} 个事件 · 正在进行 {1} 个",
            13f,
            GmTextPrimary);
        SetLocalizedFormat(countText, "已加载 {0} 个事件 · 正在进行 {1} 个", definitionCount, activeCount);
        countText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(toolbar.transform, "重载 JSON", ReloadGameEventConfiguration, 112f, 32f);

        if (manager == null || definitionCount == 0)
        {
            AddPageHint(gameEventPageContent, "没有加载到可用事件，请检查 Resources/Config/GameEvents/Definitions。", 36f);
            RefreshSearchResultsIfVisible();
            return;
        }

        for (int i = 0; i < manager.Definitions.Count; i++)
            CreateGameEventCard(manager, manager.Definitions[i]);

        RefreshSearchResultsIfVisible();
    }

    private void CreateGameEventCard(GameEventManager manager, GameEventDefinition definition)
    {
        bool active = manager.IsEventActive(definition.Id);
        GameObject card = CreateUiObject("Event " + definition.Id, gameEventPageContent);
        card.AddComponent<LayoutElement>().preferredHeight = 94f;
        card.AddComponent<Image>().color = active
            ? GmSelection
            : GmSurface;
        Outline outline = card.AddComponent<Outline>();
        StyleGmOutline(outline, active);

        HorizontalLayoutGroup cardLayout = card.AddComponent<HorizontalLayoutGroup>();
        cardLayout.padding = new RectOffset(12, 10, 8, 8);
        cardLayout.spacing = 12f;
        cardLayout.childAlignment = TextAnchor.MiddleLeft;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = false;
        cardLayout.childForceExpandHeight = true;

        GameObject info = CreateUiObject("Info", card.transform);
        LayoutElement infoLayout = info.AddComponent<LayoutElement>();
        infoLayout.flexibleWidth = 1f;
        VerticalLayoutGroup infoGroup = info.AddComponent<VerticalLayoutGroup>();
        infoGroup.spacing = 1f;
        infoGroup.childControlWidth = true;
        infoGroup.childControlHeight = true;
        infoGroup.childForceExpandWidth = true;
        infoGroup.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(
            info.transform,
            string.Empty,
            14f,
            active ? GmAccentHover : GmTextPrimary);
        SetLocalizedProvider(title, () => FlatWorldLocalizationService.GetUiFormat(
            "{0}  <color=#80969B>({1})</color>",
            FlatWorldLocalizationService.GetUiText(definition.DisplayName), definition.Id));
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 23f;

        TextMeshProUGUI description = CreateText(
            info.transform,
            string.Empty,
            12f,
            GmTextSecondary);
        SetLocalizedProvider(description, () => FlatWorldLocalizationService.GetUiText(
            string.IsNullOrWhiteSpace(definition.Description) ? "无事件说明。" : definition.Description));
        description.enableWordWrapping = false;
        description.overflowMode = TextOverflowModes.Ellipsis;
        description.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        string triggerType = definition.Trigger?.Type ?? "unknown";
        TextMeshProUGUI metadata = CreateText(
            info.transform,
            string.Empty,
            11f,
            GmTextSecondary);
        SetLocalizedProvider(metadata, () =>
        {
            string duration = definition.DurationDays > 0f
                ? FlatWorldLocalizationService.GetUiFormat("{0:0.##} 天", definition.DurationDays)
                : FlatWorldLocalizationService.GetUiText("动作完成即结束");
            return FlatWorldLocalizationService.GetUiFormat(
                "触发器 {0} · 持续 {1} · 动作 {2} 个 · {3}",
                triggerType, duration, definition.Actions.Count,
                FlatWorldLocalizationService.GetUiText(active ? "进行中" : "未运行"));
        });
        metadata.enableWordWrapping = false;
        metadata.overflowMode = TextOverflowModes.Ellipsis;
        metadata.gameObject.AddComponent<LayoutElement>().preferredHeight = 19f;

        string eventId = definition.Id;
        Button actionButton = CreateButton(
            card.transform,
            active ? "结束事件" : "强制触发",
            () => TriggerOrCancelGameEvent(eventId),
            112f,
            38f);
        SetGmButtonVisual(actionButton, active ? GmDanger : GmSurfaceRaised, !active);

        RegisterSearchEntry(
            GmPageId.GameEvents,
            definition.DisplayName,
            $"{definition.Id} {definition.Description} {triggerType} 游戏事件 event 触发",
            card.transform as RectTransform);
    }

    private void TriggerOrCancelGameEvent(string eventId)
    {
        BindGameEventManager();
        if (boundGameEventManager == null)
        {
            SetStatus("游戏事件管理器不可用。", Color.yellow);
            return;
        }

        bool wasActive = boundGameEventManager.IsEventActive(eventId);
        bool success = wasActive
            ? boundGameEventManager.CancelEvent(eventId)
            : boundGameEventManager.TryForceTriggerNow(eventId);

        if (success)
        {
            SetStatusFormat(
                wasActive ? "已结束事件：{0}" : "已触发事件：{0}",
                new Color(0.35f, 0.95f, 0.85f),
                eventId);
        }
        else
        {
            SetStatus(
                "操作失败：请确认已进入游戏世界且本机拥有状态权限。",
                Color.yellow);
        }

        RequestGameEventPageRefresh();
    }

    private void ReloadGameEventConfiguration()
    {
        BindGameEventManager();
        if (boundGameEventManager == null)
        {
            SetStatus("游戏事件管理器不可用。", Color.yellow);
            return;
        }

        GameEventConfigLoadResult result = boundGameEventManager.ReloadConfiguration();
        RebuildGameEventPage();
        SetStatusFormat(
            result.HasErrors
                ? "事件配置已重载，但发现 {0} 个配置问题，请查看 Console。"
                : "事件配置已重载，共 {0} 个事件。",
            result.HasErrors ? Color.yellow : new Color(0.35f, 0.95f, 0.85f),
            result.HasErrors ? result.Issues.Count : boundGameEventManager.Definitions.Count);
    }

    #endregion

}
