using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class ItemDroper : Mod_ItemDroper
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
    public Mod_Inventory hand;

    public override ModuleData _Data { get => modData; set => modData = value as Ex_ModData; }

    private Mod_FocusPoint faceMouse;
    public PlayerController playerController;
    public Vector2 DropPos => playerController.GetMouseWorldPosition();

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

    public override void Load()
    {
        base.Load();

        faceMouse = item.itemMods.GetMod_ByID(ModText.FocusPoint).GetComponent<Mod_FocusPoint>();

        Hotbar = item.itemMods.GetMod_ByID(ModText.Hotbar).GetComponent<Inventory_HotBar>();

        hand = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<Mod_Inventory>();

        playerController = item.GetComponent<PlayerController>();

        // 绑定按键事件
        var inputActions = playerController._inputActions.Win10;
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
        Vector2 mousePosition = Mouse.current.position.ReadValue();

        List<RaycastResult> results = new List<RaycastResult>();
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = mousePosition
        };

        EventSystem.current.RaycastAll(pointerEventData, results);

        hoveredSlot = null;
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
    ItemSlot handSlot = hand?.inventory?.Data?.itemSlots?[hand.inventory.Data.Index];
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
                Hotbar.DestroyCurrentObject(Hotbar.CurentSelectItem);
            }
            return;
        }
    }
    
    // 只有当手上和快捷栏都没有物品时，才处理UI悬停的物品
    if (hoveredSlot != null && hoveredSlot.Data != null && hoveredSlot.Data.Amount > 0)
    {
        if (isCtrlPressed)
        {
            DropItemByCount(hoveredSlot.Data, hoveredSlot.Data.Amount);
        }
        else
        {
            DropItemByCount(hoveredSlot.Data, 1);
        }
        
        // 如果物品已经耗尽，且是当前快捷栏选中的物品，则销毁手上物体
        if (hoveredSlot.Data.Amount <= 0 && hoveredSlot.Data == Hotbar?.CurrentSelectItemSlot)
        {
            Hotbar?.DestroyCurrentObject(Hotbar.CurentSelectItem);
        }
        
        hoveredSlot.Data.RefreshUI();
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
        if (hand.inventory.Data.itemSlots[hand.inventory.Data.Index].itemData != null)
        {
            ItemSlot handSlot = hand.inventory.Data.itemSlots[hand.inventory.Data.Index];
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
                Hotbar.DestroyCurrentObject(Hotbar.CurentSelectItem);
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
        if (playerController != null && playerController._inputActions != null)
        {
            var inputActions = playerController._inputActions.Win10;
            inputActions.F.performed -= OnDropButtonPressed;
            inputActions.F.canceled -= OnDropButtonReleased;
            inputActions.Ctrl.performed -= OnCtrlPressed;
            inputActions.Ctrl.canceled -= OnCtrlReleased;
        }
    }
    #endregion

    #region 物品丢弃接口

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

        // 减少原物品数量
        slot.Amount -= count;

        Item newObject = null;
        // 实例化新物体
        ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(Chunk.GetChunkPosition(transform.position).ToString(), out Chunk chunk);
        if (chunk != null)
        {
            newObject = ItemMgr.Instance.InstantiateItem(newItemData, chunk.gameObject);
        }

        if (newObject == null)
        {
            Debug.LogError("实例化失败，找不到资源：" + newItemData.IDName);
            return;
        }

        Item newItem = newObject.GetComponent<Item>();
        if (newItem == null)
        {
            Debug.LogError("新物体中缺少 Item 组件！");
            return;
        }

        // 使用 InstantiateItem 中新生成的 GUID 和数据
        // 不再手动设置 newItem.itemData = newItemData，因为 InstantiateItem 已经处理了这一步

        // 计算位置
        Vector2 startPos = transform.position;
        Vector2 endPos = DropPos;

        float distance = Vector2.Distance(startPos, endPos);
        float animTime = baseDropDuration + distance * distanceSensitivity;

        // 调用父类 DropItem 实现动画控制
        newItem.Load();
        DropItem_Pos(newItem, startPos, endPos, animTime);
        
        // 只有当物品完全丢弃完后才清除数据
        if (slot.Amount <= 0)
        {
            slot.ClearData();
        }
    }

    slot.RefreshUI();
}    [Button("快速丢弃")]
    public void FastDropItem(int count = 1)
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();

        List<RaycastResult> results = new List<RaycastResult>();
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = mousePosition
        };

        EventSystem.current.RaycastAll(pointerEventData, results);

        if (results.Count > 0)
        {
            var uiItemSlot = results[0].gameObject.GetComponent<ItemSlot_UI>();

            if (uiItemSlot != null && uiItemSlot.Data != null)
            {
                DropItemByCount(uiItemSlot.Data, count);
            }
        }
    }
    
    [Button("快速丢弃整组")]
    public void FastDropStack()
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();

        List<RaycastResult> results = new List<RaycastResult>();
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = mousePosition
        };

        EventSystem.current.RaycastAll(pointerEventData, results);

        if (results.Count > 0)
        {
            var uiItemSlot = results[0].gameObject.GetComponent<ItemSlot_UI>();

            if (uiItemSlot != null && uiItemSlot.Data != null)
            {
                DropItemByCount(uiItemSlot.Data, uiItemSlot.Data.Amount);
            }
        }
    }

    #endregion
}