using System.Collections.Generic;
using UnityEngine;

public sealed partial class WorldRenderingManager
{
    #region 手持食物表现请求
    private readonly Dictionary<Item, HeldFoodPresentation> heldFoodPresentations = new(ItemReferenceComparer.Instance);

    public void SetHeldFoodPresentation(Item item, float eatingProgress, float maximumProgress)
    {
        if (item == null || !item.InHand || item.DestructionHandled || item.IsInPool) return;
        if (!RegisterItemRendering(item)) return;
        if (!heldFoodPresentations.TryGetValue(item, out HeldFoodPresentation request))
        {
            request = new HeldFoodPresentation(item);
            heldFoodPresentations.Add(item, request);
        }
        request.EatingProgress = eatingProgress;
        request.MaximumProgress = maximumProgress;
        request.Refresh();
    }

    public void ClearHeldFoodPresentation(Item item)
    {
        if (!ReferenceEquals(item, null) && heldFoodPresentations.Remove(item, out HeldFoodPresentation request))
            request.Release();
    }

    private void RefreshHeldFoodPresentation(Item item)
    {
        if (heldFoodPresentations.TryGetValue(item, out HeldFoodPresentation request)) request.Refresh();
    }

    private void ClearHeldFoodPresentations()
    {
        foreach (HeldFoodPresentation request in heldFoodPresentations.Values) request.Release();
        heldFoodPresentations.Clear();
    }

    private sealed class HeldFoodPresentation
    {
        private const float ProgressEpsilon = .0001f;
        private readonly Item item;
        private HeldFoodBiteRenderEffect biteEffect;
        private SpriteRenderer targetRenderer;
        private int originalSortingLayer, originalSortingOrder;
        internal float EatingProgress, MaximumProgress;
        internal HeldFoodPresentation(Item owner) => item = owner;

        internal void Refresh()
        {
            if (item == null || !item.InHand) { Release(); return; }
            if (targetRenderer == null)
            {
                SpriteRenderer renderer = item.Sprite;
                if (renderer == null || renderer.sprite == null)
                    foreach (SpriteRenderer child in item.GetComponentsInChildren<SpriteRenderer>(true))
                        if (child.sprite != null) { renderer = child; break; }
                if (renderer == null || renderer.sprite == null) return;
                targetRenderer = renderer;
                originalSortingLayer = renderer.sortingLayerID;
                originalSortingOrder = renderer.sortingOrder;
                ApplyAvailableRendererSorting(renderer, WorldSortingManager.HeldFoodCategory);
            }
            float maximum = Mathf.Max(ProgressEpsilon, MaximumProgress);
            int bites = Mathf.Max(1, Mathf.CeilToInt(maximum));
            int completed = EatingProgress >= maximum - ProgressEpsilon ? bites :
                Mathf.Clamp(Mathf.FloorToInt(EatingProgress + ProgressEpsilon), 0, bites - 1);
            // 未进食时不创建咬痕资源，保留原有完整食物的零分配路径。
            if (completed == 0) { biteEffect?.ClearBites(); return; }
            if (targetRenderer.sprite == null) return;
            biteEffect ??= item.GetComponent<HeldFoodBiteRenderEffect>() ?? item.gameObject.AddComponent<HeldFoodBiteRenderEffect>();
            biteEffect.SetBites(targetRenderer, completed, bites, CreateBiteSeed(bites));
        }

        private int CreateBiteSeed(int maximumBites)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (item.itemData?.Guid ?? 0);
                hash = hash * 31 + GetStableStringHash(item.itemData?.IDName);
                hash = hash * 31 + GetStableStringHash(targetRenderer.sprite.name);
                hash = hash * 31 + Mathf.RoundToInt(item.itemData?.Stack?.Amount ?? 0f);
                return hash * 31 + maximumBites;
            }
        }

        private static int GetStableStringHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                if (value != null) foreach (char character in value) hash = (hash ^ character) * 16777619;
                return (int)hash;
            }
        }

        internal void Release()
        {
            biteEffect?.ClearBites();
            if (targetRenderer != null)
            {
                targetRenderer.sortingLayerID = originalSortingLayer;
                targetRenderer.sortingOrder = originalSortingOrder;
            }
            targetRenderer = null;
        }
    }
    #endregion
}
