using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class StorageBox : Item , IInteract,IUI,IInventoryData
{
    public StorageData storageData;

    public UltEvent _onInventoryData_Dict_Changed;
    public UltEvent OnInventoryData_Dict_Changed { get => _onInventoryData_Dict_Changed; set => _onInventoryData_Dict_Changed = value; }

    public override ItemData Item_Data
    {
        get
        {
            return storageData;
        }
        set
        {
            storageData = value as StorageData;
        }
    }

    public SelectSlot SelectSlot { get; set; }

    public Canvas Canvas { get; set; }

    public Dictionary<string, Inventory_Data> Data_InventoryData
    {
        get
        {
            return storageData.Item_inventoryData_Dict;
        }
        set
        {
            storageData.Item_inventoryData_Dict = value;
        }
    }

    [ShowNonSerializedField]
    public Dictionary<string, Inventory> children_Inventory_GameObject = new Dictionary<string, Inventory>();
    public Dictionary<string, Inventory> Children_Inventory_GameObject
    {
        get
        {
            return children_Inventory_GameObject;
        }
        set
        {
            children_Inventory_GameObject = value;
        }
    }
    #region 交互接口实现
    public void Interact_Cancel(IInteracter interacter = null)
    {
        HideUI();
    }

    public void Interact_Start(IInteracter interacter = null)
    {
        SwitchUI();
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        throw new System.NotImplementedException();
    }
    #endregion

    #region UI接口实现
    void SwitchUI()
    {
        Canvas.enabled = !Canvas.enabled;
    }
    void HideUI()
    {
        Canvas.enabled = false;
    }
    #endregion

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    #region 生命周期
    void Awake()
    {
        IInventoryData inventoryData = this;
        inventoryData.FillDict_SetBelongItem(transform);
    }
    // Start is called before the first frame update
    void Start()
    {
        Canvas = GetComponentInChildren<Canvas>();
    }


    #endregion
}

