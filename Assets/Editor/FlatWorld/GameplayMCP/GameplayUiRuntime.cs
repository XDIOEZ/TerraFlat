using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// GamePlayMCP 的 UI 语义适配器。
    /// 只观察当前激活 Canvas，并通过 EventSystem 的真实射线与 Pointer 事件链点击，不直接调用按钮业务回调。
    /// </summary>
    internal static class GameplayUiRuntime
    {
        private const int DefaultNodeLimit = 64;
        private const int MaximumNodeLimit = 128;
        private const int MaximumTextLength = 120;
        private static readonly Vector2[] ClickCandidates =
        {
            new(0.5f, 0.5f),
            new(0.25f, 0.5f),
            new(0.75f, 0.5f),
            new(0.5f, 0.25f),
            new(0.5f, 0.75f),
            new(0.25f, 0.25f),
            new(0.75f, 0.25f),
            new(0.25f, 0.75f),
            new(0.75f, 0.75f)
        };

        #region UI 树

        /// <summary>构建当前所有激活根 Canvas 的紧凑语义树，并支持分页限制上下文大小。</summary>
        public static object BuildTree(JObject parameters)
        {
            if (!Application.isPlaying)
                return new ErrorResponse("not_playing: Unity 必须处于 Play Mode 才能读取运行时 UI。");

            int offset = Mathf.Max(0, ReadInt(parameters, "offset", 0));
            int limit = Mathf.Clamp(ReadInt(parameters, "limit", DefaultNodeLimit), 1, MaximumNodeLimit);
            bool includeText = ReadBool(parameters, "includeText", true);
            bool interactiveOnly = ReadBool(parameters, "interactiveOnly", false);

            Canvas[] rootCanvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true)
                .Where(IsQueryableRootCanvas)
                .OrderByDescending(canvas => canvas.sortingOrder)
                .ThenBy(canvas => BuildPath(canvas.transform), StringComparer.Ordinal)
                .ToArray();

            var nodes = new List<JObject>(Math.Min(MaximumNodeLimit * 2, 256));
            for (int i = 0; i < rootCanvases.Length; i++)
            {
                Canvas canvas = rootCanvases[i];
                int rootId = canvas.gameObject.GetInstanceID();
                nodes.Add(BuildNode(canvas.gameObject, null, 0, "canvas"));
                TraverseSemanticChildren(
                    canvas.transform,
                    rootId,
                    1,
                    includeText,
                    interactiveOnly,
                    nodes);
            }

            int totalCount = nodes.Count;
            JObject[] page = nodes.Skip(offset).Take(limit).ToArray();
            bool truncated = offset + page.Length < totalCount;
            Scene activeScene = SceneManager.GetActiveScene();

            return new SuccessResponse("FlatWorld live semantic UI tree.", new
            {
                action = "tree",
                scene = activeScene.IsValid() ? activeScene.name : string.Empty,
                root_canvas_count = rootCanvases.Length,
                total_count = totalCount,
                offset,
                returned_count = page.Length,
                limit,
                truncated,
                next_offset = truncated ? offset + page.Length : (int?)null,
                semantic_tree = new JArray(page),
                hint = "Use targetId from a clickable node with gameplay_ui(action=click), then query tree again because UI may have changed."
            });
        }

        /// <summary>递归投影语义节点；纯布局 Transform 被压缩，但完整层级仍保留在 path 中。</summary>
        private static void TraverseSemanticChildren(
            Transform parent,
            int semanticParentId,
            int semanticDepth,
            bool includeText,
            bool interactiveOnly,
            ICollection<JObject> result)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy || !IsVisibleThroughCanvasGroups(child))
                    continue;

                int nextParentId = semanticParentId;
                int nextDepth = semanticDepth;
                if (TryResolveSemanticKind(child.gameObject, includeText, interactiveOnly, out string kind))
                {
                    JObject node = BuildNode(child.gameObject, semanticParentId, semanticDepth, kind);
                    result.Add(node);
                    nextParentId = child.gameObject.GetInstanceID();
                    nextDepth = semanticDepth + 1;
                }

                TraverseSemanticChildren(
                    child,
                    nextParentId,
                    nextDepth,
                    includeText,
                    interactiveOnly,
                    result);
            }
        }

        /// <summary>识别 AI 真正需要理解的交互控件、滚动容器、Canvas 与独立可见文本。</summary>
        private static bool TryResolveSemanticKind(
            GameObject target,
            bool includeText,
            bool interactiveOnly,
            out string kind)
        {
            kind = ResolveControlKind(target);
            bool clickable = IsClickCapable(target);
            if (interactiveOnly)
                return clickable;

            if (!string.IsNullOrEmpty(kind))
                return true;

            if (!includeText || HasClickableAncestor(target.transform))
                return false;

            if (!TryGetOwnText(target, out string text) || string.IsNullOrWhiteSpace(text))
                return false;

            kind = "text";
            return true;
        }

        /// <summary>根据现有 Unity UI 类型和通用 Pointer Handler 得到稳定的语义类型。</summary>
        private static string ResolveControlKind(GameObject target)
        {
            if (target.GetComponent<Canvas>() != null)
                return "canvas";
            if (target.GetComponent<ItemSlot_UI>() != null)
                return "item_slot";
            if (target.GetComponent<Button>() != null)
                return "button";
            if (target.GetComponent<Toggle>() != null)
                return "toggle";
            if (target.GetComponent<Slider>() != null)
                return "slider";
            if (target.GetComponent<Scrollbar>() != null)
                return "scrollbar";
            if (target.GetComponent<TMP_Dropdown>() != null || target.GetComponent<Dropdown>() != null)
                return "dropdown";
            if (target.GetComponent<TMP_InputField>() != null || target.GetComponent<InputField>() != null)
                return "input";
            if (target.GetComponent<Selectable>() != null)
                return "selectable";
            if (HasPointerHandler(target))
                return "pointer";
            if (target.GetComponent<ScrollRect>() != null)
                return "scroll";
            return string.Empty;
        }

        /// <summary>将一个语义 UI 节点压缩为适合模型上下文的结构化数据。</summary>
        private static JObject BuildNode(GameObject target, int? parentId, int depth, string kind)
        {
            bool clickable = IsClickCapable(target);
            var node = new JObject
            {
                ["id"] = target.GetInstanceID(),
                ["parent"] = parentId.HasValue ? new JValue(parentId.Value) : JValue.CreateNull(),
                ["depth"] = depth,
                ["name"] = target.name,
                ["type"] = kind,
                ["path"] = BuildPath(target.transform),
                ["clickable"] = clickable
            };

            if (clickable)
                node["interactable"] = IsInteractable(target);

            string text = ResolveDisplayText(target, kind);
            if (!string.IsNullOrEmpty(text))
                node["text"] = text;

            if (TryGetScreenRect(target, out Rect screenRect))
            {
                node["rect"] = new JArray(
                    Round(screenRect.x),
                    Round(screenRect.y),
                    Round(screenRect.width),
                    Round(screenRect.height));
                node["onScreen"] = screenRect.xMax >= 0f &&
                                   screenRect.yMax >= 0f &&
                                   screenRect.xMin <= Screen.width &&
                                   screenRect.yMin <= Screen.height;
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
                node["selected"] = eventSystem.currentSelectedGameObject == target;

            AppendControlState(target, node);
            return node;
        }

        /// <summary>补充少量会直接影响 AI 选择的控件状态，不展开完整组件数据。</summary>
        private static void AppendControlState(GameObject target, JObject node)
        {
            ItemSlot_UI slot = target.GetComponent<ItemSlot_UI>();
            if (slot != null)
            {
                node["slotIndex"] = slot.slotIndex;
                string iconName = slot.image?.sprite?.name;
                if (!string.IsNullOrEmpty(iconName))
                    node["icon"] = iconName;
            }

            if (target.GetComponent<Toggle>() is { } toggle)
                node["value"] = toggle.isOn;
            else if (target.GetComponent<Slider>() is { } slider)
                node["value"] = Round(slider.value);
            else if (target.GetComponent<Scrollbar>() is { } scrollbar)
                node["value"] = Round(scrollbar.value);
            else if (target.GetComponent<TMP_Dropdown>() is { } tmpDropdown)
                node["value"] = tmpDropdown.value;
            else if (target.GetComponent<Dropdown>() is { } dropdown)
                node["value"] = dropdown.value;
            else if (target.GetComponent<TMP_InputField>() is { } tmpInput)
                node["value"] = NormalizeText(tmpInput.text);
            else if (target.GetComponent<InputField>() is { } input)
                node["value"] = NormalizeText(input.text);
        }

        #endregion

        #region UI 点击

        /// <summary>按 UI 树返回的运行时 ID 查找目标，并通过真实 EventSystem 射线执行左键点击。</summary>
        public static object Click(JObject parameters)
        {
            if (!Application.isPlaying)
                return new ErrorResponse("not_playing: Unity 必须处于 Play Mode 才能点击运行时 UI。");

            int targetId = ReadInt(parameters, "targetId", 0);
            if (targetId == 0)
                return new ErrorResponse("missing_ui_target: action=click 需要 targetId。");

            GameObject target = EditorUtility.InstanceIDToObject(targetId) as GameObject;
            if (!IsQueryableRuntimeObject(target))
                return new ErrorResponse($"ui_target_not_found: 找不到当前运行时 UI 节点 id={targetId}，请重新读取 UI 树。");
            if (!target.activeInHierarchy || !IsVisibleThroughCanvasGroups(target.transform))
                return new ErrorResponse($"ui_target_hidden: UI 节点 id={targetId} 当前不可见，请重新读取 UI 树。");
            if (!IsClickCapable(target))
                return new ErrorResponse($"ui_target_not_clickable: UI 节点 id={targetId} 不是可点击控件。");
            if (!IsInteractable(target))
                return new ErrorResponse($"ui_target_disabled: UI 节点 id={targetId} 当前不可交互。");

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return new ErrorResponse("event_system_missing: 当前没有可用 EventSystem。");

            string targetPath = BuildPath(target.transform);
            string targetName = target.name;
            string targetKind = ResolveControlKind(target);
            if (!TryFindClickableRaycast(
                    eventSystem,
                    target,
                    out Vector2 clickPosition,
                    out RaycastResult raycast,
                    out string blocker))
            {
                string suffix = string.IsNullOrEmpty(blocker) ? string.Empty : $" 顶层命中：{blocker}。";
                return new ErrorResponse(
                    $"ui_target_blocked: 目标当前没有玩家可点击的可见区域。{suffix}");
            }

            var eventData = new PointerEventData(eventSystem)
            {
                pointerId = -1,
                position = clickPosition,
                pressPosition = clickPosition,
                delta = Vector2.zero,
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                clickTime = Time.unscaledTime,
                eligibleForClick = true,
                pointerCurrentRaycast = raycast,
                pointerPressRaycast = raycast
            };

            GameObject hitObject = raycast.gameObject;
            GameObject downHandler = ExecuteEvents.ExecuteHierarchy(
                hitObject,
                eventData,
                ExecuteEvents.pointerDownHandler);
            GameObject upHandler = hitObject != null
                ? ExecuteEvents.ExecuteHierarchy(hitObject, eventData, ExecuteEvents.pointerUpHandler)
                : null;
            GameObject clickHandler = hitObject != null
                ? ExecuteEvents.ExecuteHierarchy(hitObject, eventData, ExecuteEvents.pointerClickHandler)
                : null;

            bool handled = downHandler != null || upHandler != null || clickHandler != null;
            return new SuccessResponse("FlatWorld UI click completed.", new
            {
                action = "click",
                target_id = targetId,
                target_name = targetName,
                target_type = targetKind,
                target_path = targetPath,
                screen_position = new[] { Round(clickPosition.x), Round(clickPosition.y) },
                handled,
                hint = "Query gameplay_ui(action=tree) again before choosing the next UI action."
            });
        }

        /// <summary>在控件矩形内尝试多个点击点，只接受真实射线最上层解析回目标控件的点。</summary>
        private static bool TryFindClickableRaycast(
            EventSystem eventSystem,
            GameObject target,
            out Vector2 clickPosition,
            out RaycastResult hit,
            out string blocker)
        {
            clickPosition = default;
            hit = default;
            blocker = string.Empty;
            if (!TryGetScreenRect(target, out Rect rect) || rect.width <= 0.01f || rect.height <= 0.01f)
                return false;

            var results = new List<RaycastResult>(16);
            for (int i = 0; i < ClickCandidates.Length; i++)
            {
                Vector2 normalized = ClickCandidates[i];
                Vector2 candidate = new(
                    Mathf.Lerp(rect.xMin, rect.xMax, normalized.x),
                    Mathf.Lerp(rect.yMin, rect.yMax, normalized.y));
                if (candidate.x < 0f || candidate.y < 0f ||
                    candidate.x > Screen.width || candidate.y > Screen.height)
                    continue;

                var probe = new PointerEventData(eventSystem)
                {
                    pointerId = -1,
                    position = candidate,
                    delta = Vector2.zero,
                    button = PointerEventData.InputButton.Left
                };
                results.Clear();
                eventSystem.RaycastAll(probe, results);
                if (results.Count == 0)
                    continue;

                RaycastResult top = results[0];
                GameObject resolvedTarget = FindNearestClickTarget(top.gameObject);
                if (resolvedTarget == target)
                {
                    clickPosition = candidate;
                    hit = top;
                    return true;
                }

                if (string.IsNullOrEmpty(blocker) && top.gameObject != null)
                    blocker = BuildPath(top.gameObject.transform);
            }

            return false;
        }

        /// <summary>从真实射线命中物向上查找最近的可点击语义控件，避免误点被嵌套控件覆盖的父节点。</summary>
        private static GameObject FindNearestClickTarget(GameObject hit)
        {
            Transform current = hit != null ? hit.transform : null;
            while (current != null)
            {
                if (IsClickCapable(current.gameObject))
                    return current.gameObject;
                current = current.parent;
            }

            return null;
        }

        #endregion

        #region 状态辅助

        /// <summary>判断根 Canvas 是否属于当前已加载运行时场景且真实激活。</summary>
        private static bool IsQueryableRootCanvas(Canvas canvas)
        {
            return canvas != null &&
                   canvas.isRootCanvas &&
                   canvas.isActiveAndEnabled &&
                   canvas.gameObject.activeInHierarchy &&
                   IsQueryableRuntimeObject(canvas.gameObject) &&
                   IsVisibleThroughCanvasGroups(canvas.transform);
        }

        /// <summary>过滤 Prefab 资产、未加载场景对象和已经销毁的对象。</summary>
        private static bool IsQueryableRuntimeObject(GameObject target)
        {
            return target != null &&
                   target.scene.IsValid() &&
                   target.scene.isLoaded &&
                   target.transform is RectTransform &&
                   target.GetComponentInParent<Canvas>() != null;
        }

        /// <summary>判断当前对象是否具有左键 Pointer/Selectable 语义。</summary>
        private static bool IsClickCapable(GameObject target)
        {
            return target != null &&
                   (target.GetComponent<Selectable>() != null || HasPointerHandler(target));
        }

        /// <summary>判断当前点击目标的实际组件与父级 CanvasGroup 是否允许交互。</summary>
        private static bool IsInteractable(GameObject target)
        {
            if (target == null || !target.activeInHierarchy || !IsVisibleThroughCanvasGroups(target.transform))
                return false;

            Selectable selectable = target.GetComponent<Selectable>();
            if (selectable != null && (!selectable.isActiveAndEnabled || !selectable.IsInteractable()))
                return false;

            bool hasActivePointerHandler = selectable != null;
            MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null && behaviour.isActiveAndEnabled &&
                    behaviour is IPointerDownHandler or IPointerUpHandler or IPointerClickHandler)
                {
                    hasActivePointerHandler = true;
                }
            }

            if (!hasActivePointerHandler)
                return false;

            CanvasGroup[] groups = target.GetComponentsInParent<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null)
                    continue;
                if (!group.interactable || !group.blocksRaycasts)
                    return false;
                if (group.ignoreParentGroups)
                    break;
            }

            return true;
        }

        /// <summary>判断对象自身是否挂有通用 Pointer 点击链处理器。</summary>
        private static bool HasPointerHandler(GameObject target)
        {
            if (target == null)
                return false;

            MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour is IPointerDownHandler or IPointerUpHandler or IPointerClickHandler)
                    return true;
            }

            return false;
        }

        /// <summary>文本位于已有可点击控件内部时不单独暴露，避免按钮标签重复占用上下文。</summary>
        private static bool HasClickableAncestor(Transform target)
        {
            Transform current = target?.parent;
            while (current != null)
            {
                if (IsClickCapable(current.gameObject))
                    return true;
                if (current.GetComponent<Canvas>() != null)
                    break;
                current = current.parent;
            }

            return false;
        }

        /// <summary>过滤被零透明 CanvasGroup 隐藏的 UI 分支。</summary>
        private static bool IsVisibleThroughCanvasGroups(Transform target)
        {
            Transform current = target;
            while (current != null)
            {
                CanvasGroup group = current.GetComponent<CanvasGroup>();
                if (group != null && group.alpha <= 0.001f)
                    return false;
                current = current.parent;
            }

            return true;
        }

        /// <summary>读取节点自身的 TMP 或旧式 Text，不递归读取整个面板。</summary>
        private static bool TryGetOwnText(GameObject target, out string text)
        {
            TMP_Text tmp = target.GetComponent<TMP_Text>();
            if (tmp != null && tmp.isActiveAndEnabled)
            {
                text = NormalizeText(tmp.text);
                return !string.IsNullOrEmpty(text);
            }

            Text legacy = target.GetComponent<Text>();
            if (legacy != null && legacy.isActiveAndEnabled)
            {
                text = NormalizeText(legacy.text);
                return !string.IsNullOrEmpty(text);
            }

            text = string.Empty;
            return false;
        }

        /// <summary>读取控件自身或其可见子级中的少量文本，供 Agent 根据语义选择按钮。</summary>
        private static string ResolveDisplayText(GameObject target, string kind)
        {
            if (string.Equals(kind, "text", StringComparison.Ordinal))
                return TryGetOwnText(target, out string ownText) ? ownText : string.Empty;
            if (string.Equals(kind, "canvas", StringComparison.Ordinal) ||
                string.Equals(kind, "scroll", StringComparison.Ordinal))
                return string.Empty;

            var values = new List<string>(3);
            TMP_Text[] tmpTexts = target.GetComponentsInChildren<TMP_Text>(false);
            for (int i = 0; i < tmpTexts.Length && values.Count < 3; i++)
                AddDistinctText(values, tmpTexts[i]?.text);

            if (values.Count == 0)
            {
                Text[] legacyTexts = target.GetComponentsInChildren<Text>(false);
                for (int i = 0; i < legacyTexts.Length && values.Count < 3; i++)
                    AddDistinctText(values, legacyTexts[i]?.text);
            }

            return NormalizeText(string.Join(" | ", values));
        }

        /// <summary>添加规范化且不重复的 UI 文本。</summary>
        private static void AddDistinctText(ICollection<string> values, string raw)
        {
            string normalized = NormalizeText(raw);
            if (string.IsNullOrEmpty(normalized) || values.Contains(normalized))
                return;
            values.Add(normalized);
        }

        /// <summary>压缩换行与连续空白，并限制单节点文本长度。</summary>
        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = string.Join(
                " ",
                value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            return normalized.Length <= MaximumTextLength
                ? normalized
                : normalized.Substring(0, MaximumTextLength - 1) + "…";
        }

        /// <summary>把 RectTransform 的四角投影为 Game View 屏幕矩形。</summary>
        private static bool TryGetScreenRect(GameObject target, out Rect rect)
        {
            rect = default;
            if (target == null || target.transform is not RectTransform rectTransform)
                return false;

            Canvas canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null)
                return false;

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera != null ? canvas.worldCamera : Camera.main;

            Vector2 first = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            float minX = first.x;
            float maxX = first.x;
            float minY = first.y;
            float maxY = first.y;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }

            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        /// <summary>生成完整 Transform 路径，解决重复按钮名无法凭文本区分的问题。</summary>
        private static string BuildPath(Transform target)
        {
            if (target == null)
                return string.Empty;

            var names = new List<string>(12);
            Transform current = target;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        /// <summary>读取整型 MCP 参数。</summary>
        private static int ReadInt(JObject parameters, string key, int fallback)
        {
            return int.TryParse(parameters?[key]?.ToString(), out int value) ? value : fallback;
        }

        /// <summary>读取布尔 MCP 参数。</summary>
        private static bool ReadBool(JObject parameters, string key, bool fallback)
        {
            return bool.TryParse(parameters?[key]?.ToString(), out bool value) ? value : fallback;
        }

        /// <summary>限制协议中的浮点精度，避免 UI 坐标制造无意义 Token。</summary>
        private static float Round(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        #endregion
    }
}
