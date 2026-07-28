using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 在 UIManager.PanelRoot 下动态创建屏幕空间气泡，并持续跟随角色头顶。
    /// 不接收射线，不参与输入，也不依赖具体台词来源。
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
            if (viewObject != null && isVisible)
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
            if (request == null || !request.IsValid || !EnsureView())
            return false;

            if (hideRoutine != null)
                StopCoroutine(hideRoutine);

            label.text = request.Text.Trim();
            visiblePriority = request.Priority;
            isVisible = true;
            viewObject.SetActive(true);
            viewRect.SetAsLastSibling();
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
                return true;
            }

            rootRect = currentRootRect;
            rootCanvas = rootRect.GetComponentInParent<Canvas>();

            viewObject = new GameObject(
                "CharacterSpeechBubble",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image),
                typeof(Shadow));
            viewRect = viewObject.GetComponent<RectTransform>();
            viewRect.SetParent(rootRect, false);
            viewRect.anchorMin = new Vector2(0.5f, 0.5f);
            viewRect.anchorMax = new Vector2(0.5f, 0.5f);
            viewRect.pivot = new Vector2(0.5f, 0f);

            Image background = viewObject.GetComponent<Image>();
            background.color = new Color(0.08f, 0.09f, 0.10f, 0.94f);
            background.raycastTarget = false;

            Shadow shadow = viewObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
            shadow.effectDistance = new Vector2(3f, -3f);
            shadow.useGraphicAlpha = true;

            canvasGroup = viewObject.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.ignoreParentGroups = false;

            CreateTail();
            CreateLabel();
            viewObject.SetActive(false);
            return true;
        }

        private void CreateTail()
        {
            GameObject tailObject = new GameObject(
                "Tail",
                typeof(RectTransform),
                typeof(Image));
            RectTransform tailRect = tailObject.GetComponent<RectTransform>();
            tailRect.SetParent(viewRect, false);
            tailRect.anchorMin = new Vector2(0.5f, 0f);
            tailRect.anchorMax = new Vector2(0.5f, 0f);
            tailRect.pivot = new Vector2(0.5f, 0.5f);
            tailRect.anchoredPosition = new Vector2(0f, -6f);
            tailRect.sizeDelta = new Vector2(18f, 18f);
            tailRect.localRotation = Quaternion.Euler(0f, 0f, 45f);

            Image tail = tailObject.GetComponent<Image>();
            tail.color = new Color(0.08f, 0.09f, 0.10f, 0.94f);
            tail.raycastTarget = false;
        }

        private void CreateLabel()
        {
            GameObject labelObject = new GameObject(
                "Message",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(viewRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
            labelRect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);

            label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 28f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 20f;
            label.fontSizeMax = 28f;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.96f, 0.94f, 0.88f, 1f);
            label.raycastTarget = false;
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
    }
}
