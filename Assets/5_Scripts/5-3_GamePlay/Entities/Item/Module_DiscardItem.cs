using NaughtyAttributes;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class Module_DiscardItem : Mod_BaseDroper
{
    [Header("基础配置")]
    public Inventory DroperInventory;
    public ItemSlot ItemToDrop_Slot;

    [Header("掉落动画参数")]
    public float parabolaHeight = 2f; // 抛物线最大高度
    public float baseDropDuration = 0.5f; // 动画基础持续时间
    public float distanceSensitivity = 0.1f; // 动画时间距离敏感度

    [Header("丢弃操作配置")]
    public float dropRepeatDelay = 0.3f; // 长按重复丢弃的延迟
    public float dropRepeatInterval = 0.1f; // 长按重复丢弃的间隔

    public Inventory_HotBar Hotbar;
    public Mod_Hand hand;

    public override ModuleData _Data { get => modData; set => modData = value as Ex_ModData; }

    private Mod_FocusPoint faceMouse;
    public GameController GameController;
    public Vector2 DropPos => GameController.GetMouseWorldPosition();

    // 长按相关变量
    [SerializeField]
    private bool isDropButtonPressed = false;
    [SerializeField]
    private float dropButtonPressTime = 0f;
    [SerializeField]
    private bool isDropRepeatActive = false;
    [SerializeField]
    private float lastDropTime = 0f; // 上次丢弃的时间
    [SerializeField]
    private ItemSlot_UI hoveredSlot = null;
    [SerializeField]
    private bool isCtrlPressed = false;

    #region 生命周期

    private void OnValidate()
    {
        _Data.ID = ModText.ItemDorper;
    }
    public override void Load()
    {
        base.Load();

        faceMouse = item.itemMods.GetMod_ByID(ModText.FocusPoint).GetComponent<Mod_FocusPoint>();

        var hotbarMod = item.itemMods.GetMod_ByID(ModText.Hotbar);
        Hotbar = hotbarMod != null ? hotbarMod.GetComponent<Inventory_HotBar>() : null;

        hand = item.GetComponentInChildren<Mod_Hand>();

        GameController = item.GetComponent<GameController>();

        // 绑定按键事件
        var inputActions = GameController._inputActions.Win10;
        inputActions.F.started += OnDropButtonPressed;
        inputActions.F.canceled += OnDropButtonReleased;
        inputActions.Ctrl.started += OnCtrlPressed;
        inputActions.Ctrl.canceled += OnCtrlReleased;
    }

    public override void ModUpdate(float deltaTime)
    {
        base.ModUpdate(deltaTime);

        // 处理长按逻辑
        if (isDropButtonPressed)
        {
            dropButtonPressTime += deltaTime;

            // 检查是否达到重复丢弃的条件
            if (!isDropRepeatActive && dropButtonPressTime >= dropRepeatDelay)
            {
                isDropRepeatActive = true;
                lastDropTime = 0f;
                Debug.Log("[ItemDroper] 长按激活，进入持续丢弃模式");
            }

            // 长按激活后执行重复丢弃（不再依赖hoveredSlot）
            if (isDropRepeatActive)
            {
                lastDropTime += deltaTime;
                // 检查是否到了重复间隔时间
                if (lastDropTime >= dropRepeatInterval)
                {
                    HandleRepeatDrop();
                    lastDropTime = 0f; // 重置计时器
                }
            }
        }
        else
        {
            // 按键未按下时重置状态
            isDropRepeatActive = false;
            dropButtonPressTime = 0f;
            lastDropTime = 0f;
        }

        // 更新当前鼠标悬停的槽位
        UpdateHoveredSlot();
    }

    private void UpdateHoveredSlot()
    {
        hoveredSlot = null;
        if (Mouse.current == null || EventSystem.current == null)
            return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();

        List<RaycastResult> results = new List<RaycastResult>();
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = mousePosition
        };

        EventSystem.current.RaycastAll(pointerEventData, results);

        if (results.Count > 0)
        {
            foreach (var r in results)
            {
                var slot = r.gameObject.GetComponent<ItemSlot_UI>();
                if (slot != null)
                {
                    hoveredSlot = slot;
                    break;
                }
            }
        }
    }

    private void HandleRepeatDrop()
    {
        // 检查手上是否有物品
        ItemSlot handSlot = hand?.HandInventory?.Data?.itemSlots?[hand.HandInventory.Data.Index];
        if (handSlot != null && handSlot.itemData != null && handSlot.Amount > 0)
        {
            if (isCtrlPressed)
            {
                DropItemByCount(handSlot, handSlot.Amount);
            }
            else
            {
                DropItemByCount(handSlot, 1);
            }
            // 只有当物品完全丢弃完后才刷新UI
            if (handSlot.Amount <= 0)
            {
                handSlot.RefreshUI();
            }
            return;
        }

        // 检查快捷栏是否有选中物品
        else if (Hotbar?.currentObject != null)
        {
            ItemSlot hotbarSlot = Hotbar.CurrentSelectItemSlot;
            if (hotbarSlot != null && hotbarSlot.Amount > 0)
            {
                if (isCtrlPressed)
                {
                    DropItemByCount(hotbarSlot, hotbarSlot.Amount);
                }
                else
                {
                    DropItemByCount(hotbarSlot, 1);
                }
                // 只有当物品完全丢弃完后才销毁对象
                if (hotbarSlot.Amount <= 0)
                {
                    Hotbar.OnDestroyCurrentObject(Hotbar.CurentSelectItem);
                }
                return;
            }
        }

        // 只有当手上和快捷栏都没有物品时，才处理UI悬停的物品
        if (hoveredSlot != null && hoveredSlot.GetSlotDataFunc != null)
        {
            ItemSlot hoveredSlotData = hoveredSlot.GetSlotDataFunc?.Invoke(-1);
            if (hoveredSlotData != null && hoveredSlotData.Amount > 0)
            {
                if (isCtrlPressed)
                {
                    DropItemByCount(hoveredSlotData, hoveredSlotData.Amount);
                }
                else
                {
                    DropItemByCount(hoveredSlotData, 1);
                }

                // 如果物品已经耗尽，且是当前快捷栏选中的物品，则销毁手上物体
                if (hoveredSlotData.Amount <= 0 && hoveredSlotData == Hotbar?.CurrentSelectItemSlot)
                {
                    Hotbar?.OnDestroyCurrentObject(Hotbar.CurentSelectItem);
                }

                hoveredSlot.RefreshUI();
            }
        }
    }

    private void OnDropButtonPressed(InputAction.CallbackContext context)
    {
        isDropButtonPressed = true;
        dropButtonPressTime = 0f;
        isDropRepeatActive = false;
        lastDropTime = 0f;
    }

    private void OnDropButtonReleased(InputAction.CallbackContext context)
    {
        isDropButtonPressed = false;
        dropButtonPressTime = 0f;
        isDropRepeatActive = false;
        lastDropTime = 0f;
        hoveredSlot = null;

        // 松开按键时执行一次丢弃操作
        // 松开按键时执行一次丢弃操作
        if (hand.HandInventory.Data.itemSlots[hand.HandInventory.Data.Index].itemData != null)
        {
            ItemSlot handSlot = hand.HandInventory.Data.itemSlots[hand.HandInventory.Data.Index];
            if (isCtrlPressed)
            {
                // Ctrl+F 丢弃整组
                DropItemByCount(handSlot, handSlot.Amount);
            }
            else
            {
                // F 丢弃单个
                DropItemByCount(handSlot, 1);
            }

            // 只有当物品完全丢弃完后才刷新UI
            if (handSlot.Amount <= 0)
            {
                handSlot.RefreshUI();
            }
        }
        else if (Hotbar.currentObject != null)
        {
            ItemSlot hotbarSlot = Hotbar.CurrentSelectItemSlot;
            if (isCtrlPressed)
            {
                // Ctrl+F 丢弃整组
                DropItemByCount(hotbarSlot, hotbarSlot.Amount);
            }
            else
            {
                // F 丢弃单个
                DropItemByCount(hotbarSlot, 1);
            }

            // 只有当物品完全丢弃完后才销毁对象
            if (hotbarSlot.Amount <= 0)
            {
                Hotbar.OnDestroyCurrentObject(Hotbar.CurentSelectItem);
            }
        }
        else
        {
            if (isCtrlPressed)
            {
                // Ctrl+F 快速丢弃整组
                FastDropStack();
            }
            else
            {
                // F 快速丢弃单个
                FastDropItem();
            }
            Hotbar.RefreshUI(Hotbar.CurrentIndex);
        }
    }

    private void OnCtrlPressed(InputAction.CallbackContext context)
    {
        isCtrlPressed = true;
    }

    private void OnCtrlReleased(InputAction.CallbackContext context)
    {
        isCtrlPressed = false;
    }

    public void OnDestroy()
    {
        if (GameController != null && GameController._inputActions != null)
        {
            var inputActions = GameController._inputActions.Win10;
            inputActions.F.started -= OnDropButtonPressed;
            inputActions.F.canceled -= OnDropButtonReleased;
            inputActions.Ctrl.started -= OnCtrlPressed;
            inputActions.Ctrl.canceled -= OnCtrlReleased;
        }
    }
    #endregion

    #region 物品丢弃接口

    /// <summary>
    /// 丢弃当前明确选择的物品：优先手持槽，其次快捷栏选中槽。手机抽屉固定传入 1，物品菜单可传入整组数量；
    /// 不依赖 Ctrl 或鼠标悬停，因此触屏调用不会命中其它 UI 槽位。
    /// </summary>
    public bool TryDropCurrentSelection(int count)
    {
        if (count <= 0)
            return false;

        ItemSlot handSlot = hand?.HandInventory?.Data?.itemSlots != null &&
                            hand.HandInventory.Data.Index >= 0 &&
                            hand.HandInventory.Data.Index < hand.HandInventory.Data.itemSlots.Count
            ? hand.HandInventory.Data.itemSlots[hand.HandInventory.Data.Index]
            : null;
        if (handSlot?.itemData != null && handSlot.Amount > 0)
        {
            DropItemByCount(handSlot, Mathf.Min(count, handSlot.Amount));
            return true;
        }

        ItemSlot hotbarSlot = Hotbar?.CurrentSelectItemSlot;
        if (hotbarSlot?.itemData == null || hotbarSlot.Amount <= 0)
            return false;

        DropItemByCount(hotbarSlot, Mathf.Min(count, hotbarSlot.Amount));
        if (hotbarSlot.Amount <= 0)
            Hotbar.OnDestroyCurrentObject(Hotbar.CurentSelectItem);
        return true;
    }

    [Button("DropItemBySlot")]
    public void DropItemBySlot(ItemSlot slot)
    {
        if (slot == null)
        {
            Debug.LogError("传入的 ItemSlot 为空！");
            return;
        }

        DropItemByCount(slot, slot.Amount);
    }

    [Button("DropItemStack")]
    public void DropItemStack(ItemSlot slot)
    {
        if (slot == null)
        {
            Debug.LogError("传入的 ItemSlot 为空！");
            return;
        }

        DropItemByCount(slot, slot.Amount);
    }

    public void DropItemByCount(ItemSlot slot, int count)
    {
        if (count <= 0 || slot == null || slot.Amount <= 0)
        {
            Debug.LogWarning("丢弃数量非法或物品槽为空！");
            return;
        }

        if (count <= slot.Amount)
        {
            // 克隆数据
            ItemData newItemData = FastCloner.FastCloner.DeepClone(slot.itemData);
            newItemData.Stack.Amount = count;
            newItemData.Stack.CanBePickedUp = false;
            newItemData.inHand = false;

            Item newObject = null;
            try
            {
                // 不再先查旧 Chunk；ItemWorldPlacement 和 Mod_Droping 会接入新版 ChunkView。
                Vector2 startPos = transform.position;
                Vector2 endPos = DropPos;
                newObject = ItemMgr.Instance.InstantiateItem(
                    newItemData,
                    startPos,
                    Quaternion.identity,
                    Vector3.one * 0.5f);
                if (newObject == null)
                    throw new InvalidOperationException("ItemMgr 未返回掉落物实例。");

                Item newItem = newObject.GetComponent<Item>();
                if (newItem == null)
                    throw new InvalidOperationException("新物体中缺少 Item 组件。");

                float distance = WorldTopologyRuntime.Distance(startPos, endPos);
                float animTime = baseDropDuration + distance * distanceSensitivity;

                newItem.Load();
                newItem.SetInHand(false);
                DropItem_Pos(newItem, startPos, endPos, animTime);

                // 生成和掉落动画都成功后才提交背包扣减，失败时不会丢失玩家物品。
                slot.Amount -= count;
                if (slot.Amount <= 0)
                    slot.ClearData();
            }
            catch (Exception exception)
            {
                if (newObject != null && !newObject.DestructionHandled &&
                    ItemMgr.Instance != null)
                {
                    ItemMgr.Instance.DespawnItem(newObject,
                        saveData: false, detachFromChunk: false);
                }

                Debug.LogError($"掉落物生成失败：{newItemData.IDName}，{exception.Message}", this);
            }
        }

        slot.RefreshUI();
    }
    [Button("快速丢弃")]
    public void FastDropItem(int count = 1)
    {
        Vector2 mousePosition = GameController != null
            ? GameController.GetPointerScreenPosition()
            : Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition;

        List<RaycastResult> results = new List<RaycastResult>();
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = mousePosition
        };

        EventSystem.current.RaycastAll(pointerEventData, results);

        if (results.Count > 0)
        {
            var uiItemSlot = results[0].gameObject.GetComponent<ItemSlot_UI>();

            if (uiItemSlot != null && uiItemSlot.GetSlotDataFunc != null)
            {
                ItemSlot slotData = uiItemSlot.GetSlotDataFunc?.Invoke(-1);
                if (slotData != null)
                {
                    DropItemByCount(slotData, count);
                }
            }
        }
    }

    [Button("快速丢弃整组")]
    public void FastDropStack()
    {
        Vector2 mousePosition = GameController != null
            ? GameController.GetPointerScreenPosition()
            : Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition;

        List<RaycastResult> results = new List<RaycastResult>();
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = mousePosition
        };

        EventSystem.current.RaycastAll(pointerEventData, results);

        if (results.Count > 0)
        {
            var uiItemSlot = results[0].gameObject.GetComponent<ItemSlot_UI>();

            if (uiItemSlot != null && uiItemSlot.GetSlotDataFunc != null)
            {
                ItemSlot slotData = uiItemSlot.GetSlotDataFunc?.Invoke(-1);
                if (slotData != null)
                {
                    DropItemByCount(slotData, slotData.Amount);
                }
            }
        }
    }

    #endregion
}
