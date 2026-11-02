using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public partial class Mod_HotBar
{
    #region 手持物掏出表现

    private const float HeldItemDrawDuration = 0.25f;
    private const float HeldItemDrawStartingScale = 0.15f;

    private readonly List<Transform> heldDrawVisuals = new();
    private readonly List<Vector3> heldDrawOriginalScales = new();
    private Tween heldDrawTween;
    private Item heldDrawItem;

    public bool IsDrawingHeldItem => ReferenceEquals(heldDrawItem, CurentSelectItem) &&
                                     heldDrawTween != null && heldDrawTween.IsActive();

    private void BeginHeldItemDrawAnimation(Item heldItem)
    {
        StopHeldItemDrawAnimation();
        if (item is not Player { IsLocalProfile: true } || heldItem == null)
            return;

        // 只缩放 SpriteRenderer 表现节点，避免修改物品根节点、物理代理和存档尺寸。
        SpriteRenderer[] renderers = heldItem.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Transform visual = renderers[i].transform;
            if (visual == heldItem.transform ||
                visual.GetComponentInChildren<Collider2D>(true) != null ||
                visual.GetComponentInChildren<Rigidbody2D>(true) != null)
                continue;

            bool hasParentRenderer = false;
            for (Transform parent = visual.parent; parent != null && parent != heldItem.transform; parent = parent.parent)
            {
                if (parent.TryGetComponent<SpriteRenderer>(out _))
                {
                    hasParentRenderer = true;
                    break;
                }
            }

            if (hasParentRenderer || heldDrawVisuals.Contains(visual))
                continue;

            heldDrawVisuals.Add(visual);
            heldDrawOriginalScales.Add(visual.localScale);
        }

        if (heldDrawVisuals.Count == 0) return;

        (_inputController != null ? _inputController : item.GetComponent<Mod_GameController>())?.CancelHeldItemActions();
        heldDrawItem = heldItem;
        ApplyHeldItemDrawScale(HeldItemDrawStartingScale);
        heldDrawTween = DOVirtual.Float(HeldItemDrawStartingScale, 1f, HeldItemDrawDuration,
                ApplyHeldItemDrawScale)
            .SetEase(Ease.OutBack)
            .OnComplete(StopHeldItemDrawAnimation);
    }

    private void ApplyHeldItemDrawScale(float multiplier)
    {
        for (int i = 0; i < heldDrawVisuals.Count; i++)
            if (heldDrawVisuals[i] != null)
                heldDrawVisuals[i].localScale = heldDrawOriginalScales[i] * multiplier;
    }

    private void StopHeldItemDrawAnimation()
    {
        Tween tween = heldDrawTween;
        heldDrawTween = null;
        heldDrawItem = null;
        tween?.Kill();
        ApplyHeldItemDrawScale(1f);
        heldDrawVisuals.Clear();
        heldDrawOriginalScales.Clear();
    }

    #endregion
}
