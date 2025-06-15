using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using NaughtyAttributes;
using System.Linq;
using Newtonsoft.Json;
using static Recipe;
using Force.DeepCloner;

public class ManualCraftingStation : MonoBehaviour, IInteract
{
    #region �ֶζ���
    [Tooltip("�������������ڴ�źϳ������ԭ������Ʒ")]
    public Inventory inputInventory;

    [Tooltip("������������ڴ�źϳɺ�õ�����Ʒ")]
    public Inventory outputInventory;

    [Tooltip("������壬�ҽӵ�UI������Ҫ���ڽ���������룬�������ϳɰ�ť�Ȳ���")]
    public GameObject interactionPanel;

    [Tooltip("��ǰ�Ѻϳ�ʱ�䣬�����ں���ʵ�ֺϳ�ʱ�����Ƶȹ���")]
    public float currentCraftingTime;

    [Tooltip("�ϳ��嵥�ֵ�����ã��洢���п��õĺϳ��䷽����Ϊ�ϳ�������ϵ��ַ�����ʾ��ֵΪ����б�")]
    public Dictionary<string, Recipe> recipes = new Dictionary<string, Recipe>();

    [Tooltip("�ϳɴ�������¼��ҽ��кϳɲ����Ĵ���")]
    public int craftingTimes = 0;
    #endregion

    #region Unity ��������
    private void Start()
    {
        if (interactionPanel != null)
        {
            Button craftButton = interactionPanel.GetComponentInChildren<Button>();
            if (craftButton != null)
                craftButton.onClick.AddListener(OnCraftButtonClick);
        }
        LoadRecipes();
    }
    #endregion

    #region �䷽����
    [Button]
    public void LoadRecipes()
    {
        recipes = GameRes.Instance.recipeDict;
    }
    #endregion

    #region �����ӿ�ʵ��
    public void Interact_Start(IInteracter interacter = null)
    {
        interactionPanel?.SetActive(true);
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        interactionPanel?.SetActive(false);
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        throw new System.NotImplementedException();
    }
    #endregion

    #region UI�¼��ص�
    private void OnCraftButtonClick()
    {
        if (Craft(inputInventory, outputInventory, RecipeType.Crafting))
            craftingTimes++;
    }
    #endregion

    #region �ϳ��߼�����
    public bool Craft(Inventory inputInventory_, Inventory outputInventory_, RecipeType recipeType)
    {
        // �����䷽��
        string recipeKey = string.Join(",",
            inputInventory_.Data.itemSlots.Select(slot => slot._ItemData?.IDName ?? ""));

        // ����䷽�����Լ�����
        if (!GameRes.Instance.recipeDict.TryGetValue(recipeKey, out var recipe) ||
            recipe.recipeType != recipeType)
        {
            Debug.LogError($"�䷽ƥ��ʧ�ܣ��� {recipeKey} �����ڻ����� {recipeType} ��ƥ��");
            return false;
        }

        // ���������
        if (inputInventory_.Data.itemSlots.Count != recipe.inputs.RowItems_List.Count)
        {
            Debug.LogError($"���������ƥ�䣺�䷽Ҫ�� {recipe.inputs.RowItems_List.Count} ����ۣ���ǰ�� {inputInventory_.Data.itemSlots.Count} ��");
            return false;
        }

        // ׼�������Ʒ
        var itemsToAdd = new List<ItemData>();
        foreach (var output in recipe.outputs.results)
        {
            var prefab = GameRes.Instance.AllPrefabs[output.resultItem];
            if (prefab == null)
            {
                Debug.LogError($"Ԥ���岻���ڣ�{output.resultItem}���䷽��{recipe.name}��");
                return false;
            }
            ItemData newItem = prefab.GetComponent<Item>().DeepClone().Item_Data;
            newItem.Stack.Amount = output.resultAmount;
            itemsToAdd.Add(newItem);
        }

        // �����Դ�Ϳռ�
        if (!CheckEnough(inputInventory_, outputInventory_, recipe.inputs, itemsToAdd))
        {
            Debug.LogError("�ϳ�ʧ�ܣ����ϲ��������ռ䲻��");
            return false;
        }

        // ��ʾ�ϳɿ�ʼ��Ϣ
        Debug.Log($"��ʼ�ϳɣ�{recipe.name}");
        Debug.Log($"������ϣ�{recipeKey}");
        Debug.Log($"������{string.Join(", ", itemsToAdd.Select(item => $"{item.Stack.Amount}x{item.IDName}"))}");

        // ִ�кϳɣ���������Ʒ
        foreach (var item in itemsToAdd)
        {
            outputInventory_.Data.AddItem(item);
            Debug.Log($"��Ӳ��{item.Stack.Amount}x{item.IDName}");
        }

        // �۳��������
        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            // ��ʾ��ϸ�ۼ���Ϣ
            Debug.Log($"��� {i}����Ҫ {required.ItemName} x{required.amount}����ǰ�� {slot._ItemData.Stack.Amount}");

            slot._ItemData.Stack.Amount -= required.amount;
            if (slot._ItemData.Stack.Amount <= 0)
            {
                Debug.Log($"��� {i}��{required.ItemName} �Ѻľ����Ƴ���Ʒ");
                inputInventory_.Data.RemoveItemAll(slot, i);
            }
            else
            {
                Debug.Log($"��� {i}��ʣ�� {required.ItemName} x{slot._ItemData.Stack.Amount}");
            }
            inputInventory.SyncUIData(i);
        }

        Debug.Log($"�ϳ���ɣ�{recipe.name}");
        return true;
    }

    private  bool CheckEnough(Inventory inputInventory_,
                                   Inventory outputInventory_,
                                   Input_List inputList,
                                   List<ItemData> itemsToAdd)
    {
        // ���ÿ����۵���Ʒ�Ƿ�����Ҫ��
        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = inputList.RowItems_List[i];

            // ����ò�۲���Ҫ��Ʒ������
            if (required.amount == 0) continue;

            // �����Ʒ����������ƥ��
            if (slot._ItemData == null ||
                slot._ItemData.IDName != required.ItemName)
                return false;

            // ��������㹻
            if (slot._ItemData.Stack.Amount < required.amount)
                return false;
        }

        // �������ռ�
        foreach (var item in itemsToAdd)
            if (!outputInventory_.Data.CanAddTheItem(item))
                return false;

        return true;
    }
    #endregion
}