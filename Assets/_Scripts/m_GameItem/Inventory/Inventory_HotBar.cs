using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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

    public FaceMouse faceMouse; // 用于控制物品旋转的组件

    // 当前选择的物品
    public ItemData currentItemData;
    public Item CurentSelectItem;
    public GameObject currentObject;

    // 当前索引与最大索引
    public int CurrentIndex { get => Data.Index; set => Data.Index = value; }
    public int MaxIndex { get => Data.itemSlots.Count; }

    #endregion

    #region 公有方法

    public override void Init()
    {
        base.Init();

        SelectBox = Instantiate(SelectBoxPrefab, itemSlotUIs[0].transform);

        // 获取FaceMouse组件（用于控制物品旋转）
        Owner.itemMods.GetMod_ByID(ModText.FaceMouse, out faceMouse);
        if ( faceMouse == null )
        {
            Debug.LogWarning("[Inventory_HotBar] 未找到FaceMouse组件，物品将无法跟随鼠标旋转");
        }

        Controller_Init();
        //修改选择框位置
        ChangeSelectBoxPosition(Data.Index);
        // 同步 UI
        RefreshUI(CurrentIndex);
    }

    public void Controller_Init()
    {
        // 先确保 Owner 存在
        if (Owner == null)
        {
            Debug.LogWarning($"[{name}] Controller_Init: Owner 为空，无法初始化输入");
            return;
        }

        // 获取 PlayerController
        var playerController = Owner.GetComponent<PlayerController>();
        if (playerController == null)
        {
            // 兜底：全局查找
            playerController = FindObjectOfType<PlayerController>();
            if (playerController == null)
            {
                Debug.LogWarning($"[{name}] Controller_Init: 未找到 PlayerController");
                return;
            }
        }

        // 确保 inputActions 已初始化
        var inputActions = playerController._inputActions;
        if (inputActions == null)
        {
            Debug.LogWarning($"[{name}] Controller_Init: PlayerController._inputActions 为空");
            return;
        }

        // 绑定输入事件
        var input = inputActions.Win10;
        input.RightClick.performed += _ => Controller_ItemAct();
        input.MouseScroll.started += SwitchHotbarByScroll;

        Debug.Log($"[{name}] 成功绑定输入事件", this);
    }


    //激活手持物品行为
    public void Controller_ItemAct()
    {
        if (CurentSelectItem != null)
            CurentSelectItem.Act();
    }

    public override void OnClick(int index)
    {
        //完成基础的物品交换逻辑
        base.OnClick(index);
        //修改选择框位置
        ChangeSelectBoxPosition(index);
        // 同步 UI
        RefreshUI(CurrentIndex);
    }


    private void SwitchHotbarByScroll(InputAction.CallbackContext context)
    {
        if (IsPointerOverUI())
            return;

        Vector2 scrollValue = context.ReadValue<Vector2>();

        if (scrollValue.y > 0)
        {
            ChangeSelectBoxPosition(CurrentIndex - 1);
        }
        else if (scrollValue.y < 0)
        {
            ChangeSelectBoxPosition(CurrentIndex + 1);
        }
    }

    private bool IsPointerOverUI()
    {
        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }

    public void ChangeSelectBoxPosition(int newIndex)
    {
        // 销毁之前的物品并从旋转列表中移除
        DestroyCurrentObject(CurentSelectItem);

        newIndex = (newIndex + MaxIndex) % MaxIndex; // 确保索引合法
        CurrentIndex = newIndex;

        if (SelectBox != null)
        {
            GameObject targetSlot = itemSlotUIs[newIndex].gameObject;
            SelectBox.transform.DOKill();
            SelectBox.transform.SetParent(targetSlot.transform, worldPositionStays: true);
            SelectBox.transform.DOLocalMove(Vector3.zero, SelectBoxChangeDuration).SetEase(Ease.OutQuad);
        }
        else
        {
            Debug.LogError("[ChangeIndex] SelectBox 为空！");
        }

        // 切换到新物品并添加到旋转列表
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

    public void DestroyCurrentObject(Item obj)
    {
        if (obj != null)
        {
            // 从FaceMouse的旋转列表中移除当前物品
            if (faceMouse != null)
            {
                faceMouse.targetRotationTransforms.Remove(obj.transform);
            }
            Destroy(obj.gameObject);
        }
    }

    // 完成：将新增对象添加到FaceMouse的旋转列表中，实现旋转控制
    private void ChangeNewObject(int index)
    {
        if (index < 0 || index >= MaxIndex)
        {
            Debug.LogError($"[ChangeNewObject] 索引 {index} 超出范围！");
            return;
        }

        var slot = Data.itemSlots[index];
        if (slot.itemData == null)
        {
            // 清空旋转列表（无物品选中时）
            if (faceMouse != null)
                //faceMouse.targetRotationTransforms.Clear();
            return;
        }

        ItemData itemData = slot.itemData;
        Item itemInstance = ItemMgr.Instance.InstantiateItem(itemData.IDName, spawnLocation.gameObject, position: default);

        if (itemInstance == null)
        {
            Debug.LogError("[ChangeNewObject] 实例化的物体为空！");
            return;
        }

        // 设置当前选择槽与当前物体引用
        CurrentSelectItemSlot = slot;
        currentObject = itemInstance.gameObject;
        CurentSelectItem = itemInstance;

        // 物体变换设置
        Transform tf = itemInstance.transform;
        tf.SetParent(spawnLocation, false);
        tf.localPosition = Vector3.zero;
        Vector3 rotation = tf.localEulerAngles;
        rotation.z = 0;
        tf.localEulerAngles = rotation;

        // 初始化 Item 属性
        itemInstance.itemData = itemData;
        itemInstance.itemData.ModuleDataDic = itemData.ModuleDataDic;
        itemInstance.Owner = slot.Belong_Inventory.Owner;

        // 事件绑定
        itemInstance.OnUIRefresh += () => RefreshUI(index);
        itemInstance.OnItemDestroy += DestroyCurrentObject;

        // 设置为当前武器
        spawnLocation.GetComponent<ITriggerAttack>()?.SetWeapon(currentObject);
        itemInstance.Load();

        // 核心：将新物品添加到FaceMouse的旋转列表，使其跟随鼠标旋转
        if (faceMouse != null)
        {
            // 清空列表确保只有当前物品被旋转
           // faceMouse.targetRotationTransforms.Clear();
            // 添加当前物品的transform到旋转列表
            faceMouse.targetRotationTransforms.Add(itemInstance.transform);
        }
    }

    #endregion
}
