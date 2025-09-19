using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Inventory : Module,IInventory
{
    public InventoryModuleData inventoryModuleData = new InventoryModuleData();
    public override ModuleData _Data { get => inventoryModuleData; set => inventoryModuleData = (InventoryModuleData)value; }
    public Inventory _Inventory { get => inventory; set => inventory = value; }
    [Tooltip("���������ڴ����Ʒ")]
    public Inventory inventory;
    public BasePanel basePanel;
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Bag;
        }
    }

    public override void Load()
    {
        basePanel = GetComponent<BasePanel>();
        if (_Inventory == null)
        {
            _Inventory = GetComponent<Inventory>();
        }
        if (inventoryModuleData.Data.Count == 0)
        {
            inventoryModuleData.Data[_Data.Name] = inventory.Data;
        }
        else
        {
            inventory.Data = inventoryModuleData.Data[_Data.Name];
        }

        if(Item_Data.ModuleDataDic.ContainsKey(_Data.Name))
        _Data = Item_Data.ModuleDataDic[_Data.Name];

        inventory.Owner = item;

        if (item.itemMods.GetMod_ByID(ModText.Hand))
        {
            inventory.DefaultTarget_Inventory =
                          item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;
        }
        else
        {
            inventory.DefaultTarget_Inventory = Inventory_Hand.PlayerHand;
        //   Debug.Log("Mod_Inventory: " + item.name + " û���ҵ�Mod_Hand");
        }

        // �����Ʒ�Ƿ��ʰȡ�������ʰȡ���������
        CheckAndHidePanelIfPickable();

        // �ָ����λ�� - ��ǿ�汾����Ӹ�����
        if (basePanel != null && basePanel.Dragger != null)
        {
            var draggerRectTransform = basePanel.Dragger.GetComponent<RectTransform>();
            if (draggerRectTransform != null)
            {
                // ȷ���������������Ч��
                if (inventoryModuleData.PanleRectPosition != null && 
                    IsValidVector2(inventoryModuleData.PanleRectPosition))
                {
                    draggerRectTransform.anchoredPosition = inventoryModuleData.PanleRectPosition;
                }
            }
        }

        _Inventory.Init();
    }


    [Button]
    public override void Save()
    {
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
                    inventoryModuleData.PanleRectPosition = draggerRectTransform.anchoredPosition;
                }
            }
        }

        inventory.Save();
        Item_Data.ModuleDataDic[_Data.Name] = inventoryModuleData;
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
    Inventory _Inventory { get; set; }
}