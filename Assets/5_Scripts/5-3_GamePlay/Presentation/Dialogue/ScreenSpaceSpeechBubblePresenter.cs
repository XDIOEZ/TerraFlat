using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 在 UIManager.PanelRoot 下动态创建屏幕空间气泡，并持续跟随角色头顶。
    /// 气泡固定处于交互面板下方，不接收射线、不参与输入，也不依赖具体台词来源。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenSpaceSpeechBubblePresenter :
        MonoBehaviour,
        ICharacterSpeechPresenter
    {
        [Header("跟随")]
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.25f, 0f);
        [SerializeField, Min(0f)] private float safeMargin = 18f;

        [Header("尺寸（基于 1920x1080 Canvas）")]
        [SerializeField, Min(80f)] private float minWidth = 170f;
        [SerializeField, Min(100f)] private float maxWidth = 360f;
        [SerializeField, Min(30f)] private float minHeight = 58f;
        [SerializeField, Min(40f)] private float maxHeight = 132f;
        [SerializeField, Min(0f)] private float horizontalPadding = 18f;
        [SerializeField, Min(0f)] private float verticalPadding = 12f;
        [SerializeField, Min(0.01f)] private float fadeDuration = 0.22f;

        private GameObject viewObject;
        private RectTransform viewRect;
        private RectTransform rootRect;
        private Canvas rootCanvas;
        private CanvasGroup canvasGroup;
        private TextMeshProUGUI label;
        private Coroutine hideRoutine;
        private Camera worldCamera;
        private bool isVisible;
        private CharacterSpeechPriority visiblePriority;

        public bool IsVisible => isVisible;
        public CharacterSpeechPriority VisiblePriority => visiblePriority;

        private void LateUpdate()
        {
            if (viewObject == null || !isVisible)
                return;

            // 设置、背包等模态面板打开时，非交互气泡直接隐藏，避免任何 Canvas 顺序异常造成遮挡。
            if (IsBlockedByGameplayModalPanel())
            {
                HideImmediate();
                return;
            }

            UpdateScreenPosition();
        }

        private void OnDisable()
        {
            HideImmediate();
        }

        private void OnDestroy()
        {
            if (viewObject != null)
                Destroy(viewObject);
        }

        public bool Show(CharacterSpeechRequest request)
        {
            if (request == null || !request.IsValid || IsBlockedByGameplayModalPanel() || !EnsureView())
                return false;

            if (hideRoutine != null)
                StopCoroutine(hideRoutine);

            label.text = request.Text.Trim();
            visiblePriority = request.Priority;
            isVisible = true;
            viewObject.SetActive(true);
            PlaceBelowInteractivePanels();
            UpdateLayout();
            if (!UpdateScreenPosition())
            {
                HideImmediate();
                return false;
            }

            canvasGroup.alpha = 1f;
            hideRoutine = StartCoroutine(HideAfterDelay(Mathf.Max(0.1f, request.Duration)));
            return true;
        }

        public void HideImmediate()
        {
            if (hideRoutine != null)
            {
                StopCoroutine(hideRoutine);
                hideRoutine = null;
            }

            isVisible = false;
            visiblePriority = CharacterSpeechPriority.Ambient;

            if (canvasGroup != null)
                canvasGroup.alpha = 0f;
            if (viewObject != null)
                viewObject.SetActive(false);
        }

private bool EnsureView()
        {
            Transform panelRoot = UIManager.Instance?.panelRoot;
            if (panelRoot == null)
                return false;

            RectTransform currentRootRect = panelRoot as RectTransform;
            if (currentRootRect == null)
                currentRootRect = panelRoot.GetComponent<RectTransform>();
            if (currentRootRect == null)
                return false;

            if (viewObject != null)
            {
                if (viewRect.parent != currentRootRect)
                    viewRect.SetParent(currentRootRect, false);
                rootRect = currentRootRect;
                rootCanvas = rootRect.GetComponentInParent<Canvas>();
                PlaceBelowInteractivePanels();
                return true;
            }

            GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.CharacterSpeechBubble);
            if (prefab == null)
            {
                Debug.LogError(
                    $"[ScreenSpaceSpeechBubblePresenter] 缺少 Prefab：{RuntimeUIPrefabKeys.CharacterSpeechBubble}。",
                    this);
                return false;
            }

            rootRect = currentRootRect;
            rootCanvas = rootRect.GetComponentInParent<Canvas>();
            viewObject = Instantiate(prefab, rootRect, false);
            viewObject.name = RuntimeUIPrefabKeys.CharacterSpeechBubble;
            viewRect = viewObject.GetComponent<RectTransform>();
            canvasGroup = viewObject.GetComponent<CanvasGroup>();
            label = FindMessageLabel(viewObject.transform);
            if (viewRect == null || canvasGroup == null || label == null)
            {
                Debug.LogError("[ScreenSpaceSpeechBubblePresenter] 气泡 Prefab 控件命名契约不完整。", viewObject);
                Destroy(viewObject);
                viewObject = null;
                return false;
            }

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            PlaceBelowInteractivePanels();
            viewObject.SetActive(false);
            return true;
        }

        /// <summary>
        /// 气泡是非交互提示，固定在 PanelRoot 最底层，确保背包和模态面板始终覆盖它。
        /// </summary>
        private void PlaceBelowInteractivePanels()
        {
            if (viewRect == null || viewRect.parent == null || viewRect.GetSiblingIndex() == 0)
                return;

            viewRect.SetAsFirstSibling();
        }

        /// <summary>判断当前是否存在必须阻断玩法输入的模态面板。</summary>
        private static bool IsBlockedByGameplayModalPanel()
        {
            UIManager manager = UIManager.ExistingInstance;
            return manager != null && manager.HasOpenGameplayInputBlockingPanel();
        }





        private void UpdateLayout()
        {
            float minimumWidth = Mathf.Min(minWidth, maxWidth);
            float maximumWidth = Mathf.Max(minWidth, maxWidth);
            float availableTextWidth = Mathf.Max(
                1f,
                maximumWidth - horizontalPadding * 2f);

            Vector2 singleLine = label.GetPreferredValues(label.text);
            float width = Mathf.Clamp(
                singleLine.x + horizontalPadding * 2f,
                minimumWidth,
                maximumWidth);

            Vector2 wrapped = label.GetPreferredValues(
                label.text,
                Mathf.Min(availableTextWidth, width - horizontalPadding * 2f),
                0f);
            float height = Mathf.Clamp(
                wrapped.y + verticalPadding * 2f,
                Mathf.Min(minHeight, maxHeight),
                Mathf.Max(minHeight, maxHeight));

            viewRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            viewRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(viewRect);
        }

        private bool UpdateScreenPosition()
        {
            if (viewObject == null || rootRect == null)
            return false;

            if (worldCamera == null)
                worldCamera = Camera.main;
            if (worldCamera == null)
            {
                viewObject.SetActive(false);
                return false;
            }

            Vector3 screenPoint =
                worldCamera.WorldToScreenPoint(transform.position + worldOffset);
            bool onScreen = screenPoint.z >= 0f &&
                            screenPoint.x >= 0f &&
                            screenPoint.x <= Screen.width &&
                            screenPoint.y >= 0f &&
                            screenPoint.y <= Screen.height;
            if (!onScreen)
            {
                viewObject.SetActive(false);
                return false;
            }

            viewObject.SetActive(true);
            Camera canvasCamera =
                rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? rootCanvas.worldCamera
                    : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rootRect,
                    screenPoint,
                    canvasCamera,
                    out Vector2 localPoint))
            {
                return false;
            }

            Rect canvasBounds = rootRect.rect;
            float halfWidth = viewRect.rect.width * 0.5f;
            float height = viewRect.rect.height;
            localPoint.x = Mathf.Clamp(
                localPoint.x,
                canvasBounds.xMin + halfWidth + safeMargin,
                canvasBounds.xMax - halfWidth - safeMargin);
            localPoint.y = Mathf.Clamp(
                localPoint.y,
                canvasBounds.yMin + safeMargin,
                canvasBounds.yMax - height - safeMargin);
            viewRect.anchoredPosition = localPoint;
            return true;
        }

        private IEnumerator HideAfterDelay(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);

            float elapsed = 0f;
            float total = Mathf.Max(0.01f, fadeDuration);
            while (elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;
                if (canvasGroup != null)
                    canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / total);
                yield return null;
            }

            hideRoutine = null;
            HideImmediate();
        }
    

private static TextMeshProUGUI FindMessageLabel(Transform root)
        {
            TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].name == "Message")
                    return texts[i];
            }

            return null;
        }
}
}
