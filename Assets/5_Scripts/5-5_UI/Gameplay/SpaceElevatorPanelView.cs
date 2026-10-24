using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SpaceElevatorPanelView : MonoBehaviour
{
    #region 正式电梯视图引用
    public const int WarehouseColumns = 6;
    public const int VisibleWarehouseRows = 4;
    public const int DefaultWarehouseSlots = WarehouseColumns * VisibleWarehouseRows;
    public TextMeshProUGUI Title, State, WarehouseHeading, CargoCapacity, Feedback, Hint;
    public Button OpenBag, SendCargo, Ride, CallCabin, Close;
    public ScrollRect WarehouseScroll;
    public RectTransform WarehouseContent, WarehouseSlotTemplate;
    public event Action Destroyed;
    // 视图仅保存正式 Prefab 控件，不持有运输或库存状态。
    private void OnDestroy() { Destroyed?.Invoke(); Destroyed = null; }
    #endregion
}
