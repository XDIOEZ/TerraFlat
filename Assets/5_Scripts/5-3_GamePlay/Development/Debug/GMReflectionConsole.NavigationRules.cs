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
            // 视口只测自身矩形，递归包围盒会把已滚到外面的长内容也算进来。
            var viewportCorners = new Vector3[4];
            worldPage.Scroll.viewport.GetWorldCorners(viewportCorners);
            var viewport = new Bounds(host.InverseTransformPoint(viewportCorners[0]), Vector3.zero);
            for (int index = 1; index < viewportCorners.Length; index++)
                viewport.Encapsulate(host.InverseTransformPoint(viewportCorners[index]));
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
            GameObject buffScrollRoot = CreateUiObject("Buff scroll rules root", host);
            Transform buffContent = ConfigureBuffTargetScroll(buffScrollRoot, 7f, out ScrollRect buffScroll);
            GameObject catalogHost = CreateUiObject("Catalog scroll rules host", host);
            Transform catalogContent = CreateCatalogScrollGrid(catalogHost.transform, "Catalog scroll");
            ScrollRect catalogScroll = catalogHost.transform.Find("Catalog scroll").GetComponent<ScrollRect>();
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
                    target.max.y <= worldPage.Scroll.viewport.rect.yMax + 1f,
                ["buffTargetScroll"] = CaptureGmScrollConfiguration(buffScroll, buffContent),
                ["catalogScroll"] = CaptureGmScrollConfiguration(catalogScroll, catalogContent)
            };
        }
        finally
        {
            DestroyImmediate(canvasRoot);
            DestroyImmediate(controllerRoot);
        }
    }

    private static JObject CaptureGmScrollConfiguration(ScrollRect scroll, Transform content)
    {
        RectTransform scrollbarRect = scroll.verticalScrollbar.GetComponent<RectTransform>();
        RectTransform slidingRect = scrollbarRect.Find("Sliding Area").GetComponent<RectTransform>();
        RectTransform handleRect = scroll.verticalScrollbar.handleRect;
        Image track = scrollbarRect.GetComponent<Image>();
        Image handle = handleRect.GetComponent<Image>();
        VerticalLayoutGroup vertical = content.GetComponent<VerticalLayoutGroup>();
        GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        return new JObject
        {
            ["itemStepScrollRect"] = scroll is ItemStepScrollRect,
            ["horizontal"] = scroll.horizontal, ["vertical"] = scroll.vertical,
            ["clamped"] = scroll.movementType == ScrollRect.MovementType.Clamped,
            ["elastic"] = scroll.movementType == ScrollRect.MovementType.Elastic,
            ["inertia"] = scroll.inertia, ["sensitivity"] = scroll.scrollSensitivity,
            ["viewportMasked"] = scroll.viewport.GetComponent<RectMask2D>() != null,
            ["viewportMinX"] = scroll.viewport.offsetMin.x, ["viewportMinY"] = scroll.viewport.offsetMin.y,
            ["viewportMaxX"] = scroll.viewport.offsetMax.x, ["viewportMaxY"] = scroll.viewport.offsetMax.y,
            ["verticalLayout"] = vertical != null, ["layoutSpacing"] = vertical != null ? vertical.spacing : -1f,
            ["paddingLeft"] = vertical != null ? vertical.padding.left : -1,
            ["paddingRight"] = vertical != null ? vertical.padding.right : -1,
            ["paddingTop"] = vertical != null ? vertical.padding.top : -1,
            ["paddingBottom"] = vertical != null ? vertical.padding.bottom : -1,
            ["expandHeight"] = vertical != null && vertical.childForceExpandHeight,
            ["gridLayout"] = grid != null,
            ["gridFixedColumns"] = grid != null && grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount,
            ["gridColumns"] = grid != null ? grid.constraintCount : -1,
            ["gridCellX"] = grid != null ? grid.cellSize.x : -1f,
            ["gridCellY"] = grid != null ? grid.cellSize.y : -1f,
            ["gridSpacingX"] = grid != null ? grid.spacing.x : -1f,
            ["gridSpacingY"] = grid != null ? grid.spacing.y : -1f,
            ["gridPaddingLeft"] = grid != null ? grid.padding.left : -1,
            ["gridPaddingRight"] = grid != null ? grid.padding.right : -1,
            ["gridPaddingTop"] = grid != null ? grid.padding.top : -1,
            ["gridPaddingBottom"] = grid != null ? grid.padding.bottom : -1,
            ["preferredHeightFit"] = fitter.verticalFit == ContentSizeFitter.FitMode.PreferredSize,
            ["scrollbarAutoHide"] = scroll.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHide,
            ["scrollbarBottomToTop"] = scroll.verticalScrollbar.direction == Scrollbar.Direction.BottomToTop,
            ["scrollbarNodesPreserved"] = scrollbarRect.name == "Scrollbar" && slidingRect.name == "Sliding Area" &&
                handleRect.name == "Handle" && scroll.verticalScrollbar.targetGraphic == handle,
            ["slidingInsetPreserved"] = slidingRect.offsetMin == new Vector2(2f, 2f) &&
                slidingRect.offsetMax == new Vector2(-2f, -2f),
            ["handleAnchorsPreserved"] = handleRect.anchorMin == Vector2.zero && handleRect.anchorMax == Vector2.one &&
                handleRect.offsetMin == Vector2.zero && handleRect.offsetMax == Vector2.zero,
            ["scrollbarMinX"] = scrollbarRect.offsetMin.x, ["scrollbarMinY"] = scrollbarRect.offsetMin.y,
            ["scrollbarMaxX"] = scrollbarRect.offsetMax.x, ["scrollbarMaxY"] = scrollbarRect.offsetMax.y,
            ["trackColor"] = new JArray(track.color.r, track.color.g, track.color.b, track.color.a),
            ["handleColor"] = new JArray(handle.color.r, handle.color.g, handle.color.b, handle.color.a)
        };
    }

    #endregion
}
#endif
