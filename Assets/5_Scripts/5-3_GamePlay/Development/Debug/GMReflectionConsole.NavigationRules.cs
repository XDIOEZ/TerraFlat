#if UNITY_EDITOR
using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region GM 固定二级分页布局采样

    [InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterGmFixedSecondaryPageRules() => GameplayTestSystemProbe.Register(
        "ui_settings", "gm_secondary_pages", CaptureGmFixedSecondaryPageRules);

    // 使用未激活的控制器和独立画布，只调用正式分页构建及显示入口，不接触现场或玩家偏好。
    private static JObject CaptureGmFixedSecondaryPageRules(JObject arguments)
    {
        var controllerRoot = new GameObject("GM page rules controller");
        controllerRoot.SetActive(false);
        var canvasRoot = new GameObject("GM page rules canvas", typeof(RectTransform), typeof(Canvas));
        canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var host = canvasRoot.GetComponent<RectTransform>();
        host.sizeDelta = new Vector2(arguments.Value<float>("width"), arguments.Value<float>("height"));
        try
        {
            var console = controllerRoot.AddComponent<GMReflectionConsole>();
            console.gmPageHost = host;
            foreach (GmPageId groupId in new[]
                { GmPageId.Player, GmPageId.World, GmPageId.Spawn, GmPageId.GameEvents, GmPageId.Device })
                console.gmGroups.Add(groupId, new GmPageView { SelectedPageId = groupId });
            console.BuildPageGroups();
            foreach (GmPageId pageId in Enum.GetValues(typeof(GmPageId)))
            {
                GmPageView page = console.CreatePage(pageId);
                GameObject tallContent = CreateUiObject("Long page content", page.Content);
                tallContent.AddComponent<LayoutElement>().preferredHeight = 1600f;
            }

            void Rebuild(GmPageId pageId)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(host);
                LayoutRebuilder.ForceRebuildLayoutImmediate(console.gmPages[pageId].Content);
            }

            JObject Sample(GmPageId pageId)
            {
                int visiblePages = 0;
                foreach (GmPageView page in console.gmPages.Values)
                    if (page.Root.activeInHierarchy) visiblePages++;
                GmPageView selected = console.gmPages[pageId];
                var navigation = (RectTransform)console.gmGroups[GetPageGroup(pageId)].Root.transform.Find("Section Navigation");
                return new JObject
                {
                    ["page"] = pageId.ToString(), ["visiblePages"] = visiblePages,
                    ["selectedVisible"] = selected.Root.activeInHierarchy,
                    ["ownsScroll"] = selected.Scroll.gameObject == selected.Root,
                    ["inView"] = console.IsPageSectionInView(pageId),
                    ["contentHeight"] = selected.Content.rect.height,
                    ["viewportHeight"] = selected.Scroll.viewport.rect.height,
                    ["navigationHeight"] = navigation != null ? navigation.rect.height : 0f,
                    ["navigationFlexibleHeight"] = navigation != null ? LayoutUtility.GetFlexibleHeight(navigation) : 0f,
                    ["navigationButtonCount"] = navigation != null ? navigation.childCount : 0,
                    ["sharedTabCount"] = navigation != null ? navigation.GetComponentsInChildren<ReusableUITabVisual>().Length : 0
                };
            }

            var selections = new JArray();
            foreach (GmPageId pageId in Enum.GetValues(typeof(GmPageId)))
            {
                console.ApplyGmPageSelectionVisibility(pageId);
                Rebuild(pageId);
                selections.Add(Sample(pageId));
            }

            console.ApplyGmPageSelectionVisibility(GmPageId.World);
            Rebuild(GmPageId.World);
            GmPageView worldPage = console.gmPages[GmPageId.World];
            var navigation = (RectTransform)console.gmGroups[GmPageId.World].Root.transform.Find("Section Navigation");
            Bounds before = RectTransformUtility.CalculateRelativeRectTransformBounds(host, navigation);
            worldPage.Scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            Bounds after = RectTransformUtility.CalculateRelativeRectTransformBounds(host, navigation);
            Bounds viewport = RectTransformUtility.CalculateRelativeRectTransformBounds(host, worldPage.Scroll.viewport);
            float savedOffset = worldPage.Content.anchoredPosition.y;
            var navigationButtons = new JArray();
            foreach (Button button in navigation.GetComponentsInChildren<Button>())
            {
                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(host, button.transform);
                navigationButtons.Add(new JObject
                {
                    ["left"] = bounds.min.x, ["right"] = bounds.max.x,
                    ["bottom"] = bounds.min.y, ["top"] = bounds.max.y,
                    ["width"] = bounds.size.x, ["height"] = bounds.size.y,
                    ["sharedControl"] = button.GetComponent<ReusableUIControl>() != null,
                    ["sharedTabVisual"] = button.GetComponent<ReusableUITabVisual>() != null
                });
            }

            console.ApplyGmPageSelectionVisibility(GmPageId.Layers);
            Rebuild(GmPageId.Layers);
            JObject switched = Sample(GmPageId.Layers);
            switched["previousHidden"] = !worldPage.Root.activeInHierarchy;
            switched["previousInView"] = console.IsPageSectionInView(GmPageId.World);
            switched["newPageAtTop"] = console.gmPages[GmPageId.Layers].Scroll.verticalNormalizedPosition;
            console.ApplyGmPageSelectionVisibility(GmPageId.Player);
            console.ApplyGmPageSelectionVisibility(console.gmGroups[GmPageId.World].SelectedPageId);
            bool rememberedSecondaryPage = console.activeGmPage == GmPageId.Layers;
            console.ApplyGmPageSelectionVisibility(GmPageId.World);
            Rebuild(GmPageId.World);
            bool restoredOffset = savedOffset > 0f && Mathf.Abs(worldPage.Content.anchoredPosition.y - savedOffset) < 1f;

            // 搜索共用正式目标滚动入口，直接取当前页中的第二条内容检验定位。
            GameObject searchTarget = CreateUiObject("Search target", worldPage.Content);
            searchTarget.AddComponent<LayoutElement>().preferredHeight = 48f;
            Rebuild(GmPageId.World);
            ScrollToPageTarget(worldPage, searchTarget.GetComponent<RectTransform>());
            Bounds target = RectTransformUtility.CalculateRelativeRectTransformBounds(worldPage.Scroll.viewport, searchTarget.transform);
            return new JObject
            {
                ["selections"] = selections, ["switched"] = switched,
                ["navigationOutsideScroll"] = !navigation.IsChildOf(worldPage.Scroll.transform),
                ["navigationMovement"] = (before.center - after.center).sqrMagnitude,
                ["navigationBottom"] = after.min.y, ["viewportTop"] = viewport.max.y,
                ["hostLeft"] = host.rect.xMin, ["hostRight"] = host.rect.xMax,
                ["hostTop"] = host.rect.yMax, ["navigationButtons"] = navigationButtons,
                ["restoredOffset"] = restoredOffset,
                ["rememberedSecondaryPage"] = rememberedSecondaryPage,
                ["deviceNavigationCount"] = console.gmGroups[GmPageId.Device].Root.GetComponentsInChildren<Button>(true).Length,
                ["searchTargetVisible"] = target.min.y >= worldPage.Scroll.viewport.rect.yMin - 1f &&
                    target.max.y <= worldPage.Scroll.viewport.rect.yMax + 1f
            };
        }
        finally
        {
            DestroyImmediate(canvasRoot);
            DestroyImmediate(controllerRoot);
        }
    }

    #endregion
}
#endif
