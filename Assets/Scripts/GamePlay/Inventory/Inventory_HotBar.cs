using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 快捷栏系统
/// 负责快捷栏索引、UI、输入、手持物品生命周期管理
/// </summary>
public class Inventory_HotBar : Inventory
{
    #region 字段与属性

    [Header("快捷栏设置")]
    public Transform spawnLocation;
    public int HotBarMaxVolume = 9;

    [Header("UI")]
    public GameObject SelectBoxPrefab;
    [Range(0.01f, 0.5f)]
    public float SelectBoxChangeDuration = 0.1f;

    public GameObject SelectBox;

    public ItemSlot CurrentSelectItemSlot;
    public Item CurentSelectItem;
    public GameObject currentObject;

    private Mod_FocusPoint faceMouse;
    private Mod_TurnBack turnBody;

    public int CurrentIndex
    {
        get => Data.Index;
        private set => Data.Index = value;
    }

    public int MaxIndex => Data.itemSlots.Count;

    #endregion

    #region 初始化

    public override void OnValidate()
    {
        Data.Name = ModText.Hotbar;
    }

    public override void InitData()
    {
        base.InitData();
        spawnLocation = this.transform;
        GetRequiredComponents();
        InitInput();
    }

    public override void InitUI()
    {
        base.InitUI();

        if (itemSlotUIs.Count == 0) return;

        SelectBox = Instantiate(SelectBoxPrefab, itemSlotUIs[0].transform);
        SwitchItem(Data.Index);
    }

    #endregion

    #region 输入

    private void InitInput()
    {
        if (item == null) return;

        var controller = item.GetComponent<GameController>();
        if (controller == null) return;

        var input = controller._inputActions.Win10;
        input.RightClick.performed += _ => CurentSelectItem.Act();
        input.MouseScroll.started += OnScrollSwitch;
    }

    private void OnScrollSwitch(InputAction.CallbackContext ctx)
    {
        if (IsPointerOverUI()) return;

        float value = ctx.ReadValue<Vector2>().y;

        if (value > 0)
            SwitchItem(CurrentIndex - 1);
        else if (value < 0)
            SwitchItem(CurrentIndex + 1);
    }

    #endregion

    #region 公共接口

    public override void OnLeftClick(int index)
    {
        base.OnLeftClick(index);
        SwitchItem(index);
    }

    public float GetCurrentItemDurabilityPercentage()
    {
        if (CurentSelectItem?.itemData == null) return 1f;

        var data = CurentSelectItem.itemData;
        return data.MaxDurability > 0 ? data.Durability / data.MaxDurability : 0f;
    }

    #endregion

    #region 核心逻辑 - 物品切换

    private void SwitchItem(int targetIndex)
    {
        targetIndex = NormalizeIndex(targetIndex);
        UnloadCurrentItem();
        CurrentIndex = targetIndex;
        MoveSelectBox(targetIndex);
        LoadItemFromSlot(targetIndex);
        RefreshUI(CurrentIndex);
    }

    private void LoadItemFromSlot(int index)
    {
        var slot = Data.itemSlots[index];
        if (slot?.itemData == null) return;

        ItemData data = slot.itemData;

        Item itemInstance = ItemMgr.Instance.InstantiateItem(
            data.IDName,
            spawnLocation.gameObject,
            default
        );

        ConfigureItemInstance(itemInstance, data, slot);
    }

    private void UnloadCurrentItem()
    {
        if (CurentSelectItem == null) return;

        CurentSelectItem.InHand = false;

        faceMouse.targetRotationTransforms.Remove(CurentSelectItem.transform);
        turnBody.controlledTransforms_Direction.Remove(CurentSelectItem.transform);
        turnBody.controlledTransforms_Position.Remove(CurentSelectItem.transform);

        CurentSelectItem.OnUIRefresh -= RefreshUI;
        CurentSelectItem.OnItemDestroy -= OnDestroyCurrentObject;

        Destroy(CurentSelectItem.gameObject);

        CurentSelectItem = null;
        currentObject = null;
    }

    #endregion

    #region Item配置

    private void ConfigureItemInstance(Item item, ItemData data, ItemSlot slot)
    {
        Transform tf = item.transform;
        tf.SetParent(spawnLocation, false);
        tf.localPosition = Vector3.zero;

        Vector3 rot = tf.localEulerAngles;
        rot.z = 0;
        tf.localEulerAngles = rot;

        item.itemData = data;
        item.Owner = this.item;

        item.OnUIRefresh += RefreshUI;
        item.OnItemDestroy += OnDestroyCurrentObject;

        item.Load();
        item.InHand = true;

        CurentSelectItem = item;
        CurrentSelectItemSlot = slot;
        currentObject = item.gameObject;

        // 注册到面向鼠标与转身系统（避免重复添加）
        if (faceMouse != null && !faceMouse.targetRotationTransforms.Contains(tf))
        {
            faceMouse.targetRotationTransforms.Add(tf);
        }

        if (turnBody != null)
        {
            if (!turnBody.controlledTransforms_Direction.Contains(tf))
            {
                turnBody.controlledTransforms_Direction.Add(tf);
            }

            // 物品实例本身的位置不需要加入 Position 列表，
            // 只在 GetRequiredComponents 中对快捷栏 Transform 做一次性注册
        }

        // 切换新物品时，强制让朝向系统刷新一次，
        // 解决“鼠标在左边滚轮切换时物品朝向出错”的问题
        turnBody?.UpdateAllTransformDirections();
    }

    #endregion

    #region UI

    private void MoveSelectBox(int index)
    {
        if (SelectBox == null) return;

        SelectBox.transform.DOKill();
        SelectBox.transform.SetParent(itemSlotUIs[index].transform, true);
        SelectBox.transform.DOLocalMove(Vector3.zero, SelectBoxChangeDuration)
            .SetEase(Ease.OutQuad);
    }

    #endregion

    #region 工具

    private int NormalizeIndex(int index)
    {
        if (MaxIndex <= 0) return 0;
        return (index + MaxIndex) % MaxIndex;
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

    private void GetRequiredComponents()
    {
        item.itemMods.GetMod_ByID(ModText.FocusPoint, out faceMouse);
        item.itemMods.GetMod_ByID(ModText.TrunBody, out turnBody);

        // 初始化时只注册一次快捷栏 Transform，用于位置镜像
        if (turnBody != null && !turnBody.controlledTransforms_Position.Contains(transform))
        {
            turnBody.controlledTransforms_Position.Add(transform);
        }
    }

    public void OnDestroyCurrentObject(Item obj)
    {
        if (obj == null) return;
        
        obj.InHand = false;
        UnloadCurrentItem();
    }

    #endregion
}
