using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 合成出口的统一预览表现：半透明虚像、由下往上的实化，以及成功弹跳。
/// </summary>
public sealed class CraftingOutputPreview : MonoBehaviour
{
    private const string GhostName = "Crafting Output Ghost";
    private const string RevealName = "Crafting Output Reveal";
    private const float GhostAlpha = 0.28f;
    private const float PopScale = 1.18f;
    private const float PopUpDuration = 0.1f;
    private const float PopDownDuration = 0.14f;

    private ItemSlot_UI _slotUI;
    private Image _realImage;
    private Image _ghostImage;
    private Image _revealImage;
    private Coroutine _popCoroutine;
    private Vector3 _restScale = Vector3.one;
    private Color _realImageColor = Color.white;
    private bool _hasRealImageColor;
    private bool _previewVisible;
    private float _progress01;

    public static CraftingOutputPreview Attach(BasePanel panel, ItemSlot_UI outputSlot)
    {
        HideLegacyProgress(panel);

        if (outputSlot == null)
            return null;

        CraftingOutputPreview preview = outputSlot.GetComponent<CraftingOutputPreview>();
        if (preview == null)
            preview = outputSlot.gameObject.AddComponent<CraftingOutputPreview>();

        preview.Initialize(outputSlot);
        return preview;
    }

    public static Sprite ResolveSprite(ItemData itemData)
    {
        if (itemData == null || string.IsNullOrEmpty(itemData.IDName) || GameRes.Instance == null)
            return null;

        GameObject prefab = GameRes.Instance.GetPrefab(itemData.IDName);
        SpriteRenderer renderer = prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>() : null;
        return renderer != null ? renderer.sprite : null;
    }

    public void Show(ItemData itemData, float progress01 = 0f)
    {
        Show(ResolveSprite(itemData), progress01);
    }

    public void Show(Sprite sprite, float progress01 = 0f)
    {
        EnsureImages();
        if (sprite == null || _ghostImage == null || _revealImage == null)
        {
            Clear();
            return;
        }

        _ghostImage.sprite = sprite;
        _revealImage.sprite = sprite;
        _previewVisible = true;
        SetRealImageVisible(false);
        _ghostImage.gameObject.SetActive(true);
        _revealImage.gameObject.SetActive(true);
        SetProgress(progress01);
    }

    public void SetProgress(float progress01)
    {
        if (_revealImage == null)
            return;

        _progress01 = Mathf.Clamp01(progress01);
        _revealImage.fillAmount = _progress01;
    }

    public void Clear()
    {
        _previewVisible = false;
        SetRealImageVisible(true);

        if (_ghostImage != null)
            _ghostImage.gameObject.SetActive(false);

        if (_revealImage != null)
        {
            _revealImage.fillAmount = 0f;
            _revealImage.gameObject.SetActive(false);
        }
    }

    public void PlaySuccess()
    {
        if (_popCoroutine != null)
            StopCoroutine(_popCoroutine);

        _popCoroutine = StartCoroutine(PopRoutine());
    }

    private void Initialize(ItemSlot_UI outputSlot)
    {
        _slotUI = outputSlot;
        _restScale = outputSlot.transform.localScale;
        EnsureImages();
    }

    private void EnsureImages()
    {
        if (_slotUI == null)
            return;

        Image reference = _slotUI.image;
        if (reference == null)
            reference = _slotUI.GetComponentInChildren<Image>(true);

        if (reference == null)
            return;

        if (_realImage != reference)
        {
            _realImage = reference;
            _realImageColor = reference.color;
            _hasRealImageColor = true;
        }

        _ghostImage = FindImage(GhostName);
        _revealImage = FindImage(RevealName);
        if (_ghostImage == null || _revealImage == null)
        {
            Debug.LogError(
                "[CraftingOutputPreview] UI_Slot Prefab 缺少制作预览图层，请重建并直接检查 Prefab。",
                _slotUI);
            return;
        }

        _ghostImage.color = new Color(1f, 1f, 1f, GhostAlpha);
        _ghostImage.raycastTarget = false;
        _ghostImage.preserveAspect = true;

        _revealImage.color = Color.white;
        _revealImage.raycastTarget = false;
        _revealImage.preserveAspect = true;
        _revealImage.type = Image.Type.Filled;
        _revealImage.fillMethod = Image.FillMethod.Vertical;
        _revealImage.fillOrigin = (int)Image.OriginVertical.Bottom;
        _revealImage.fillClockwise = true;

        int referenceIndex = reference.transform.GetSiblingIndex();
        _ghostImage.transform.SetSiblingIndex(referenceIndex + 1);
        _revealImage.transform.SetSiblingIndex(referenceIndex + 2);
        Clear();
    }

private Image FindImage(string imageName)
    {
        Image[] images = _slotUI.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && images[i].name == imageName)
                return images[i];
        }

        return null;
    }



    private IEnumerator PopRoutine()
    {
        Transform target = _slotUI != null ? _slotUI.transform : transform;
        Vector3 start = _restScale;
        Vector3 peak = start * PopScale;
        bool previewWasVisible = _previewVisible;

        if (previewWasVisible)
        {
            _ghostImage.gameObject.SetActive(false);
            _revealImage.gameObject.SetActive(false);
            SetRealImageVisible(true);
        }

        float elapsed = 0f;
        while (elapsed < PopUpDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            target.localScale = Vector3.LerpUnclamped(start, peak, EaseOutBack(elapsed / PopUpDuration));
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < PopDownDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            target.localScale = Vector3.LerpUnclamped(peak, start, Mathf.Clamp01(elapsed / PopDownDuration));
            yield return null;
        }

        target.localScale = start;
        if (_previewVisible)
        {
            SetRealImageVisible(false);
            _ghostImage.gameObject.SetActive(true);
            _revealImage.gameObject.SetActive(true);
            _revealImage.fillAmount = _progress01;
        }

        _popCoroutine = null;
    }

    private void SetRealImageVisible(bool visible)
    {
        if (_realImage == null || !_hasRealImageColor)
            return;

        Color color = _realImageColor;
        color.a = visible ? _realImageColor.a : 0f;
        _realImage.color = color;
    }

    private static float EaseOutBack(float value)
    {
        value = Mathf.Clamp01(value) - 1f;
        const float overshoot = 1.70158f;
        return 1f + value * value * ((overshoot + 1f) * value + overshoot);
    }

    private static void HideLegacyProgress(BasePanel panel)
    {
        if (panel == null)
            return;

        Transform[] transforms = panel.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && transforms[i].name == "Progress")
                transforms[i].gameObject.SetActive(false);
        }
    }
}
