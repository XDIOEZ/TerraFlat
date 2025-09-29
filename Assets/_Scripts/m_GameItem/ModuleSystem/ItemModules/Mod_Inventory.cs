using AYellowpaper.SerializedCollections;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Mod_Inventory : Module,IInventory
{
    public InventoryModuleData  Data = new InventoryModuleData();
    public override ModuleData _Data { get => Data; set => Data = (InventoryModuleData)value; }
    public Inventory inventory { get => InventoryRefDic["����"]; set => InventoryRefDic["����"] = value; }
    [Tooltip("Inventory�����ֵ�")]
    public SerializedDictionary<string, Inventory> inventoryRefDic = new();
    [Tooltip("Inventory�����ֵ�")]
    public SerializedDictionary<string, Inventory> InventoryRefDic { get=> inventoryRefDic; set => inventoryRefDic = value; }
    [Tooltip("ģ�����")]
    public BasePanel basePanel;
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Bag;
        }
    }

    public void OnValidate()
    {
        if(inventoryRefDic.Count == 0)
        {
            inventoryRefDic.Add("����", GetComponentInChildren<Inventory>());
        }
    }

    public override void Load()
{
    basePanel = GetComponent<BasePanel>();
    if (inventory == null)
    {
        inventory = GetComponent<Inventory>();
    }
    if (Data.Data.Count == 0)
    {
        Data.Data[_Data.Name] = inventory.Data;
    }
    else
    {
        inventory.Data = Data.Data[_Data.Name];
    }

    if(Item_Data.ModuleDataDic.ContainsKey(_Data.Name))
    _Data = Item_Data.ModuleDataDic[_Data.Name];

    inventory.Owner = item;

    if (item.itemMods.GetMod_ByID(ModText.Hand))
    {
        inventory.DefaultTarget_Inventory =
                      item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>().GetDefaultTargetInventory();
    }
    else
    {
        inventory.DefaultTarget_Inventory = Inventory_Hand.PlayerHand;
    }

    // �����Ʒ�Ƿ��ڿ�ʰȡ״̬�������ʰȡ��ر����
    CheckAndHidePanelIfPickable();

    // �ָ����λ�úͿ���״̬
    if (basePanel != null)
    {
        // �ָ����λ�� - ��ǿ�汾�����Ӹ�����
        if (basePanel.Dragger != null)
        {
            var draggerRectTransform = basePanel.Dragger.GetComponent<RectTransform>();
            if (draggerRectTransform != null)
            {
                // ֻ�е������λ��������Ч�Ҳ�Ϊ��ʱ��Ӧ��λ��
                if (Data.PanleRectPosition != null && 
                    IsValidVector2(Data.PanleRectPosition) &&
                    (Data.PanleRectPosition.x != 0 || Data.PanleRectPosition.y != 0))
                {
                    draggerRectTransform.anchoredPosition = Data.PanleRectPosition;
                }
                // ����ǳ��μ�����λ������Ϊ�գ���ʹ��Prefab�Դ���λ�ã�����Ҫ����
            }
        }
        
        // �ָ���忪��״̬
        if (Data.BasePanelIsOpen)
        {
            basePanel.Open();
        }
        else
        {
            basePanel.Close();
        }
    }

    item.itemMods.GetMod_ByID(ModText.Interact, out Mod_Interaction interactable);
    if(interactable!= null)
    {
        interactable.OnAction_Start += Interact_Start;
        interactable.OnAction_Stop += Interact_Stop;
    }


        inventory.Init();


      
    }
    public void Start()
    {
        GameRes.Instance.InventoryInitGet(Data.InventoryInitName, out Inventoryinit inventoryInit);
        if (inventoryInit != null)
        {
            inventory.TryInitializeItems(inventoryInit);

        }
        //��ʼ��ˢ��UI
        inventory.RefreshUI();
    }
    //�����˷�������
    public void Interact_Start(Item item_)
    {
        item_.itemMods.GetMod_ByID(ModText.Hand, out Mod_Inventory handMod);
        if (handMod == null) return;
        inventory.DefaultTarget_Inventory = handMod.inventory;
        basePanel.Toggle();
    }
    //��ҽ�������
    public void Interact_Stop(Item item_)
    {
        inventory.DefaultTarget_Inventory = null;
        basePanel.Close();
    }

    [Button]
    public override void Save()
    {
        // ������忪��״̬
        if (basePanel != null)
        {
            Data.BasePanelIsOpen = basePanel.IsOpen();
        }
        
        // �������λ�� - ��ǿ�汾����Ӹ�����
        if (basePanel != null && basePanel.Dragger != null)
        {
            var draggerRectTransform = basePanel.Dragger.GetComponent<RectTransform>();
            if (draggerRectTransform != null)
            {
                // ֻ������崦����Ч״̬ʱ�ű���λ��
                if (draggerRectTransform.anchoredPosition != null &&
                    IsValidVector2(draggerRectTransform.anchoredPosition))
                {
                    Data.PanleRectPosition = draggerRectTransform.anchoredPosition;
                }
            }
        }

        inventory.Save();
        Item_Data.ModuleDataDic[_Data.Name] = Data;
    }

    // �������������Vector2�Ƿ���Ч
    private bool IsValidVector2(Vector2 vector)
    {
        // ����Ƿ������Чֵ
        return !float.IsNaN(vector.x) && !float.IsNaN(vector.y) &&
               !float.IsInfinity(vector.x) && !float.IsInfinity(vector.y);
    }
    
    // TODO��ɣ������Ʒ��ʰȡ״̬���������
    /// <summary>
    /// �����Ʒ�Ƿ��ʰȡ�������ʰȡ���������
    /// </summary>
    private void CheckAndHidePanelIfPickable()
    {
        // ���basePanel�Ƿ����
        if (basePanel == null)
        {
            basePanel = GetComponent<BasePanel>();
            if (basePanel == null)
            {
                Debug.LogWarning("�޷��ҵ�BasePanel�����������������߼�");
                return;
            }
        }
        
        // �����Ʒ�����Ƿ��������Ʒ��ʰȡ
        if (item != null && item.itemData != null && item.itemData.Stack.CanBePickedUp)
        {
            // ʹ��BasePanel��Close�����������
            basePanel.Close();
            Debug.Log($"��Ʒ {item.name} ��ʰȡ���Զ����ر������");
        }
        // �����Ʒ����ʰȡ���򱣳���嵱ǰ״̬����ǿ����ʾ��
    }

    // ��ѡ���ṩһ���������������ⲿ����
    /// <summary>
    /// ����������������Ʒ��ʰȡ״̬���������ʾ
    /// </summary>
    public void UpdatePanelVisibilityBasedOnPickableState()
    {
        CheckAndHidePanelIfPickable();
    }
}

public interface IInventory
{
    [Tooltip("Inventory�����ֵ�")]
    public SerializedDictionary<string, Inventory> InventoryRefDic { get; set; }
    
    [Tooltip("Ĭ�Ϸ��ص�Ŀ��Inventory")]
    public Inventory GetDefaultTargetInventory()
    {
        if (InventoryRefDic == null || InventoryRefDic.Count == 0)
            return null;
            
        // ���ص�һ��Inventory
        return InventoryRefDic.Values.First();
    }
    
    [Tooltip("�������һ��Inventory")]
    public Inventory GetRandomTargetInventory()
    {
        if (InventoryRefDic == null || InventoryRefDic.Count == 0)
            return null;
            
        // ��ֵת��Ϊ���鲢���ѡ��һ��
        var inventories = InventoryRefDic.Values.ToArray();
        int randomIndex = UnityEngine.Random.Range(0, inventories.Length);
        return inventories[randomIndex];
    }
}
[Serializable]
[MemoryPackable]
public partial class InventoryModuleData : ModuleData
{
    [ShowInInspector]
    public Dictionary<string, Inventory_Data> Data = new Dictionary<string, Inventory_Data>();
    public Vector3 PanleRectPosition = Vector3.zero;//TODO �������������һ��Vector3���������ڱ�������λ��
    public string InventoryInitName = "";
    public bool BasePanelIsOpen = true;
}