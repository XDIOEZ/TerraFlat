using DG.Tweening;
using Force.DeepCloner;
using log4net.Util;
using MemoryPack;
using PlasticGui.WorkspaceWindow.Locks;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UltEvents;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;
using ButtonAttribute = Sirenix.OdinInspector.ButtonAttribute;

public class CraftingTable : Item,IWork, IInteract, IInventoryData,ISave_Load,IHealth,IBuilding
{
    #region �ֶ�

    // 1.inventory ��������
    public Inventory inputInventory;
    // 3.inventory �������
    public Inventory outputInventory;
    //�ϳɰ�ť
    public Button button;
    //�رհ�ť
    public Button closeButton;
    //���
    public Canvas canvas;
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

    public Building_InstallAndUninstall _InstallAndUninstall = new ();
    private bool bePlayerTake = false;
    #endregion

    void Start()
    {
       // IInventoryData inventoryData = this;
     //   inventoryData.FillDict_SetBelongItem(transform);

        button.onClick.AddListener(() => Work_Start());
        closeButton.onClick.AddListener(() => CloseUI());

        CloseUI();

        _InstallAndUninstall.Init(transform);

        if(BelongItem != null)
        {
            BePlayerTaken = true;
        }

    }
    public void Update()
    {
        _InstallAndUninstall.Update();
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
        _InstallAndUninstall.Install();
    }

    public void OnDestroy()
    {
        // ��ȫ���� ghost ʵ��
        _InstallAndUninstall.CleanupGhost();
    }
    #endregion

    #region �ϳɷ���
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
            inputInventory.SyncUI( i);
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
            if (!outputInventory_.Data.CanAddTheItem(item))
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
    }

    #endregion

    #region ���罻���ӿ�ʵ��
    //�л�UI��ʾ״̬
    public void SwitchUI()
    {
        canvas.enabled = !canvas.enabled;
    }
    //����Ui
    public void OpenUI()
    {
        canvas.enabled = true;
    }
    //�ر�Ui
    public void CloseUI()
    {
        canvas.enabled = false;
    }
    public void Work_Start()
    {
        Craft(inputInventory, outputInventory, RecipeType.Crafting);
    }

    public void Interact_Start(IInteracter interacter = null)
    {
        if (CanInteract)
        {
            SwitchUI();
            //���������Ʒ�����е�Value��Children_Inventory_GameObject
            foreach (var inventory in Children_Inventory_GameObject.Values)
            {
                inventory.DefaultTarget_Inventory 
                    = interacter.InventoryData.Children_Inventory_GameObject["�ֲ����"];
            }
        }
    }

    public override void Act()
    {
        Debug.Log("Act");
        Install();
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        CloseUI();
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        throw new System.NotImplementedException();
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
        
        //IInventoryData inventoryData = this;
        //inventoryData.FillDict_SetBelongItem(transform);

        inputInventory = Children_Inventory_GameObject["������Ʒ��"];
        outputInventory = Children_Inventory_GameObject["�����Ʒ��"];

        if (InventoryData_Dict.ContainsKey("������Ʒ��"))
        {
            inputInventory.Data = InventoryData_Dict["������Ʒ��"];
            outputInventory.Data = InventoryData_Dict["�����Ʒ��"];
        }
    }

    public void Death()
    {
        if (CanInteract)
            UnInstall();
    }
    public float GetDamage(Damage damage)
    {
        if (Hp.Check_Weakness(damage.DamageType))
        {
            float Value = damage.Return_EndDamage();

            Hp.Value -= Value;

            if (Hp.Value <= 0)
            {
                Death();
            }
            return Value;
        }
        else
        {
            float damageValue = damage.Return_EndDamage(Defense);

            Hp.Value -= damageValue;

            if (Hp.Value <= 0)
            {
                Death();
            }
            return damageValue;
        }
    }
    #endregion
    #endregion

}

