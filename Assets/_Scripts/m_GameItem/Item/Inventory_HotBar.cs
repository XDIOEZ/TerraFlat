using DG.Tweening;
using UnityEngine;

public class Inventory_HotBar : Inventory
{
    #region 字段

    [Header("快捷栏设置")]
    [Tooltip("生成位置")]
    public Transform spawnLocation;

    [Tooltip("快捷栏最大容量")]
    public int HotBarMaxVolume = 9;

    [Tooltip("选择框预制体")]
    public GameObject SelectBoxPrefab;

    [Tooltip("选择框变化时间")]
    [Range(0.01f, 0.5f)]
    public float SelectBoxChangeDuration = 0.1f;

    public GameObject SelectBox;

    public ItemSlot CurrentSelectItemSlot;

    // 当前选择的物品
    public ItemData currentItemData;
    public GameObject currentObject;

    // 当前索引与最大索引
    public int CurrentIndex { get => Data.selectedSlotIndex; set => Data.selectedSlotIndex = value; }
    public int MaxIndex { get => Data.itemSlots.Count; }

    #endregion

    #region 公有方法
    public new void Start()
    {
        base.Start();
        SelectBox = Instantiate(SelectBoxPrefab, itemSlotUIs[0].transform);
    }

    public override void OnItemClick(int index)
    {
        //完成基础的物品交换逻辑
        base.OnItemClick(index);
        //修改选择框位置
        ChangeSelectBoxPosition(index);
        // 同步 UI
        SyncUIData(CurrentIndex);
    }

    public void ChangeSelectBoxPosition(int newIndex)
    {
        //销毁之前的物品
        DestroyCurrentObject();

        newIndex = (newIndex + MaxIndex) % MaxIndex; // 确保索引合法

        CurrentIndex = newIndex;

        if (SelectBox != null)
        {
            GameObject targetSlot = itemSlotUIs[newIndex].gameObject;

            // 停止之前的动画
            SelectBox.transform.DOKill();

            // 设置选择框父物体
            SelectBox.transform.SetParent(targetSlot.transform, worldPositionStays: true);

            // 平滑移动到目标物品槽中心
            SelectBox.transform.DOLocalMove(Vector3.zero, SelectBoxChangeDuration)
                .SetEase(Ease.OutQuad);
        }
        else
        {
            Debug.LogError("[ChangeIndex] SelectBox 为空！");
        }

        // 切换到新物品
        ChangeNewObject(newIndex);
    }

    #endregion

    #region 私有方法

    private void SetSelectBoxSortingOrder(int order)
    {
        if (SelectBox != null)
        {
            Canvas selectBoxCanvas = SelectBox.GetComponent<Canvas>();

            if (selectBoxCanvas != null)
            {
                selectBoxCanvas.sortingOrder = order;
                return;
            }

            Canvas parentCanvas = SelectBox.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
                parentCanvas.sortingOrder = order;
                Debug.LogWarning("已设置父 Canvas 的 sortingOrder，可能影响其他 UI 元素！");
            }
            else
            {
                Debug.LogError("未找到 Canvas 组件，无法设置 sortingOrder！");
            }
        }
    }

    public void DestroyCurrentObject()
    {
        if (currentObject != null)
        {
            Destroy(currentObject);
        }
    }

    private void ChangeNewObject(int __index)
    {
        if (__index < 0 || __index >= MaxIndex)
        {
            Debug.LogError($"[ChangeNewObject] 索引 {__index} 超出范围！");
            return;
        }
        if (Data.itemSlots[__index]._ItemData == null)
        {
            return;
        }

        Item item = RunTimeItemManager.Instance.InstantiateItem(Data.itemSlots[__index]._ItemData.IDName);
        CurrentSelectItemSlot = Data.itemSlots[__index];
        if (item != null)
        {


            item.transform.SetParent(spawnLocation, false);
            item.transform.localPosition = Vector3.zero;

            Vector3 localRotation = item.transform.localEulerAngles;
            localRotation.z = 0;
            item.transform.localEulerAngles = localRotation;

            currentObject = item.gameObject;

            ItemData itemData = Data.itemSlots[__index]._ItemData;
            currentObject.GetComponent<Item>().Item_Data = itemData;
            currentObject.GetComponent<Item>().BelongItem = CurrentSelectItemSlot.Belong_Inventory.Belong_Item;
            currentObject.GetComponent<Item>().UpdatedUI_Event += () => SyncUIData(__index);
            currentObject.GetComponent<Item>().DestroyItem_Event += DestroyCurrentObject;
            spawnLocation.GetComponent<ITriggerAttack>().SetWeapon(currentObject);
        }
        else
        {
            Debug.LogError("[ChangeNewObject] 实例化的物体为空！");
        }

    }

    #endregion
}
