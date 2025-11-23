using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 快捷栏系统，继承自基础库存类，管理玩家的快捷物品栏
/// 负责物品选择、显示和交互功能
/// </summary>
public class Inventory_HotBar : Inventory
{
    #region 字段与属性

    [Header("快捷栏设置")]
    [Tooltip("物品生成位置")]
    public Transform spawnLocation;

    [Tooltip("快捷栏最大容量")]
    public int HotBarMaxVolume = 9;

    [Tooltip("选择框预制体")]
    public GameObject SelectBoxPrefab;

    [Tooltip("选择框变化时间")]
    [Range(0.01f, 0.5f)]
    public float SelectBoxChangeDuration = 0.1f;

    /// <summary>
    /// 当前选择框游戏对象
    /// </summary>
    public GameObject SelectBox;

    /// <summary>
    /// 当前选中的物品槽
    /// </summary>
    public ItemSlot CurrentSelectItemSlot;

    /// <summary>
    /// 用于控制物品旋转的组件
    /// </summary>
    public Mod_FocusPoint faceMouse; 
    
    /// <summary>
    /// 用于控制转身的组件
    /// </summary>
    public Mod_TurnBody turnBody;   

    /// <summary>
    /// 当前选择的物品数据
    /// </summary>
    public ItemData currentItemData;
    
    /// <summary>
    /// 当前选择的物品实例
    /// </summary>
    public Item CurentSelectItem;
    
    /// <summary>
    /// 当前选择物品的游戏对象
    /// </summary>
    public GameObject currentObject;

    /// <summary>
    /// 当前索引属性
    /// </summary>
    public int CurrentIndex { get => Data.Index; set => Data.Index = value; }
    
    /// <summary>
    /// 最大索引属性
    /// </summary>
    public int MaxIndex { get => Data.itemSlots.Count; }

    #endregion

    public override void OnValidate()
    {
        Data.Name = ModText.Hotbar;
    }


    #region 初始化与设置

    /// <summary>
    /// 初始化快捷栏系统
    /// </summary>
    public override void Init()
    {
        base.Init();

        // 实例化选择框
        SelectBox = Instantiate(SelectBoxPrefab, itemSlotUIs[0].transform);

        // 获取FaceMouse组件（用于控制物品旋转）
        Owner.itemMods.GetMod_ByID(ModText.FocusPoint, out faceMouse);
        if (faceMouse == null)
        {
            Debug.LogWarning("[Inventory_HotBar] 未找到FaceMouse组件，物品将无法跟随鼠标旋转");
        }

        // 获取TurnBody组件（用于控制转身）
        Owner.itemMods.GetMod_ByID(ModText.TrunBody, out turnBody);
        if (turnBody == null)
        {
            Debug.LogWarning("[Inventory_HotBar] 未找到TurnBody组件，物品将无法实现转身镜像");
        }

        // 初始化控制器和UI
        Controller_Init();
        ChangeSelectBoxPosition(Data.Index);
        RefreshUI(CurrentIndex);
    }
        public override void BindController()
    {
        Debug.Log("BindController Null " + Data.Name);
    }

    /// <summary>
    /// 初始化输入控制器
    /// </summary>
    public void Controller_Init()
    {
        // 先确保 Owner 存在
        if (Owner == null)
        {
            Debug.LogWarning($"[{name}] Controller_Init: Owner 为空，无法初始化输入");
            return;
        }
    
        // 获取 PlayerController（仅从Owner获取，不再进行全局查找）
        var playerController = Owner.GetComponent<PlayerController>();
        if (playerController == null)
        {
            // 直接返回，不再进行全局查找
            Debug.LogWarning($"[{name}] Controller_Init: Owner上未找到 PlayerController，可能是非玩家对象使用快捷栏");
            return;
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

    #endregion

    #region 物品交互

    /// <summary>
    /// 激活手持物品行为
    /// </summary>
    public void Controller_ItemAct()
    {
        if (CurentSelectItem != null)
            CurentSelectItem.Act();
    }

    /// <summary>
    /// 处理左键点击事件
    /// </summary>
    /// <param name="index">点击的槽位索引</param>
    public override void OnLeftClick(int index)
    {
        //完成基础的物品交换逻辑
        base.OnLeftClick(index);
        //修改选择框位置
        ChangeSelectBoxPosition(index);
        // 同步 UI
        RefreshUI(CurrentIndex);
    }

    #endregion

    #region 快捷栏切换

    /// <summary>
    /// 通过鼠标滚轮切换快捷栏
    /// </summary>
    /// <param name="context">输入事件上下文</param>
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

    /// <summary>
    /// 检查指针是否在UI上
    /// </summary>
    /// <returns>如果指针在UI上返回true，否则返回false</returns>
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

    /// <summary>
    /// 改变选择框位置
    /// </summary>
    /// <param name="newIndex">新的索引位置</param>
    public void ChangeSelectBoxPosition(int newIndex)
    {
        // 销毁之前的物品并从旋转列表中移除
        DestroyCurrentObject(CurentSelectItem);

        // 确保索引合法（循环索引）
        newIndex = (newIndex + MaxIndex) % MaxIndex;
        CurrentIndex = newIndex;

        if (SelectBox != null)
        {
            // 移动选择框到目标位置
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

    #region 物品管理

    /// <summary>
    /// 设置选择框的层级顺序
    /// </summary>
    /// <param name="order">层级顺序值</param>
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

    /// <summary>
    /// 销毁当前物品
    /// </summary>
    /// <param name="obj">要销毁的物品</param>
    public void DestroyCurrentObject(Item obj)
    {
        if (obj != null)
        {
            // 从FaceMouse的旋转列表中移除当前物品
            if (faceMouse != null)
            {
                faceMouse.targetRotationTransforms.Remove(obj.transform);
            }
            
            // 从TurnBody的控制列表中移除当前物品
            if (turnBody != null)
            {
                turnBody.controlledTransforms.Remove(obj.transform);
            }
            
            Destroy(obj.gameObject);
        }
    }

    /// <summary>
    /// 切换到新物品
    /// 将新增对象添加到FaceMouse的旋转列表中，实现旋转控制
    /// </summary>
    /// <param name="index">物品索引</param>
    private void ChangeNewObject(int index)
    {
        // 参数验证
        if (index < 0 || index >= MaxIndex)
        {
            Debug.LogError($"[ChangeNewObject] 索引 {index} 超出范围！");
            return;
        }

        var slot = Data.itemSlots[index];
        if (slot.itemData == null)
        {
            return;
        }

        ItemData itemData = slot.itemData;
        
        // 添加防御性检查 - 检查ItemData.name是否为空
        if (string.IsNullOrEmpty(itemData.IDName))
        {
            Debug.LogWarning($"[Inventory_HotBar] 物品ID为空或未设置名称，Prefab ID: {itemData.IDName}");
        }
        
        // 添加防御性检查 - 检查IDName是否为空
        if (string.IsNullOrEmpty(itemData.IDName))
        {
            Debug.LogError("[Inventory_HotBar] 物品IDName为空，无法实例化物品");
            return;
        }
        spawnLocation = this.transform;
        // 实例化物品
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
        itemInstance.Owner = Owner;

        // 事件绑定
        itemInstance.OnUIRefresh += () => RefreshUI(index);
        itemInstance.OnItemDestroy += DestroyCurrentObject;

        // 设置为当前武器
        spawnLocation.GetComponent<ITriggerAttack>()?.SetWeapon(currentObject);
        itemInstance.Load();

        // 核心：将新物品添加到FaceMouse的旋转列表，使其跟随鼠标旋转
        if (faceMouse != null)
        {
            faceMouse.targetRotationTransforms.Add(itemInstance.transform);
        }
        
        // 核心：将新物品添加到TurnBody的控制列表，实现转身后镜像
        if (turnBody != null)
        {
            turnBody.controlledTransforms.Add(itemInstance.transform);
        }
    }

    #endregion
}