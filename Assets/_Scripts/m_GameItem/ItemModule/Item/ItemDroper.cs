using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class ItemDroper : Mod_ItemDroper
{
    [Header("��������")]
    public Inventory DroperInventory;
    public ItemSlot ItemToDrop_Slot;

    [Header("���䶯������")]
    public float parabolaHeight = 2f; // ���������߶�
    public float baseDropDuration = 0.5f; // ������������ʱ��
    public float distanceSensitivity = 0.1f; // ����ʱ��������ж�

    [Header("������������")]
    public float dropRepeatDelay = 0.3f; // �����ظ��������ӳ�
    public float dropRepeatInterval = 0.1f; // �����ظ������ļ��

    public Inventory_HotBar Hotbar;
    public Mod_Inventory hand;

    public override ModuleData _Data { get => modData; set => modData = value as Ex_ModData; }

    private Mod_FocusPoint faceMouse;
    public PlayerController playerController;
    public Vector2 DropPos => playerController.GetMouseWorldPosition();

    // ������ر���
    [SerializeField]
    private bool isDropButtonPressed = false;
    [SerializeField]
    private float dropButtonPressTime = 0f;
    [SerializeField]
    private bool isDropRepeatActive = false;
    [SerializeField]
    private float lastDropTime = 0f; // �ϴζ�����ʱ��
    [SerializeField]
    private ItemSlot_UI hoveredSlot = null;
    [SerializeField]
    private bool isCtrlPressed = false;

    #region ��������

    public override void Load()
    {
        base.Load();

        faceMouse = item.itemMods.GetMod_ByID(ModText.FocusPoint).GetComponent<Mod_FocusPoint>();

        Hotbar = item.itemMods.GetMod_ByID(ModText.Hotbar).GetComponent<Inventory_HotBar>();

        hand = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<Mod_Inventory>();

        playerController = item.GetComponent<PlayerController>();

        // �󶨰����¼�
        var inputActions = playerController._inputActions.Win10;
        inputActions.F.started += OnDropButtonPressed;
        inputActions.F.canceled += OnDropButtonReleased;
        inputActions.Ctrl.started += OnCtrlPressed;
        inputActions.Ctrl.canceled += OnCtrlReleased;
    }

public override void ModUpdate(float deltaTime)
{
    base.ModUpdate(deltaTime);

    // �������߼�
    if (isDropButtonPressed)
    {
        dropButtonPressTime += deltaTime;

        // ����Ƿ�ﵽ�ظ�����������
        if (!isDropRepeatActive && dropButtonPressTime >= dropRepeatDelay)
        {
            isDropRepeatActive = true;
            lastDropTime = 0f;
            Debug.Log("[ItemDroper] ������������������ģʽ");
        }

        // ���������ִ���ظ���������������hoveredSlot��
        if (isDropRepeatActive)
        {
            lastDropTime += deltaTime;
            // ����Ƿ����ظ����ʱ��
            if (lastDropTime >= dropRepeatInterval)
            {
                HandleRepeatDrop();
                lastDropTime = 0f; // ���ü�ʱ��
            }
        }
    }
    else
    {
        // ����δ����ʱ����״̬
        isDropRepeatActive = false;
        dropButtonPressTime = 0f;
        lastDropTime = 0f;
    }

    // ���µ�ǰ�����ͣ�Ĳ�λ
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
    // ��������Ƿ�����Ʒ
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
        // ֻ�е���Ʒ��ȫ��������ˢ��UI
        if (handSlot.Amount <= 0)
        {
            handSlot.RefreshUI();
        }
        return;
    }
    
    // ��������Ƿ���ѡ����Ʒ
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
            // ֻ�е���Ʒ��ȫ�����������ٶ���
            if (hotbarSlot.Amount <= 0)
            {
                Hotbar.DestroyCurrentObject(Hotbar.CurentSelectItem);
            }
            return;
        }
    }
    
    // ֻ�е����ϺͿ������û����Ʒʱ���Ŵ���UI��ͣ����Ʒ
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
        
        // �����Ʒ�Ѿ��ľ������ǵ�ǰ�����ѡ�е���Ʒ����������������
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

        // �ɿ�����ʱִ��һ�ζ�������
        // �ɿ�����ʱִ��һ�ζ�������
        if (hand.inventory.Data.itemSlots[hand.inventory.Data.Index].itemData != null)
        {
            ItemSlot handSlot = hand.inventory.Data.itemSlots[hand.inventory.Data.Index];
            if (isCtrlPressed)
            {
                // Ctrl+F ��������
                DropItemByCount(handSlot, handSlot.Amount);
            }
            else
            {
                // F ��������
                DropItemByCount(handSlot, 1);
            }

            // ֻ�е���Ʒ��ȫ��������ˢ��UI
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
                // Ctrl+F ��������
                DropItemByCount(hotbarSlot, hotbarSlot.Amount);
            }
            else
            {
                // F ��������
                DropItemByCount(hotbarSlot, 1);
            }
            
            // ֻ�е���Ʒ��ȫ�����������ٶ���
            if (hotbarSlot.Amount <= 0)
            {
                Hotbar.DestroyCurrentObject(Hotbar.CurentSelectItem);
            }
        }
        else
        {
            if (isCtrlPressed)
            {
                // Ctrl+F ���ٶ�������
                FastDropStack();
            }
            else
            {
                // F ���ٶ�������
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

    #region ��Ʒ�����ӿ�

    [Button("DropItemBySlot")]
    public void DropItemBySlot(ItemSlot slot)
    {
        if (slot == null)
        {
            Debug.LogError("����� ItemSlot Ϊ�գ�");
            return;
        }

        DropItemByCount(slot, slot.Amount);
    }
    
    [Button("DropItemStack")]
    public void DropItemStack(ItemSlot slot)
    {
        if (slot == null)
        {
            Debug.LogError("����� ItemSlot Ϊ�գ�");
            return;
        }
        
        DropItemByCount(slot, slot.Amount);
    }

public void DropItemByCount(ItemSlot slot, int count)
{
    if (count <= 0 || slot == null || slot.Amount <= 0)
    {
        Debug.LogWarning("���������Ƿ�����Ʒ��Ϊ�գ�");
        return;
    }

    if (count <= slot.Amount)
    {
        // ��¡����
        ItemData newItemData = FastCloner.FastCloner.DeepClone(slot.itemData);
        newItemData.Stack.Amount = count;
        newItemData.Stack.CanBePickedUp = false;

        // ����ԭ��Ʒ����
        slot.Amount -= count;

        Item newObject = null;
        // ʵ����������
        ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(Chunk.GetChunkPosition(transform.position).ToString(), out Chunk chunk);
        if (chunk != null)
        {
            newObject = ItemMgr.Instance.InstantiateItem(newItemData, chunk.gameObject);
        }

        if (newObject == null)
        {
            Debug.LogError("ʵ����ʧ�ܣ��Ҳ�����Դ��" + newItemData.IDName);
            return;
        }

        Item newItem = newObject.GetComponent<Item>();
        if (newItem == null)
        {
            Debug.LogError("��������ȱ�� Item �����");
            return;
        }

        // ʹ�� InstantiateItem �������ɵ� GUID ������
        // �����ֶ����� newItem.itemData = newItemData����Ϊ InstantiateItem �Ѿ���������һ��

        // ����λ��
        Vector2 startPos = transform.position;
        Vector2 endPos = DropPos;

        float distance = Vector2.Distance(startPos, endPos);
        float animTime = baseDropDuration + distance * distanceSensitivity;

        // ���ø��� DropItem ʵ�ֶ�������
        newItem.Load();
        DropItem_Pos(newItem, startPos, endPos, animTime);
        
        // ֻ�е���Ʒ��ȫ���������������
        if (slot.Amount <= 0)
        {
            slot.ClearData();
        }
    }

    slot.RefreshUI();
}    [Button("���ٶ���")]
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
    
    [Button("���ٶ�������")]
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