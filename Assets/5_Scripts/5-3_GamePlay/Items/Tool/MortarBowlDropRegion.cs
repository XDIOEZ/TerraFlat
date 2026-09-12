using UnityEngine;

/// <summary>透明槽位的实际投料命中区；复用石碗内壁轮廓，碗外和石壁不接收拖入。</summary>
public sealed class MortarBowlDropRegion : MonoBehaviour, ICanvasRaycastFilter
{
    public MortarInteractionView View; // 与动画共用内壁尺寸。
    public ItemSlot_UI Slot; // 正式模板绑定的槽位，克隆时跟随实例引用。

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        // 有物品时跟随小槽位命中；只有空槽使用整个凹槽的投料轮廓。
        if (Slot.GetSlotDataFunc != null && Slot.GetSlotDataFunc(Slot.slotIndex)?.itemData != null)
            return true;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(View.DragArea, screenPoint, eventCamera, out Vector2 point))
            return false;
        RectTransform bowl = View.BowlShape;
        return Mathf.Abs(point.x - bowl.anchoredPosition.x) <= View.InteriorHalfWidth &&
               point.y >= View.FloorAt(point.x) &&
               point.y <= View.InteriorTop;
    }
}
