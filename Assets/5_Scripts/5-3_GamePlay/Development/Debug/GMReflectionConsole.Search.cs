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
    #region 搜索索引与目标定位

    private void RegisterSearchEntry(
        GmPageId pageId,
        string label,
        string keywords,
        RectTransform target)
    {
        if (target == null || string.IsNullOrWhiteSpace(label))
            return;

        gmSearchEntries.Add(new GmSearchEntry
        {
            PageId = pageId,
            Label = label.Trim(),
            SearchText = $"{label} {keywords} {GetPageLabel(pageId)}".ToLowerInvariant(),
            Target = target
        });
    }

    private void RemoveSearchEntriesForPage(GmPageId pageId)
    {
        gmSearchEntries.RemoveAll(entry => entry.PageId == pageId);
    }

    private void RebuildGlobalSearchResults()
    {
        if (gmSearchInput == null || gmSearchResultsRoot == null || gmSearchResultsContent == null)
            return;

        ClearChildren(gmSearchResultsContent);
        string query = gmSearchInput.text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            gmSearchResultsRoot.SetActive(false);
            SetLocalizedText(gmSearchSummaryText, "输入名称后跳转");
            return;
        }

        string[] tokens = query
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        List<GmSearchEntry> matches = gmSearchEntries
            .Where(entry => entry.Target != null && tokens.All(token => entry.SearchText.Contains(token)))
            .OrderByDescending(entry => entry.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(entry => GetPageLabel(entry.PageId), StringComparer.Ordinal)
            .ThenBy(entry => entry.Label, StringComparer.Ordinal)
            .Take(MaxVisibleSearchResults)
            .ToList();

        gmSearchResultsRoot.SetActive(true);
        gmSearchResultsRoot.transform.SetAsLastSibling();
        float resultsHeight = Mathf.Clamp(matches.Count * 36f + 18f, 56f, 158f);
        ConfigureSearchResultsOverlay(gmSearchResultsRect, resultsHeight);
        if (matches.Count > 0)
            SetLocalizedFormat(gmSearchSummaryText, "找到 {0} 项", matches.Count);
        else
            SetLocalizedText(gmSearchSummaryText, "没有匹配项");

        if (matches.Count == 0)
        {
            AddPageHint(gmSearchResultsContent, "没有找到对应功能，请尝试更短的关键词。", 32f);
            return;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            GmSearchEntry entry = matches[i];
            Button resultButton = CreateButton(
                gmSearchResultsContent,
                $"{GetPageLabel(entry.PageId)}  ›  {entry.Label}",
                () => NavigateToSearchEntry(entry),
                0f,
                32f);
            TextMeshProUGUI resultLabel = resultButton.GetComponentInChildren<TextMeshProUGUI>(true);
            SetLocalizedProvider(resultLabel, () => FlatWorldLocalizationService.GetUiFormat(
                "{0}  ›  {1}",
                FlatWorldLocalizationService.GetUiText(GetPageLabel(entry.PageId)),
                FlatWorldLocalizationService.GetUiText(entry.Label)));
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(gmSearchResultsContent as RectTransform);
    }

    private void NavigateToSearchEntry(GmSearchEntry entry)
    {
        if (entry == null || entry.Target == null)
        {
            SetStatus("搜索结果已失效，请重新搜索。", Color.yellow);
            return;
        }

        gmSearchInput.SetTextWithoutNotify(string.Empty);
        gmSearchResultsRoot.SetActive(false);
        SetLocalizedText(gmSearchSummaryText, "输入名称后跳转");
        SetActivePage(entry.PageId);

        // 切页可能重建事件、任务或命令条目，改用最新结果对应的目标。
        GmSearchEntry current = gmSearchEntries.FirstOrDefault(candidate =>
            candidate.PageId == entry.PageId && candidate.Label == entry.Label &&
            candidate.SearchText == entry.SearchText && candidate.Target != null);
        if (current == null)
        {
            SetStatus("搜索目标已更新，请重新搜索。", Color.yellow);
            return;
        }

        if (searchNavigationCoroutine != null)
            StopCoroutine(searchNavigationCoroutine);
        searchNavigationCoroutine = StartCoroutine(ScrollToSearchTarget(current));
    }

    private IEnumerator ScrollToSearchTarget(GmSearchEntry entry)
    {
        yield return null;
        if (entry?.Target == null || activeGmPage != entry.PageId ||
            !gmPages.TryGetValue(entry.PageId, out GmPageView page))
            yield break;

        Canvas.ForceUpdateCanvases();
        ScrollToPageTarget(page, entry.Target);

        Image highlight = entry.Target.GetComponent<Image>();
        if (highlight != null)
        {
            Color original = highlight.color;
            highlight.color = GmSelection;
            yield return new WaitForSecondsRealtime(0.9f);
            if (highlight != null)
                highlight.color = original;
        }

        searchNavigationCoroutine = null;
    }

    private void ClearGlobalSearch()
    {
        if (gmSearchInput == null)
            return;

        gmSearchInput.text = string.Empty;
        gmSearchInput.ActivateInputField();
    }

    #endregion

}
