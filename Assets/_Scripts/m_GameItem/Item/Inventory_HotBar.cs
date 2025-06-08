using DG.Tweening;
using NaughtyAttributes;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Inventory_HotBar : MonoBehaviour
{
    #region 字段

    [Tooltip("物品栏 UI")] // 在 Inspector 中显示为 "物品栏 UI"
    public Inventory_UI _inventory_UI;

    private Inventory inventory;

    [Tooltip("当前索引")] // 在 Inspector 中显示为 "当前索引"
    public int CurrentIndex = 0;

    [Tooltip("最大索引")] // 在 Inspector 中显示为 "最大索引"
    public int maxIndex;

    [Tooltip("生成位置")] // 在 Inspector 中显示为 "生成位置"
    public Transform spawnLocation;

    public GameObject SelectBoxPrefab;
    public GameObject SelectBox;

    public ItemSlot currentSelectItemSlot;

    #region 当前选择的物品槽
    public ItemData currentItemData;
    public GameObject currentObject;
    #endregion

    //添加unity特性方便编辑器修改
    [Tooltip("选择框变化时间")] 
    [Range(0.01f, 0.5f)]
    public float SelectBoxChangeDuration = 0.1f;

    [Tooltip("快捷栏最大容量")] // 在 Inspector 中显示为 "快捷栏最大容量"
    public int HotBarMaxVolume = 9;

    #endregion

    #region Unity 方法
    private void Awake()
    {
        // 检查 _inventory_UI 是否为空，若为空则尝试获取
        if (_inventory_UI == null)
        {
            _inventory_UI = GetComponent<Inventory_UI>();
        }

        if (_inventory_UI != null)
        {
            inventory = _inventory_UI.inventory;
            maxIndex = inventory.Data.itemSlots.Count;
            //Debug.Log($"[Awake] 获取到 Inventory_UI 组件，最大索引为: {maxIndex}");
        }
        else
        {
            Debug.LogError("[Awake] 无法获取 Inventory_UI 组件，请确保该组件已正确挂载！");
        }



    }

    private void Start()
    {
        if (inventory != null)
        {
            //确保在UI监听点击后再注册事件
            _inventory_UI.inventory.onSlotChanged += (int i, ItemSlot itemSlot) => ChangeIndex(i);

            // 确保 CurrentIndex 在有效范围内
            if (CurrentIndex < 0 || CurrentIndex >= inventory.Data.itemSlots.Count)
            {
                CurrentIndex = 0;
                Debug.LogWarning("[Start] 当前索引超出范围，已重置为 0");
            }

            // 实例化选择框到初始物品槽的父容器下（确保父容器是 Inventory UI 的根）
            if (_inventory_UI.itemSlots_UI != null && _inventory_UI.itemSlots_UI.Count > CurrentIndex)
            {
                // 1. 实例化选择框并设置父对象为 Inventory UI 的根容器
                SelectBox = Instantiate(SelectBoxPrefab,_inventory_UI.transform);

                SelectBox.transform.SetParent(_inventory_UI.transform.parent, true);

              

               

                // 2. 设置初始位置到当前物品槽的坐标
            //    GameObject initialSlot = _inventory_UI.itemSlots_UI[CurrentIndex].gameObject;

             //   SelectBox.transform.position = initialSlot.transform.position;

                SelectBox.transform.position = _inventory_UI.transform.position;

                // 3. 设置选择框的 Canvas 的 SortingOrder 为 2
                SetSelectBoxSortingOrder(2);

              //  Debug.Log($"[Start] 实例化选择框到索引 {CurrentIndex} 的物品槽");
            }
            else
            {
                Debug.LogError($"[Start] 无法实例化 SelectBoxPrefab，itemSlots_UI 为空或索引 {CurrentIndex} 超出范围！");
            }

            foreach (ItemSlot itemSlot in inventory.Data.itemSlots)
            {
                itemSlot.SlotMaxVolume = HotBarMaxVolume;
            }
        }
        else
        {
            Debug.LogError("[Start] Inventory 为空，无法初始化！");
        }


        //根据Data中的index设置初始索引
        ChangeIndex(inventory.Data.selectedSlotIndex);
    }
    private void SetSelectBoxSortingOrder(int order)
    {
        if (SelectBox != null)
        {
            // 1. 获取选择框的 Canvas 组件
            Canvas selectBoxCanvas = SelectBox.GetComponent<Canvas>();


            if (selectBoxCanvas != null)
            {
                selectBoxCanvas.sortingOrder = order;
                return;
            }

            // 2. 如果没有 Canvas，尝试获取父级的 Canvas（适用于选择框在现有 Canvas 下的情况）
            Canvas parentCanvas = SelectBox.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
                parentCanvas.sortingOrder = order;
                Debug.LogWarning("设置父 Canvas 的 sortingOrder，可能影响其他 UI 元素！");
            }
            else
            {
                Debug.LogError("未找到 Canvas 组件，无法设置 sortingOrder！");
            }
        }
    }

    #endregion

    #region 公有方法

    public void ChangeIndex(int newIndex)
    {
        inventory.Data.selectedSlotIndex = newIndex;
        // 确保索引在有效范围内
        newIndex = (newIndex + maxIndex) % maxIndex; // 确保索引合法（需确保maxIndex是物品槽数量）
        CurrentIndex = newIndex;

        // 获取当前选中的物品槽游戏对象
        GameObject temp = _inventory_UI.itemSlots_UI[CurrentIndex].gameObject;

        

        if (SelectBox != null && temp != null)
        {
            // 1. 立即停止之前的动画
            SelectBox.transform.DOKill(); // 关键：中断所有未完成的动画

            // 2. 设置父对象并保持世界坐标
            SelectBox.transform.SetParent(temp.transform, worldPositionStays: true);

            // 3. 使用局部坐标移动动画（确保位置精准）
            SelectBox.transform.DOLocalMove(
                Vector3.zero, // 移动到物品槽中心（局部坐标原点）
                duration: SelectBoxChangeDuration
            ).SetEase(Ease.OutQuad); // 添加平滑缓动

            // 可选：如果物品槽层级有缩放/旋转，可改为世界坐标移动
            // currentSelectBox.transform.DOMove(temp.transform.position, 0.2f).SetEase(Ease.OutQuad);
        }
        else
        {
            Debug.LogError("[ChangeIndex] currentSelectBox 或 temp 为空！");
        }

        // 其他逻辑
        ChangeNewObject(newIndex);
    }

    public void UpdateItemSlotUI()
    {
        if (currentObject == null)
        {
            Debug.Log("[UpdateItemSlotUI] 当前物体为空，无法更新 UI！");
            return;
        }
        if(currentObject.GetComponent<Item>().Item_Data != null)
        {
            _inventory_UI.RefreshSlotUI(CurrentIndex);
        }
    }
    #endregion

    #region 私有方法

    public void DestroyCurrentObject()
    {
        if (currentObject != null)
        {
            _inventory_UI.RefreshSlotUI(CurrentIndex);
            Destroy(currentObject);
          //  Debug.Log("[DestroyCurrentObject] 销毁当前物体成功");
        }
    }

    private void ChangeNewObject(int __index)
    {
        // 销毁当前物体
        DestroyCurrentObject();

        // 检查索引是否有效
        if (__index < 0 || __index >= maxIndex)
        {
        //    Debug.LogError($"[ChangeNewObject] 索引 {__index} 超出范围，无法更换物体！");
            return;
        }

        ItemSlot itemSlot = inventory.GetItemSlot(__index);
        currentSelectItemSlot = itemSlot;

        if (itemSlot._ItemData == null)
        {
         //   Debug.LogError("[ChangeNewObject] 物品数据为空，无法更换物体！");
            return;
        }

        // 异步加载预制体

       GameObject go = GameRes.Instance.AllPrefabs[itemSlot._ItemData.IDName];
        //实例化预制体
        GameObject newObject = Instantiate(go);

        if (newObject != null)
        {
            // 设置父对象
            newObject.transform.SetParent(spawnLocation, false);

            

            // 修改本地位置，确保位置为 (0, 0, 0)
            newObject.transform.localPosition = Vector3.zero;
            // 修改本地旋转，确保 Z 轴为 0
            Vector3 localRotation = newObject.transform.localEulerAngles;
            localRotation.z = 0;
            newObject.transform.localEulerAngles = localRotation;
            // 赋值当前对象
            currentObject = newObject;
            // 获取物品数据并设置
            ItemData itemData = itemSlot._ItemData;
            currentObject.GetComponent<Item>().Item_Data = itemData;
            currentObject.GetComponent<Item>().BelongItem = currentSelectItemSlot.belong_Inventory._belongItem;
            currentObject.GetComponent<Item>().UpdatedUI_Event += UpdateItemSlotUI;
            currentObject.GetComponent<Item>().DestroyItem_Event += DestroyCurrentObject;
            spawnLocation.GetComponent<ITriggerAttack>().SetWeapon(currentObject);
         //    Debug.Log($"[ChangeNewObject] 成功实例化物体: {itemData.Name}");
        }
        else
        {
          //  Debug.LogError("[ChangeNewObject] 实例化的物体为空！");
        }
    }

    #endregion
}
