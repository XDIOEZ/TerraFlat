using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 一次指针回调只扫描一次 UI，槽位、玩法目标和世界阻挡共用本次命中缓冲。
internal sealed class InventorySlotPointerQuery
{
    #region 指针命中快照
    private readonly List<RaycastResult> hits = new(8);
    private readonly List<MonoBehaviour> behaviours = new(8);

    public void Capture(PointerEventData eventData)
    {
        hits.Clear();
        EventSystem.current?.RaycastAll(eventData, hits);
    }

    public ItemSlot_UI FindSlot()
    {
        foreach (RaycastResult hit in hits)
        {
            ItemSlot_UI slot = hit.gameObject != null
                ? hit.gameObject.GetComponentInParent<ItemSlot_UI>() : null;
            if (slot != null) return slot;
        }
        return null;
    }

    public IInventoryDragDropTarget FindGameplayTarget(PointerEventData eventData)
    {
        Camera camera = eventData.pressEventCamera ?? eventData.enterEventCamera;
        foreach (RaycastResult hit in hits)
        {
            if (hit.gameObject == null) continue;
            behaviours.Clear();
            hit.gameObject.GetComponentsInParent(true, behaviours);
            foreach (MonoBehaviour behaviour in behaviours)
                if (behaviour != null && behaviour.isActiveAndEnabled &&
                    behaviour is IInventoryDragDropTarget target &&
                    target.ContainsInventoryDropPoint(eventData.position, camera))
                    return target;
        }
        return null;
    }

    public bool IsOverBlockingUI(PointerEventData eventData)
    {
        foreach (RaycastResult hit in hits)
        {
            GameObject target = hit.gameObject;
            if (target == null) continue;
            if (target.GetComponentInParent<ItemSlot_UI>() != null) return true;
            BasePanel panel = target.GetComponentInParent<BasePanel>();
            if (panel != null)
            {
                if (!panel.IsOpen()) continue;
                RectTransform bounds = panel.Dragger != null ? panel.Dragger.rectTransform : panel.rectTransform;
                if (bounds == null || RectTransformUtility.RectangleContainsScreenPoint(
                        bounds, eventData.position, eventData.pressEventCamera)) return true;
                continue;
            }
            if (target.GetComponentInParent<MobileHeldItemDropSurface>() == null &&
                target.GetComponentInParent<Selectable>() != null) return true;
        }
        return false;
    }
    #endregion
}
