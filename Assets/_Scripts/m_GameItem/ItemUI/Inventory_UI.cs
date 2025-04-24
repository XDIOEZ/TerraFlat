using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using NaughtyAttributes;
using UltEvents;

// ���UI�࣬���ڹ�������û�����
public class Inventory_UI : MonoBehaviour
{
    #region �ֶ�
    [Tooltip("���õı�������")]
    public Inventory inventory;

    [Tooltip("��Ʒ���UI�б�")]
    public List<ItemSlot_UI> itemSlots_UI = new List<ItemSlot_UI>();

    [Tooltip("��Ʒ���UIԤ����")]
    public GameObject itemSlot_UI_prefab;

    [Tooltip("��������Ʒ��ѡ����")]
    public SelectSlot TargetSendItemSlot = null;
    #endregion

    #region ����
    [Tooltip("����UI�仯�¼�")]
    public UltEvent<int> OnUIChanged
    {
        get => inventory.onUIChanged;
        set => inventory.onUIChanged = value;
    }
    #endregion

    #region Unity�������ڷ���
    // �ڽű�ʵ��������ʱ���ã�����һЩ��ʼ������
    public void Awake()
    {
        inventory.Data.inventoryName = gameObject.name;
        // ���δ�ֶ�ָ�����������Դ���������л�ȡ
        if (inventory == null)
        {
            inventory = GetComponent<Inventory>();
        }
        // ����ʵ����������Ʒ��۵�UI
        //InstantiateItemSlots();
    }

    // ���ű�����ʱ���ã�����ȡ�������¼��Ա����ڴ�й©
    private void OnDestroy()
    {
        // ȡ�����Ŀ��UI�仯�¼�
        OnUIChanged -= RefreshSlotUI;
    }
    #endregion

    #region ��������
    // ��ť����ɵ��ã�����ʵ������Ʒ��۵�UI
    [Button]
    public void Instantiate_ItemSlotUI()
    {
        // ���ԭ�е���Ʒ���UI�б�
        itemSlots_UI.Clear();

        //��������������

        List<GameObject> childrenToDestroy = new List<GameObject>();

        foreach (Transform child in transform)
        {
            childrenToDestroy.Add(child.gameObject);
        }

        foreach (GameObject childObj in childrenToDestroy)
        {
           DestroyImmediate(childObj);
        }
  
        // ʵ�����µ���Ʒ���UI
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            // ʵ�����µ���Ʒ���UIԤ���壬����������Ϊ��ǰ������Ӷ���
            GameObject itemSlot_UI_go = Instantiate(itemSlot_UI_prefab, transform);
            // ����ʵ��������Ʒ���UI�����ӵ��б���
            ItemSlot_UI itemSlot_UI = itemSlot_UI_go.GetComponent<ItemSlot_UI>();

            itemSlots_UI.Add(itemSlot_UI);

            inventory.Data.itemSlots[i].Index = i;

            if(inventory.Data.itemSlots[i].SlotMaxVolume == 0|| inventory.Data.itemSlots[i].SlotMaxVolume == 128)
            inventory.Data.itemSlots[i].SlotMaxVolume = 100;

            itemSlot_UI.ItemSlot = inventory.Data.itemSlots[i];

            itemSlot_UI.ItemSlot.Index = i;

            if (itemSlot_UI.onItemClick == null)
            {
                itemSlot_UI.onItemClick = new UltEvent<int>();
            }
            // ����ɵĵ���¼�������
            itemSlot_UI.onItemClick.Clear();
            // ����µĵ���¼�������
            itemSlot_UI.onItemClick += OnItemSlotClicked;

            itemSlot_UI.onItemClick += (int index) =>
            {
                Debug.Log("����¼�");
            };

            itemSlot_UI.RefreshUI();
        }
    }

    public void AddListenersToItemSlots()
    {

        foreach (ItemSlot_UI itemSlot_UI in itemSlots_UI)
        {
            if (itemSlot_UI.onItemClick == null)
            {
                itemSlot_UI.onItemClick = new UltEvent<int>();
            }
            // ����ɵĵ���¼�������
            itemSlot_UI.onItemClick.Clear();
            // ����µĵ���¼�������
            itemSlot_UI.onItemClick += OnItemSlotClicked;

            itemSlot_UI.onItemClick += (int index) =>
            {
                Debug.Log("����¼�");
            };
        }
    }



    // ��ʼ����ˢ�����п��UI�ķ���
    [Button("ˢ�����п��UI")]
    public void RefreshAllInventoryUI()
    {
        for (int i = 0; i < itemSlots_UI.Count; i++)
        {
            // ��UI�ı��������л�����Ϊ��ʵ��������
            UpdateDataFormInventory(i);
            // ����UI�仯�¼���ˢ�¶�Ӧ������UI
            OnUIChanged.Invoke(i);
        }
    }

    void UpdateDataFormInventory(int i)
    {
        //�������������Χ����������Ϊ���һ����۵�����
        if (i >= inventory.Data.itemSlots.Count)
        {
            i = inventory.Data.itemSlots.Count - 1;
        }

        itemSlots_UI[i].ItemSlot = inventory.Data.itemSlots[i];
    }

    // ˢ��ָ����������Ʒ���UI
    public void RefreshSlotUI(int index)
    {
        UpdateDataFormInventory(index);

        // �������������Χ����������Ϊ���һ����۵�����
        if (index >= itemSlots_UI.Count)
        {
            index = itemSlots_UI.Count - 1;
        }
        // ���ö�Ӧ��������Ʒ���UI��ˢ�·���
        itemSlots_UI[index].RefreshUI();
    }

    #endregion

    #region ˽�з���
    // ������Ʒ��۵���¼�
    private void OnItemSlotClicked(int _index_)
    {
        int LocalIndex = _index_;
        int InputIndex = _index_;

        if (TargetSendItemSlot.HandInventoryUI.inventory.Data.inventoryName == "�ֲ����")
        {
            InputIndex = 0;
        }

        // �������Ĳ�۱仯�¼�
        inventory.onSlotChanged?.Invoke(LocalIndex, TargetSendItemSlot.HandInventoryUI.inventory.GetItemSlot(InputIndex));

        // ˢ�����ϲ�۵�UI
        TargetSendItemSlot.HandInventoryUI.OnUIChanged.Invoke(InputIndex);
    }
    #endregion
}