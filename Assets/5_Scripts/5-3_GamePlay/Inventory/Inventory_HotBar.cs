using DG.Tweening;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 快捷栏模块（Module形态）
/// 内部通过 RuntimeInventory 复用原有 Inventory 逻辑。
/// </summary>
public class Inventory_HotBar : Module, IInventory
{
#region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

#endregion

#region 模组参数

    [SerializeReference]
    public List<string> RawData = new List<string>();

    [System.Serializable]
    public class HotBarRuntimeInventory : Inventory
    {
        [System.NonSerialized]
        public Inventory_HotBar Owner;

        public override void OnValidate()
        {
            base.OnValidate();
            Owner?.OnInventoryValidate();
        }

        public override void InitData()
        {
            base.InitData();
            Owner?.OnInventoryInitData();
        }

        public override void InitUI()
        {
            base.InitUI();
            Owner?.OnInventoryInitUI();
        }

        public override void OnLeftClick(int index)
        {
            base.OnLeftClick(index);
            Owner?.OnInventoryLeftClick(index);
        }

        public override void OnShiftQuickTransfer(int index)
        {
            base.OnShiftQuickTransfer(index);
            Owner?.OnInventoryShiftQuickTransfer(index);
        }

        public override void ModUpdate(float deltaTime)
        {
            base.ModUpdate(deltaTime);
            Owner?.OnInventoryModUpdate(deltaTime);
        }
    }

    [Header("快捷栏运行时库存")]
    [SerializeReference]
    public HotBarRuntimeInventory RuntimeInventory = new HotBarRuntimeInventory();

    [Header("快捷栏设置")]
    [SerializeReference]
    public Transform spawnLocation;
    public int HotBarMaxVolume = 9;

    [Header("UI")]
    public GameObject SelectBoxPrefab;
    [Range(0.01f, 0.5f)]
    public float SelectBoxChangeDuration = 0.1f;
    [ReadOnly] public GameObject SelectBox;
    [ReadOnly] public ItemSlot CurrentSelectItemSlot;
    [ReadOnly] public Item CurentSelectItem;
    [ReadOnly] public GameObject currentObject;

    private Mod_FocusPoint faceMouse;
    private Mod_TurnBack turnBody;

    private InputAction _rightClickAction;
    private InputAction _mouseScrollAction;

    public Inventory_Data Data => RuntimeInventory?.Data;
    public List<ItemSlot_UI> itemSlot_UI => RuntimeInventory?.itemSlot_UI;

    public int CurrentIndex
    {
        get => Data != null ? Data.Index : 0;
        private set
        {
            if (Data != null)
            {
                Data.Index = value;
            }
        }
    }

    public int MaxIndex => Data?.itemSlots != null ? Data.itemSlots.Count : 0;

#endregion

#region 生命周期

    public void OnValidate()
    {
        EnsureRuntimeInventoryBinding();
        RuntimeInventory?.OnValidate();
    }

    public override void Load()
    {
        ModSaveData.ReadData(ref RawData);

        EnsureRuntimeInventoryBinding();

        if (RuntimeInventory == null)
        {
            throw new System.InvalidOperationException("[Inventory_HotBar] RuntimeInventory 为空，无法加载");
        }

        EnsureHotBarSlots();

        RuntimeInventory.item = item;
        RuntimeInventory.InitData();
        EnsureHotBarUIOnLoad();
        BindInventoryController();
    }

    public override void Save()
    {
        ModSaveData.WriteData(RawData);
    }

    private void OnDestroy()
    {
        UnbindHotbarInput();
        RuntimeInventory?.UnbindController();
    }

    public override void ModUpdate(float deltaTime)
    {
        RuntimeInventory?.ModUpdate(deltaTime);
    }

#endregion

#region RuntimeInventory回调

    private void EnsureRuntimeInventoryBinding()
    {
        if (RuntimeInventory == null)
        {
            RuntimeInventory = new HotBarRuntimeInventory();
        }

        RuntimeInventory.Owner = this;
    }

    private void OnInventoryValidate()
    {
        if (_Data != null)
        {
            _Data.ID = ModText.Hotbar;
            _Data.Name = ModText.Hotbar;
        }

        if (Data != null)
        {
            Data.Name = ModText.Hotbar;
        }
    }

    private void OnInventoryInitData()
    {
        if (spawnLocation == null)
        {
            if (item != null)
            {
                spawnLocation = item.transform;
            }
            else
            {
                Debug.LogWarning("[Inventory_HotBar] spawnLocation 未配置且 item 为空");
            }
        }

        GetRequiredComponents();
        BindHotbarInput();
    }

    private void OnInventoryInitUI()
    {
        if (itemSlot_UI == null || itemSlot_UI.Count == 0)
        {
            return;
        }

        if (SelectBox != null)
        {
            Destroy(SelectBox);
        }

        SelectBox = Instantiate(SelectBoxPrefab, itemSlot_UI[0].transform);
        SwitchItem(CurrentIndex);
    }

    private void OnInventoryLeftClick(int index)
    {
        SwitchItem(index);
    }

    private void OnInventoryShiftQuickTransfer(int index)
    {
        SyncCurrentHeldItemWithSlot();
    }

    private void OnInventoryModUpdate(float deltaTime)
    {
        SyncCurrentHeldItemWithSlot();
    }

    private void EnsureHotBarSlots()
    {
        if (Data == null)
        {
            throw new System.InvalidOperationException("[Inventory_HotBar] Data 为空，无法初始化快捷栏槽位");
        }

        if (Data.itemSlots == null)
        {
            Data.itemSlots = new List<ItemSlot>();
        }

        int targetCount = Mathf.Max(HotBarMaxVolume, 1);

        for (int i = 0; i < targetCount; i++)
        {
            if (i >= Data.itemSlots.Count)
            {
                Data.itemSlots.Add(new ItemSlot(i));
            }
            else if (Data.itemSlots[i] == null)
            {
                Data.itemSlots[i] = new ItemSlot(i);
            }

            Data.itemSlots[i].Index = i;
        }
    }

    private void EnsureHotBarUIOnLoad()
    {
        if (RuntimeInventory.basePanel == null)
        {
            RuntimeInventory.EnsurePanelCreated();
        }

        if (RuntimeInventory.basePanel != null && (itemSlot_UI == null || itemSlot_UI.Count == 0))
        {
            RuntimeInventory.InitUI();
        }

        if (RuntimeInventory.basePanel != null)
        {
            RuntimeInventory.basePanel.Open();
        }
    }

#endregion

#region 输入

    private void BindInventoryController()
    {
        if (item == null)
        {
            return;
        }

        GameController controller = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (controller == null)
        {
            controller = item.GetComponent<GameController>();
        }

        RuntimeInventory.BindController(controller);
    }

    private void BindHotbarInput()
    {
        UnbindHotbarInput();

        if (item == null)
        {
            return;
        }

        GameController controller = item.GetComponent<GameController>();
        if (controller == null || controller._inputActions == null)
        {
            return;
        }

        _rightClickAction = controller._inputActions.Win10.RightClick;
        _mouseScrollAction = controller._inputActions.Win10.MouseScroll;

        if (_rightClickAction != null)
        {
            _rightClickAction.performed += OnRightClickPerformed;
        }

        if (_mouseScrollAction != null)
        {
            _mouseScrollAction.started += OnScrollSwitch;
        }
    }

    private void UnbindHotbarInput()
    {
        if (_rightClickAction != null)
        {
            _rightClickAction.performed -= OnRightClickPerformed;
            _rightClickAction = null;
        }

        if (_mouseScrollAction != null)
        {
            _mouseScrollAction.started -= OnScrollSwitch;
            _mouseScrollAction = null;
        }
    }

    private void OnRightClickPerformed(InputAction.CallbackContext ctx)
    {
        if (CurentSelectItem == null)
        {
            Debug.LogWarning("[Inventory_HotBar] 右键使用失败：当前未持有物品");
            return;
        }

        CurentSelectItem.Act();
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

#region 对外兼容接口

    public void RefreshUI(int index)
    {
        RuntimeInventory?.RefreshUI(index);
    }

    public void RefreshUI()
    {
        RuntimeInventory?.RefreshUI();
    }

    public float GetCurrentItemDurabilityPercentage()
    {
        if (CurentSelectItem?.itemData == null) return 1f;

        var data = CurentSelectItem.itemData;
        return data.MaxDurability > 0 ? data.Durability / data.MaxDurability : 0f;
    }

    public Inventory GetDefaultTargetInventory()
    {
        return RuntimeInventory;
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
        if (Data == null || Data.itemSlots == null || index < 0 || index >= Data.itemSlots.Count)
        {
            return;
        }

        var slot = Data.itemSlots[index];
        if (slot?.itemData == null) return;

        if (spawnLocation == null)
        {
            throw new System.InvalidOperationException("[Inventory_HotBar] spawnLocation 为空，无法实例化手持物品");
        }

        ItemData data = slot.itemData;

        Item itemInstance = ItemMgr.Instance.InstantiateItem(
            data.IDName,
            position: default,
            parent: spawnLocation.gameObject
        );

        ConfigureItemInstance(itemInstance, data, slot);
    }

    private void UnloadCurrentItem()
    {
        if (CurentSelectItem == null) return;

        CurentSelectItem.SetInHand(false);

        if (faceMouse != null)
        {
            faceMouse.targetRotationTransforms.Remove(CurentSelectItem.transform);
        }

        if (turnBody != null)
        {
            turnBody.controlledTransforms_Direction.Remove(CurentSelectItem.transform);
            turnBody.controlledTransforms_Position.Remove(CurentSelectItem.transform);
        }

        CurentSelectItem.OnUIRefresh -= RefreshUI;
        CurentSelectItem.OnItemDestroy -= OnDestroyCurrentObject;

        Destroy(CurentSelectItem.gameObject);

        CurentSelectItem = null;
        currentObject = null;
    }

#endregion

#region Item配置

    private void ConfigureItemInstance(Item itemInstance, ItemData data, ItemSlot slot)
    {
        Transform tf = itemInstance.transform;
        tf.SetParent(spawnLocation, false);
        tf.localPosition = Vector3.zero;

        Vector3 rot = tf.localEulerAngles;
        rot.z = 0;
        tf.localEulerAngles = rot;

        itemInstance.itemData = data;
        itemInstance.Owner = item;

        itemInstance.OnUIRefresh += RefreshUI;
        itemInstance.OnItemDestroy += OnDestroyCurrentObject;

        itemInstance.Load();
        itemInstance.SetInHand(true);

        CurentSelectItem = itemInstance;
        CurrentSelectItemSlot = slot;
        currentObject = itemInstance.gameObject;

        if (faceMouse != null && !faceMouse.targetRotationTransforms.Contains(tf))
        {
            faceMouse.targetRotationTransforms.Add(tf);
        }

        if (turnBody != null && !turnBody.controlledTransforms_Direction.Contains(tf))
        {
            turnBody.controlledTransforms_Direction.Add(tf);
        }

        turnBody?.UpdateAllTransformDirections();
    }

#endregion

#region UI

    private void MoveSelectBox(int index)
    {
        if (SelectBox == null || itemSlot_UI == null || index < 0 || index >= itemSlot_UI.Count) return;

        SelectBox.transform.DOKill();
        SelectBox.transform.SetParent(itemSlot_UI[index].transform, true);
        SelectBox.transform.DOLocalMove(Vector3.zero, SelectBoxChangeDuration).SetEase(Ease.OutQuad);
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
        if (item == null) return;

        item.itemMods.GetMod_ByID(ModText.FocusPoint, out faceMouse);
        item.itemMods.GetMod_ByID(ModText.TrunBody, out turnBody);

        Transform positionTransform = spawnLocation != null ? spawnLocation : item.transform;

        if (turnBody != null && positionTransform != null &&
            !turnBody.controlledTransforms_Position.Contains(positionTransform))
        {
            turnBody.controlledTransforms_Position.Add(positionTransform);
        }
    }

    public void OnDestroyCurrentObject(Item obj)
    {
        if (obj == null) return;

        obj.SetInHand(false);
        UnloadCurrentItem();
    }

    private void SyncCurrentHeldItemWithSlot()
    {
        if (Data == null || Data.itemSlots == null || Data.itemSlots.Count == 0)
            return;

        int fixedIndex = NormalizeIndex(CurrentIndex);
        if (fixedIndex != CurrentIndex)
            CurrentIndex = fixedIndex;

        ItemSlot currentSlot = Data.itemSlots[CurrentIndex];
        CurrentSelectItemSlot = currentSlot;

        ItemData slotData = currentSlot?.itemData;

        if (slotData == null)
        {
            if (CurentSelectItem != null)
                UnloadCurrentItem();

            return;
        }

        if (CurentSelectItem == null)
        {
            LoadItemFromSlot(CurrentIndex);
            return;
        }

        if (!ReferenceEquals(CurentSelectItem.itemData, slotData))
        {
            UnloadCurrentItem();
            LoadItemFromSlot(CurrentIndex);
        }
    }

#endregion
}
