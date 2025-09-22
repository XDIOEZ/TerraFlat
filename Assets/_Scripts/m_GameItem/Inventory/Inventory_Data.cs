using FastCloner.Code;
using Force.DeepCloner;
using MemoryPack;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using Sirenix.OdinInspector;
using Random = UnityEngine.Random; // ���Odin����

[Serializable]
[MemoryPackable]
public partial class Inventory_Data
{
    //TODO ����Event - ����ɣ�Event_RefreshUI��������UIˢ�µ��¼�
    public string Name = string.Empty;                      // ��������
    public List<ItemSlot> itemSlots = new List<ItemSlot>(); // ��Ʒ���б�
    public int Index = 0;                      // ��ǰѡ�в�λ����

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public UltEvent<int> Event_RefreshUI = new UltEvent<int>(); // UIˢ���¼�

    [FastClonerIgnore]
    public bool IsFull => itemSlots.TrueForAll(slot => slot.itemData != null);

    // ���캯��
    [MemoryPackConstructor]
    public Inventory_Data(List<ItemSlot> itemSlots, string Name)
    {
        this.itemSlots = itemSlots;
        this.Name = Name;
    }

    #region ��۲����߼�

    public void RemoveItemAll(ItemSlot itemSlot, int index = 0)
    {
        itemSlot.itemData = null;
        Event_RefreshUI.Invoke(index);
    }

    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        itemSlots[index].itemData = inputItemData;
    }

    public ItemSlot GetItemSlot(int index)
    {
        if (index < 0 || index >= itemSlots.Count)
            return itemSlots[0];
        return itemSlots[index];
    }

    public ItemData GetItemData(int index) => GetItemSlot(index).itemData;

    public void ChangeItemDataAmount(int index, float amount)
    {
        itemSlots[index].itemData.Stack.Amount += amount;
    }

    #endregion

    #region ���������߼�

    public void ChangeItemData_Default(int index, ItemSlot inputSlotHand)
    {
        float rate = 1f;
        if (inputSlotHand.Belong_Inventory == null)
        {
            return;
        }
        var handInventory = inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>();
        if (handInventory != null)
            rate = handInventory.GetItemAmountRate;

        var localSlot = itemSlots[index];
        var localData = localSlot.itemData;
        var inputData = inputSlotHand.itemData;



        // ���1��������Ϊ��
        if (localData == null && inputData == null) return;

        // ���2���������壬���ؿ�
        if (inputData != null && localData == null)
        {
            int changeAmount = Mathf.CeilToInt(inputData.Stack.Amount * rate);
            ChangeItemAmount(inputSlotHand, localSlot, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ���3���ֿգ�������
        if (inputData == null && localData != null)
        {
            int changeAmount = Mathf.CeilToInt(localData.Stack.Amount * rate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ���4�����⽻�����������ݲ�һ�£�
        if (inputData.Stack.Volume >= 2 || localData.Stack.Volume >= 2)
        {
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ���4�����⽻�����������ݲ�һ�£�
        if (inputData.ItemSpecialData != localData.ItemSpecialData)
        {
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            Debug.Log("���⽻��");
            return;
        }

        // ���5����Ʒ��ͬ���ѵ�����
        if (inputData.IDName == localData.IDName)
        {
            int changeAmount = Mathf.CeilToInt(localData.Stack.Amount * rate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ���6����Ʒ��ͬ��ֱ�ӽ���
        localSlot.Change(inputSlotHand);
        Event_RefreshUI.Invoke(index);
        Debug.Log($"(��Ʒ��ͬ)������Ʒ��λ:{index} ��Ʒ:{inputSlotHand.itemData.IDName}");
    }

    public bool ChangeItemAmount(ItemSlot localSlot, ItemSlot inputSlotHand, int count)
    {
        if (inputSlotHand.itemData == null)
        {
            var tempData = FastCloner.FastCloner.DeepClone(localSlot.itemData);
            tempData.Stack.Amount = 0;
            inputSlotHand.itemData = tempData;
        }

        if (localSlot.itemData.ItemSpecialData != inputSlotHand.itemData.ItemSpecialData)
            return false;

        int changed = 0;

        while (changed < count &&
               localSlot.itemData.Stack.Amount > 0 &&
               inputSlotHand.itemData.Stack.Amount < inputSlotHand.SlotMaxVolume)
        {
            localSlot.itemData.Stack.Amount--;
            inputSlotHand.itemData.Stack.Amount++;
            changed++;
        }

        if (localSlot.itemData.Stack.Amount <= 0)
            localSlot.ClearData();

        return changed > 0;
    }

    #endregion

    #region �����ת���߼�

    public bool TryAddItem(ItemData inputItemData, bool doAdd = true)
    {
        if (inputItemData == null) return false;

        float unitVolume = inputItemData.Stack.Volume;
        float remainingAmount = inputItemData.Stack.Amount;
        bool addedAny = false;

        // �Ƕѵ���Ʒ���������1��
        if (unitVolume > 1)
        {
            for (int i = 0; i < itemSlots.Count; i++)
            {
                if (itemSlots[i].itemData == null)
                {
                    if (doAdd)
                    {
                        SetOne_ItemData(i,inputItemData);
                        Event_RefreshUI.Invoke(i);
                        inputItemData.Stack.CanBePickedUp = false;
                    }
                    return true;
                }
            }
            return false;
        }

        // �ѵ���Ʒ�����Ϊ1��
        for (int i = 0; i < itemSlots.Count && remainingAmount > 0; i++)
        {
            var slot = itemSlots[i];
            bool hasItem = slot.itemData != null;
            bool sameItem = hasItem &&
                            slot.itemData.IDName == inputItemData.IDName &&
                            slot.itemData.ItemSpecialData == inputItemData.ItemSpecialData;

            if ((!hasItem && slot.IsFull) || (hasItem && (!sameItem || slot.IsFull)))
                continue;

            float currentVol = hasItem ? slot.itemData.Stack.CurrentVolume : 0f;
            float canAdd = slot.SlotMaxVolume - currentVol;
            float toAdd = Mathf.Min(remainingAmount, canAdd);
            if (toAdd <= 0f) continue;

            if (doAdd)
            {
                if (hasItem)
                    ChangeItemDataAmount(i, toAdd);
                else
                {
                    var newItem = FastCloner.FastCloner.DeepClone(inputItemData);
                    newItem.Stack.Amount = toAdd;
                    SetOne_ItemData(i, newItem);
                }
                Event_RefreshUI.Invoke(i);
            }

            remainingAmount -= toAdd;
            addedAny = true;
        }

        if (doAdd)
        {
            inputItemData.Stack.CanBePickedUp = false;
            if (remainingAmount > 0)
                Debug.LogWarning($"��Ʒ���δ��ȫ��ɣ�ʣ�� {remainingAmount} ��δ��ӡ�");
        }

        return addedAny;
    }

    /// <summary>
    /// ��������Ʒ��֮��ת��ָ��������upToCount������Ʒ��
    /// ת���߼��������¼�飺
    /// - ������λ��Ч���Ҳ���ͬ
    /// - ��Դ��λ����Ʒ������������
    /// - ���Ŀ���λ������Ʒ��������������Դ��Ʒһ�£������������ݣ�
    /// - ����Ʒ���ɶѵ���Volume > 1�������ܺϲ�������ղ۲�����ת��
    /// - ת�ƺ��Զ����� UI ������
    /// </summary>
    public bool TransferItemQuantity(ItemSlot slotFrom, ItemSlot slotTo, int upToCount)
    {
        if (slotFrom == null || slotTo == null || slotFrom == slotTo || upToCount <= 0)
            return false;

        var dataFrom = slotFrom.itemData;
        if (dataFrom == null || dataFrom.Stack.Amount <= 0)
            return false;

        var dataTo = slotTo.itemData;

        // ��Ŀ���λ������Ʒ����ȷ��ID����������һ��
        if (dataTo != null &&
            (dataTo.IDName != dataFrom.IDName || dataTo.ItemSpecialData != dataFrom.ItemSpecialData))
            return false;

        // ����Ʒ���ɶѵ���Volume > 1�������ܽ��жѵ�ʽת�ƣ�ֻ��ֱ���ƶ��������ղ�
        if (dataFrom.Stack.Volume > 1)
        {
            // �ǿղ�λ���ܽ��ղ��ɶѵ���Ʒ
            if (dataTo != null)
                return false;

            // ֻ����ת��һ��
            var singleData = dataFrom;
            if (dataFrom.Stack.Amount == 1)
            {
                // ֱ�Ӱ�Ǩ���ã����� Clone������ GC��
                slotTo.itemData = dataFrom;
                slotFrom.ClearData();
            }
            else
            {
                // ��ԭ�����и��Ƴ�һ���¶���
                var newData = dataFrom.DeepClone();
                newData.Stack.Amount = 1;
                dataFrom.Stack.Amount -= 1;
                slotTo.itemData = newData;
            }

            slotFrom.RefreshUI();
            slotTo.RefreshUI();
            return true;
        }

        // �ѵ��߼�����
        int transferCount = Mathf.Min(upToCount, (int)dataFrom.Stack.Amount);

        // ��¡һ��ת�ƶ�������ת������
        var transferData = dataFrom.DeepClone();
        transferData.Stack.Amount = transferCount;

        // �۳���Դ��Ʒ����
        dataFrom.Stack.Amount -= transferCount;
        if (dataFrom.Stack.Amount <= 0)
            slotFrom.ClearData();

        // ���Ŀ��Ϊ�գ�ֱ�Ӹ�ֵ�������������
        if (dataTo == null)
            slotTo.itemData = transferData;
        else
            dataTo.Stack.Amount += transferCount;

        slotFrom.RefreshUI();
        slotTo.RefreshUI();

        return true;
    }


    #endregion


    public ItemData FindItemByTag_First(string tag)
    {
        foreach (var slot in itemSlots)
        {
            if(slot.itemData!= null && slot.itemData.ItemTags.HasTypeTag(tag))
            {
                return slot.itemData;
            }
        }
        return null;
    }
    public List<ItemData> FindItemByTag_All(string tag)
    {
        List<ItemData> result = new List<ItemData>();
        foreach (var slot in itemSlots)
        {
            if (slot.itemData!= null && slot.itemData.ItemTags.HasTypeTag(tag))
            {
                result.Add(slot.itemData);
            }
        }

        return result;
    }
    public ModuleData GetModuleByID(string ID)
    {
        foreach (var slot in itemSlots)
        {
            if (slot.itemData != null)
            {
                var moduledata = slot.itemData.GetModuleData_Frist(ID);
                if (moduledata != null)
                {
                    return moduledata;
                }
            }
        }
        return null;
    }
    //TODO ���Ӹ���ID��ȡ��Ʒ�ķ��� - �����
    public ItemSlot GetItemSlotByModuleID(string moduleID)
    {
        foreach (var slot in itemSlots)
        {
            if (slot.itemData != null)
            {
                var module = slot.itemData.GetModuleData_Frist(moduleID); // �����еķ���
                if (module != null)
                {
                    return slot;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// ����Prefabע��ItemData��ָ����Slot��
    /// </summary>
    /// <param name="prefab">��ƷԤ���壬�������Item���</param>
    /// <param name="count">��Ʒ����</param>
    /// <param name="index">Ҫע��Ĳ�λ����</param>
    [Button("ע����Ʒ����λ")] // Odin���ԣ���Inspector����ʾΪ��ť
    [LabelText("ע����Ʒ")] // Odin���ԣ��Զ����ǩ�ı�
    public void InjectItemData(
        [LabelText("��ƷԤ����")] GameObject prefab, 
        [LabelText("����")] [MinValue(1)] int count, 
        [LabelText("��λ����")] [MinValue(0)] int index)
    {
        // ������֤
        if (prefab == null)
        {
            Debug.LogError("ע��ʧ�ܣ�Prefab����Ϊ��");
            return;
        }

        if (index < 0 || index >= itemSlots.Count)
        {
            Debug.LogError($"ע��ʧ�ܣ����� {index} ������Χ [0, {itemSlots.Count - 1}]");
            return;
        }

        // ��ȡPrefab�ϵ�Item���
        Item itemComponent = prefab.GetComponent<Item>();
        if (itemComponent == null)
        {
            Debug.LogError($"ע��ʧ�ܣ�Prefab {prefab.name} ���Ҳ���Item���");
            return;
        }
         // ȷ��ItemData�Ѿ���ʼ�����
        // ��¡ItemData
        ItemData clonedItemData = itemComponent.IsPrefabInit();
        if (clonedItemData == null)
        {
            Debug.LogError($"ע��ʧ�ܣ��޷���¡ {prefab.name} ��ItemData");
            return;
        }

        // ��������
        clonedItemData.Stack.Amount = count;
        
        // ע�뵽ָ����λ
        SetOne_ItemData(index, clonedItemData);
        
        // ����UIˢ��
        Event_RefreshUI.Invoke(index);
        
        Debug.Log($"�ɹ�ע����Ʒ {prefab.name} x{count} ����λ {index}");
    }
    /// <summary>
    /// �Զ�ע����Ʒ�������У����ܲ��ҿղ�λ��ɶѵ���λ�����⸲��������Ʒ
    /// </summary>
    /// <param name="prefab">��ƷԤ���壬�������Item���</param>
    /// <param name="count">��Ʒ����</param>
    [Button("�Զ�ע����Ʒ")] // Odin���ԣ���Inspector����ʾΪ��ť
    [LabelText("�Զ�ע����Ʒ")] // Odin���ԣ��Զ����ǩ�ı�
    public void AutoInjectItemData(
        [LabelText("��ƷԤ����")] GameObject prefab,
        [LabelText("����")][MinValue(1)] int count)
    {
        // ������֤
        if (prefab == null)
        {
            Debug.LogError("�Զ�ע��ʧ�ܣ�Prefab����Ϊ��");
            return;
        }

        if (count <= 0)
        {
            Debug.LogError("�Զ�ע��ʧ�ܣ������������0");
            return;
        }

        // ��ȡPrefab�ϵ�Item���
        Item itemComponent = prefab.GetComponent<Item>();
        if (itemComponent == null)
        {
            Debug.LogError($"�Զ�ע��ʧ�ܣ�Prefab {prefab.name} ���Ҳ���Item���");
            return;
        }

        // ȷ��ItemData�Ѿ���ʼ�����
        

        // ��¡ItemData
        ItemData itemData = itemComponent.IsPrefabInit();
        if (itemData == null)
        {
            Debug.LogError($"�Զ�ע��ʧ�ܣ��޷���¡ {prefab.name} ��ItemData");
            return;
        }

        // ��������
        itemData.Stack.Amount = count;
        itemData.Stack.CanBePickedUp = false;

        // ���������Ʒ�����Զ�����ѵ��Ϳղ�λ��
        if (TryAddItem(itemData, true))
        {
            Debug.Log($"�ɹ��Զ�ע����Ʒ {prefab.name} x{count}");
        }
        else
        {
            Debug.LogError($"�Զ�ע��ʧ�ܣ������ռ䲻�㣬�޷�ע����Ʒ {prefab.name} x{count}");
        }
    }
    /// <summary>
/// ���ע����Ʒ�������У�ֻ�ڿղ�λע�룬���Ḳ��������Ʒ
/// </summary>
/// <param name="prefabList">��ƷԤ�����б�</param>
/// <param name="minCount">ÿ����Ʒ����С����</param>
/// <param name="maxCount">ÿ����Ʒ���������</param>
/// <param name="minItems">����ע�����Ʒ������</param>
/// <param name="maxItems">���ע�����Ʒ������</param>
[Button("���ע����Ʒ")] // Odin���ԣ���Inspector����ʾΪ��ť
[LabelText("���ע����Ʒ")] // Odin���ԣ��Զ����ǩ�ı�
public void RandomInjectItems(
    [LabelText("��ƷԤ�����б�")] List<GameObject> prefabList,
    [LabelText("��С����")] [MinValue(1)] int minCount = 1,
    [LabelText("�������")] [MinValue(1)] int maxCount = 5,
    [LabelText("������Ʒ����")] [MinValue(1)] int minItems = 1,
    [LabelText("�����Ʒ����")] [MinValue(1)] int maxItems = 3)
{
    // ������֤
    if (prefabList == null || prefabList.Count == 0)
    {
        Debug.LogError("���ע��ʧ�ܣ�Prefab�б�Ϊ�ջ�δ����");
        return;
    }

    if (minCount > maxCount)
    {
        Debug.LogWarning("��С������������������Զ�����ֵ");
        int temp = minCount;
        minCount = maxCount;
        maxCount = temp;
    }

    if (minItems > maxItems)
    {
        Debug.LogWarning("������Ʒ������������Ʒ���࣬�Զ�����ֵ");
        int temp = minItems;
        minItems = maxItems;
        maxItems = temp;
    }

    // ������Ʒ��������������Prefab�б���
    maxItems = Mathf.Min(maxItems, prefabList.Count);
    minItems = Mathf.Min(minItems, maxItems);

    // ���ѡ��Ҫע�����Ʒ��������
    int itemCount = Random.Range(minItems, maxItems + 1);

    // ���ѡ���ظ�����Ʒ
    List<GameObject> selectedPrefabs = new List<GameObject>();
    List<GameObject> availablePrefabs = new List<GameObject>(prefabList);
    
    for (int i = 0; i < itemCount && availablePrefabs.Count > 0; i++)
    {
        int randomIndex = Random.Range(0, availablePrefabs.Count);
        selectedPrefabs.Add(availablePrefabs[randomIndex]);
        availablePrefabs.RemoveAt(randomIndex);
    }

    // Ϊѡ�е�ÿ����Ʒ�������������ע��
    List<GameObject> prefabsToInject = new List<GameObject>();
    List<int> countsToInject = new List<int>();

    foreach (var prefab in selectedPrefabs)
    {
        if (prefab != null)
        {
            int randomCount = Random.Range(minCount, maxCount + 1);
            prefabsToInject.Add(prefab);
            countsToInject.Add(randomCount);
        }
    }

    // ʹ���Զ�ע�뷽��ע����Ʒ
    if (prefabsToInject.Count > 0)
    {
        AutoInjectItemDataList(prefabsToInject, countsToInject);
        Debug.Log($"���ע����ɣ��ɹ�ע�� {prefabsToInject.Count} ����Ʒ");
    }
    else
    {
        Debug.LogWarning("���ע��ʧ�ܣ�û����Ч����Ʒ��ע��");
    }
}

/// <summary>
/// ���ע����Ʒ�������У��򻯰棩��ֻ�ڿղ�λע�룬���Ḳ��������Ʒ
/// </summary>
/// <param name="prefabList">��ƷԤ�����б�</param>
/// <param name="count">ÿ����Ʒ�Ĺ̶�����</param>
/// <param name="itemCount">ע�����Ʒ������</param>
[Button("���ע����Ʒ(�̶�����)")] // Odin���ԣ���Inspector����ʾΪ��ť
[LabelText("���ע����Ʒ(�̶�����)")] // Odin���ԣ��Զ����ǩ�ı�
public void RandomInjectItems(
    [LabelText("��ƷԤ�����б�")] List<GameObject> prefabList,
    [LabelText("�̶�����")] [MinValue(1)] int count = 1,
    [LabelText("��Ʒ������")] [MinValue(1)] int itemCount = 3)
{
    // ������֤
    if (prefabList == null || prefabList.Count == 0)
    {
        Debug.LogError("���ע��ʧ�ܣ�Prefab�б�Ϊ�ջ�δ����");
        return;
    }

    // ������Ʒ��������������Prefab�б���
    itemCount = Mathf.Min(itemCount, prefabList.Count);

    // ���ѡ���ظ�����Ʒ
    List<GameObject> selectedPrefabs = new List<GameObject>();
    List<GameObject> availablePrefabs = new List<GameObject>(prefabList);
    
    for (int i = 0; i < itemCount && availablePrefabs.Count > 0; i++)
    {
        int randomIndex = UnityEngine.Random.Range(0, availablePrefabs.Count);
        selectedPrefabs.Add(availablePrefabs[randomIndex]);
        availablePrefabs.RemoveAt(randomIndex);
    }

    // ʹ���Զ�ע�뷽��ע����Ʒ
    if (selectedPrefabs.Count > 0)
    {
        AutoInjectItemDataList(selectedPrefabs, count);
        Debug.Log($"���ע����ɣ��ɹ�ע�� {selectedPrefabs.Count} ����Ʒ��ÿ�� {count} ��");
    }
    else
    {
        Debug.LogWarning("���ע��ʧ�ܣ�û����Ч����Ʒ��ע��");
    }
    
}
    /// <summary>
/// �Զ�ע����Ʒ�б������У����ܲ��ҿղ�λ��ɶѵ���λ�����⸲��������Ʒ
/// </summary>
/// <param name="prefabList">��ƷԤ�����б�</param>
/// <param name="countList">��Ӧ��Ʒ�����б�</param>
[Button("�Զ�ע����Ʒ�б�")] // Odin���ԣ���Inspector����ʾΪ��ť
[LabelText("�Զ�ע����Ʒ�б�")] // Odin���ԣ��Զ����ǩ�ı�
public void AutoInjectItemDataList(
    [LabelText("��ƷԤ�����б�")] List<GameObject> prefabList, 
    [LabelText("�����б�")] List<int> countList)
{
    // ������֤
    if (prefabList == null || countList == null)
    {
        Debug.LogError("�Զ�ע��ʧ�ܣ�Prefab�б�������б���Ϊ��");
        return;
    }

    if (prefabList.Count != countList.Count)
    {
        Debug.LogError($"�Զ�ע��ʧ�ܣ�Prefab�б�����({prefabList.Count})�������б�����({countList.Count})��ƥ��");
        return;
    }

    if (prefabList.Count == 0)
    {
        Debug.LogWarning("�Զ�ע��ʧ�ܣ�Prefab�б�Ϊ��");
        return;
    }

    int successCount = 0;
    int failCount = 0;

    // �������Զ�ע��ÿ����Ʒ
    for (int i = 0; i < prefabList.Count; i++)
    {
        GameObject prefab = prefabList[i];
        int count = countList[i];

        if (prefab == null)
        {
            Debug.LogWarning($"�����յ�Prefab������ {i}��");
            failCount++;
            continue;
        }

        if (count <= 0)
        {
            Debug.LogWarning($"������Ч���� {count} ����Ʒ {prefab.name}������ {i}��");
            failCount++;
            continue;
        }

        // ��ȡPrefab�ϵ�Item���
        Item itemComponent = prefab.GetComponent<Item>();
        if (itemComponent == null)
        {
            Debug.LogError($"�Զ�ע��ʧ�ܣ�Prefab {prefab.name} ���Ҳ���Item��������� {i}��");
            failCount++;
            continue;
        }

        // ��¡ItemData
        ItemData itemData = itemComponent.IsPrefabInit();
        if (itemData == null)
        {
            Debug.LogError($"�Զ�ע��ʧ�ܣ��޷���¡ {prefab.name} ��ItemData������ {i}��");
            failCount++;
            continue;
        }

        // ��������
        itemData.Stack.Amount = count;
        itemData.Stack.CanBePickedUp = false;

        // ���������Ʒ
        if (TryAddItem(itemData, true))
        {
            Debug.Log($"�ɹ��Զ�ע����Ʒ {prefab.name} x{count}");
            successCount++;
        }
        else
        {
            Debug.LogError($"�Զ�ע��ʧ�ܣ������ռ䲻�㣬�޷�ע����Ʒ {prefab.name} x{count}");
            failCount++;
        }
    }

    Debug.Log($"�Զ�ע����Ʒ�б���ɣ��ɹ� {successCount} ����ʧ�� {failCount} ��");
}

// ���ط�����֧��ͳһ����
[Button("�Զ�ע����Ʒ�б�(ͳһ����)")]
[LabelText("�Զ�ע����Ʒ�б�(ͳһ����)")]
public void AutoInjectItemDataList(
    [LabelText("��ƷԤ�����б�")] List<GameObject> prefabList, 
    [LabelText("ͳһ����")] [MinValue(1)] int uniformCount = 1)
{
    if (prefabList == null)
    {
        Debug.LogError("�Զ�ע��ʧ�ܣ�Prefab�б���Ϊ��");
        return;
    }

    // ����ͳһ�����б�
    List<int> countList = new List<int>();
    for (int i = 0; i < prefabList.Count; i++)
    {
        countList.Add(uniformCount);
    }

    AutoInjectItemDataList(prefabList, countList);
}
}