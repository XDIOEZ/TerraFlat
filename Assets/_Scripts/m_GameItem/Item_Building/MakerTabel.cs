using Force.DeepCloner;
using System.Collections.Generic;
using System.Linq;
using UltEvents;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;
using ButtonAttribute = Sirenix.OdinInspector.ButtonAttribute;

public class CraftingTable : Item,ISave_Load,IBuilding
{
    #region �ֶ�
    //�ϳɰ�ť
    public Button button;
    //�رհ�ť
    public Button closeButton;

    public BasePanel panel;
    // 4.workerData ��������
    public Data_Worker Data;

    public UltEvent _onInventoryData_Dict_Changed;
    public UltEvent OnInventoryData_Dict_Changed { get => _onInventoryData_Dict_Changed; set => _onInventoryData_Dict_Changed = value; }
    public override ItemData Item_Data
    {
        get => Data;
        set => Data = (Data_Worker)value;
    }
    public Dictionary<string, Inventory_Data> InventoryData_Dict
    {
        get
        {
            return Data.Inventory_Data_Dict;
        }
        set
        {
            Data.Inventory_Data_Dict = value;
        }
    }
    public UltEvent OnDeath { get; set; }
    [ShowInInspector]
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

    public SelectSlot SelectSlot { get; set; }
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }


    public bool CanInteract { get => !Data.Stack.CanBePickedUp; set => Data.Stack.CanBePickedUp = !value; }
    public Hp Hp { get=> Data.Hp; set => Data.Hp = value; }
    public Defense Defense { get => Data.Defense; set => Data.Defense = value; }
    public UltEvent OnHpChanged { get; set; }
    public UltEvent OnDefenseChanged { get; set; }
    public bool IsInstalled { get =>Data.IsInstalled; set => Data.IsInstalled = value; }
    public bool BePlayerTaken { get => bePlayerTake; set => bePlayerTake = value; }

    public BaseBuilding _InstallAndUninstall;
    private bool bePlayerTake = false;
    #endregion

    new void  Start()
    {
        base.Start();


        panel = GetComponentInChildren<BasePanel>();

        Mods["����ģ��"].OnAction_Start += Interact_Start;
        Mods["����ģ��"].OnAction_Start += (Item) => { panel.Toggle(); };
        Mods["����ģ��"].OnAction_Cancel += (Item) => { panel.Close(); };

       /* Mods[ModText.Hp]*/

        if (BelongItem != null)
        {
            BePlayerTaken = true;
        }
    }
    #region ��װ�Ͳ��

    [Sirenix.OdinInspector.Button]
    public void UnInstall()
    {
        _InstallAndUninstall.UnInstall();
    }
    [Button]
    public void Install()
    {
        Mods["����ģ��"].OnAction.Invoke(1);
    }

    public void OnDestroy()
    {
        // ��ȫ���� ghost ʵ��
       // _InstallAndUninstall.CleanupGhost();
    }
    #endregion

    #region �ϳɷ���
/*    public bool Craft(Inventory inputInventory_, Inventory outputInventory_, RecipeType recipeType)
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
            outputInventory_.Data.TryAddItem(item);
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
            inputInventory.RefreshUI( i);
        }

        Debug.Log($"�ϳ���ɣ�{recipe.name}");
        return true;
    }
    private bool CheckEnough(Inventory inputInventory_,
                         Inventory outputInventory_,
                         Input_List inputList,
                         List<ItemData> itemsToAdd)
    {
        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = inputList.RowItems_List[i];

            // ��۲���Ҫ��Ʒ������
            if (required.amount == 0)
            {
                Debug.Log($"��λ{i} ����Ҫ���ϣ�����");
                continue;
            }

            // �����Ʒ�Ƿ����
            if (slot._ItemData == null)
            {
                Debug.LogWarning($"��λ{i} ��Ҫ {required.ItemName} x{required.amount}������ƷΪ��");
                return false;
            }

            // ��������Ƿ�ƥ��
            if (slot._ItemData.IDName != required.ItemName)
            {
                Debug.LogWarning($"��λ{i} ��Ʒ��ƥ�䣬���� {required.ItemName}��ʵ���� {slot._ItemData.IDName}");
                return false;
            }

            // ��������Ƿ��㹻
            if (slot._ItemData.Stack.Amount < required.amount)
            {
                Debug.LogWarning($"��λ{i} �����������㣬��ǰ {slot._ItemData.Stack.Amount}����Ҫ {required.amount}");
                return false;
            }
            else
            {
                Debug.Log($"��λ{i} ���ϼ��ͨ����{slot._ItemData.IDName} x{slot._ItemData.Stack.Amount}/{required.amount}");
            }
        }

        // �������ռ�
        foreach (var item in itemsToAdd)
        {
            if (!outputInventory_.Data.TryAddItem(item,false))
            {
                Debug.LogWarning($"�����Ʒ {item.IDName} �޷�����������������ܿռ䲻��");
                return false;
            }
            else
            {
                Debug.Log($"������ͨ����������� {item.IDName} x{item.Stack.Amount}");
            }
        }

        Debug.Log("��������������ͨ�������Խ��кϳ�");
        return true;
    }*/

    #endregion

    #region ���罻���ӿ�ʵ��
/*
    public void Work_Start()
    {
        Craft(inputInventory, outputInventory, RecipeType.Crafting);
    }
*/
    public void Interact_Start(Item item)
    {
        if (CanInteract)
        {
            Mods["����̨�ϳ�ģ��"].GetComponent<Mod_HandMade>().inputInventory.DefaultTarget_Inventory
                = item.Mods[ModText.Hand].GetComponent<IInventory>()._Inventory;
            Mods["����̨�ϳ�ģ��"].GetComponent<Mod_HandMade>().outputInventory.DefaultTarget_Inventory
                = item.Mods[ModText.Hand].GetComponent<IInventory>()._Inventory;
        }
    }

    public override void Act()
    {
        Debug.Log("Act");
        OnAct.Invoke();
    }

    public void Work_Update()
    {
        throw new System.NotImplementedException();
    }

    public void Work_Stop()
    {
        throw new System.NotImplementedException();
    }

    #region ����ͼ���
    public void Save()
    {
        //TODO ��������
    }

    public void Load()
    {
        /*inputInventory = Children_Inventory_GameObject["������Ʒ��"];
        outputInventory = Children_Inventory_GameObject["�����Ʒ��"];

        if (InventoryData_Dict.ContainsKey("������Ʒ��"))
        {
            inputInventory.Data = InventoryData_Dict["������Ʒ��"];
            outputInventory.Data = InventoryData_Dict["�����Ʒ��"];
        }*/
    }
    #endregion
    #endregion

}

