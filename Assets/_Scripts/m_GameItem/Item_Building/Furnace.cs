using Force.DeepCloner;
using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.UI;

public class Furnace : Item, IWork, IInteract,IInventoryData
{
    #region ����
    [Tooltip("��¯����")]
    public Data_Worker _furnaceData;

    [Tooltip("�Ƿ����ڹ���")]
    public bool isWorking;

    [Tooltip("�Ƿ���Թ���")]
    public bool canWork;

    [Tooltip("���ȼ����(��λ:��)")]
    public float maxFuelAmount = 128;

    [Tooltip("��ǰȼ����")]
    public float currentFuelAmount = 0;

    [Tooltip("�����ٶ�(��λ:��)")]
    public float workSpeed = 1;

    [Tooltip("��ǰ������ɶ�(��λ:��)")]
    public float workProgress = 0;

    [Tooltip("�������ɶ�(��λ:��)")]
    public float maxWorkProgress = 8;

    [Tooltip("ȼ��������(��λ/��)")]
    public float fuelConsumeRate = 1;

    // �����
    [Tooltip("�����")]
    public Inventory Input_inventory;
    // �����
    [Tooltip("�����")]
    public Inventory Output_inventory;
    // ȼ�ϲ�
    [Tooltip("ȼ�ϲ�")]
    public Inventory Fuel_inventory;
    [Tooltip("������ť")]
    public Button workButton;
    [Tooltip("��Ʒ���")]
    public Canvas itemCanvas;
    [Tooltip("�رհ�ť")]
    public Button closeButton;
    [Tooltip("ȼ������ʾ")]
    public Slider fuelSlider;
    [Tooltip("����������ʾ")]
    public Slider workSlider;

    public SelectSlot SelectSlot { get; set; }


    public UltEvent _onInventoryData_Dict_Changed;
    public UltEvent OnInventoryData_Dict_Changed { get => _onInventoryData_Dict_Changed; set => _onInventoryData_Dict_Changed = value; }


    [Tooltip("����������ʾ")]
    public Recipe currentWorkingRecipe;

    // ��������¼������ʼʱ���Э������
    private float workStartTime;
    private Coroutine workCoroutine;
    #endregion

    //ʵ����ʱ��ʼ��Inventory

    new void Start()
    {
        base.Start();
        workButton.onClick.AddListener(Work_Start);
        closeButton.onClick.AddListener(CloseUI);
        fuelSlider.maxValue = maxFuelAmount;
        workSlider.maxValue = maxWorkProgress;
        OnInventoryData_Dict_Changed += SetChildInventoryData;

        InitializeInventory("�����", ref Input_inventory);
        InitializeInventory("�����", ref Output_inventory);
        InitializeInventory("ȼ�ϲ�", ref Fuel_inventory);

        CloseUI();
    }

    private void InitializeInventory(string name, ref Inventory inventory)
    {
        //����Ѿ������˾Ͳ��ٳ�ʼ��
        if (_furnaceData.Inventory_Data_Dict.ContainsKey(name))
        {
            return;
        }
       // _furnaceData.Inventory_Data_Dict.Add(name, new Inventory_Data());
        inventory.Data = _furnaceData.Inventory_Data_Dict[name];
        inventory.Data.itemSlots = new List<ItemSlot> 
        {
            new ItemSlot(0), 
            new ItemSlot(1),
            new ItemSlot(2) 
        };
        inventory.Data.Name = name;

        foreach (var itemSlot in inventory.Data.itemSlots)
        {
            itemSlot.Belong_Inventory = inventory;
        }
    }


    public void SetChildInventoryData()
    {
        Debug.Log("��������Ʒ��InventoryData");
        Inventory[] Inventory_s = GetComponentsInChildren<Inventory>();
        foreach (var inventory_ShowNow in Inventory_s)
        {
            inventory_ShowNow.Data = InventoryData_Dict[inventory_ShowNow.Data.Name];
            foreach (var itemSlot in InventoryData_Dict[inventory_ShowNow.Data.Name].itemSlots)
            {
                itemSlot.Belong_Inventory = inventory_ShowNow;
            }
        }
    }


    public override ItemData Item_Data
    {
        get => _furnaceData;
        set
        {
         
         
            _furnaceData = value as Data_Worker;

            OnInventoryData_Dict_Changed.Invoke();
            Debug.LogWarning("������¯����");
        }
    }

    public Dictionary<string, Inventory_Data> InventoryData_Dict
    {
        get
        {
            return _furnaceData.Inventory_Data_Dict;
        }
        set
        {
            _furnaceData.Inventory_Data_Dict = value;
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
    #region �ϳɷ���

    private static bool CheckEnough(Inventory inputInventory_, Inventory outputInventory_, List<CraftingIngredient> currentRecipeList, Input_List input_list, List<ItemData> itemsToAdd)
    {
        #region ����ԭ�����Ƿ��㹻
        // ������ǰ�ϳ��嵥������Ƿ����㹻��ԭ����
        foreach (var ingredient in currentRecipeList)
        {
            for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
            {
                var item_slot = inputInventory_.Data.itemSlots[i];

                if (item_slot._ItemData == null)
                {
                    continue;
                }

                if (item_slot._ItemData.IDName == ingredient.ItemName)
                {
                    if (item_slot._ItemData.Stack.Amount < input_list.RowItems_List[i].amount)
                    {

                        Debug.LogError($"�ϳ�ʧ��:�����۶�������ȱ��ԭ����{item_slot._ItemData.Stack.Amount}+{input_list.RowItems_List[i].amount}");
                        return false;
                    }
                }
            }

            return true;

        }
        // ���Խ����д���ӵ���Ʒ��ӵ��������
        foreach (var item in itemsToAdd)
        {
            /*if (!outputInventory_.CanAddTheItem(item))
            {
                // ������ʧ�ܣ�ֱ�ӷ���
                return false;
            }*/
        }
        return true;//�ɹ�ͨ�����
        #endregion
    }

    // ʵ�ʺϳɷ���ʵ��
    public static bool Craft(Inventory inputInventory_, Inventory outputInventory_, RecipeType RecipeType)
    {
        #region ��ȡ��ǰ�ϳ��嵥

        List<CraftingIngredient> currentRecipeList = new List<CraftingIngredient>();

        foreach (var item_slot in inputInventory_.Data.itemSlots)
        {
            if (item_slot._ItemData == null)
            {
                // ������Ϊ�գ����һ���յ� CraftingIngredient ���󵽵�ǰ�ϳ��嵥
                currentRecipeList.Add(new CraftingIngredient(""));
                continue;
            }
            // ������Ʒ���䵱ǰ������ӵ���ǰ�ϳ��嵥
            currentRecipeList.Add(new CraftingIngredient(item_slot._ItemData.IDName));
        }

        #endregion

        string recipeKey = Recipe.ToStringList(currentRecipeList);
        Output_List output_list;
        Input_List input_list;
        List<ItemData> itemsToAdd = new List<ItemData>();

        if (GameRes.Instance.recipeDict.ContainsKey(recipeKey) && (GameRes.Instance.recipeDict[recipeKey].recipeType == RecipeType))
        {
            // ��ȡƥ���䷽������б�
            output_list = GameRes.Instance.recipeDict[recipeKey].outputs;
            input_list = GameRes.Instance.recipeDict[recipeKey].inputs;
            #region ��ȡ�����Ʒ��ItemData
            // ��������б��е�ÿ�������
            foreach (var output in output_list.results)
            {
                // ͬ�����������Ʒ��Ԥ����
                GameObject prefab = GameRes.Instance.AllPrefabs[output.resultItem];
                if (prefab != null)
                {
                    // ��¡Ԥ�������Ʒ����
                    ItemData output_item = prefab.GetComponent<Item>().DeepClone().Item_Data;
                    // ���������Ʒ������
                    output_item.Stack.Amount = output.resultAmount;
                    // �������Ʒ��ӵ�������б�
                    itemsToAdd.Add(output_item);
                }
                else
                {
                    // ���δ���ҵ�Ԥ���壬���������Ϣ
                    Debug.LogError($"δ���ҵ�Ԥ���壺{output.resultItem}");
                    return false;
                }
            }
            #endregion
            if (!CheckEnough(inputInventory_, outputInventory_, currentRecipeList, input_list, itemsToAdd))
            {
                Debug.LogError("�ϳ�ʧ��:�����۶�������ȱ��ԭ����");
                return false;
            }

            #region �ϳ�
            foreach (var item in itemsToAdd)
            {
                outputInventory_.Data.TryAddItem(item);
            }
            // ���ݺϳ��嵥��ɾ���������ڵ���Ʒ

            // �ҵ���Ӧ�� output_item
            if (input_list != null)
            {
                for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
                {
                    var item_slot = inputInventory_.Data.itemSlots[i];

                    if (item_slot._ItemData != null && item_slot._ItemData.IDName == currentRecipeList[i].ItemName)
                    {
                        if (item_slot._ItemData.Stack.Amount >= input_list.RowItems_List[i].amount)
                        {
                            // �����Ʒ������Ϊ 0���������������Ƴ�����Ʒ
                            item_slot._ItemData.Stack.Amount -= input_list.RowItems_List[i].amount;

                            if (item_slot._ItemData.Stack.Amount == 0)
                            {
                                inputInventory_.Data.RemoveItemAll(item_slot, i);
                            }
                            // ˢ�� UI
                            //item_slot.UI.RefreshUI();
                        }
                    }
                }
            }

            return true;//�ɹ��ϳ�
            #endregion
        }
        Debug.LogError("�䷽�����ڻ�ƥ��");
        return false;

    }

    #endregion

    #region ���������߼�
    [Button("��ʼ����")]
    public void Work_Start()
    {
        //��ǰ��Я�̾���ֹͣ
        if (workCoroutine != null)
        {
            StopCoroutine(workCoroutine);
            workCoroutine = null;
        }
        // ����Ƿ���Թ���
        if (!Work_Check())
        {
            Debug.LogWarning("�������������㣬�޷���ʼ������");
            return;
        }
        // ��¼��ǰ�����Ŀ�ʼʱ��
        workStartTime = Time.time;
        // ���ù������ȣ������Ҫ��
        workProgress = 0;
        // ���� Work_Coroutine Э�̣�����������
        workCoroutine = StartCoroutine(Work_Coroutine());
        // ����״̬���
        isWorking = true;
    }

    // ÿ0.02��ִ��һ�ε�Э�̺���
    public IEnumerator Work_Coroutine()
    {
        while (true)
        {
            Work_Update();
            yield return new WaitForSeconds(0.02f);
        }
    }
    /// <summary>
    /// �����¯�Ƿ���Կ�ʼ����
    /// </summary>
    /// <returns>�Ƿ���Թ���</returns>
    public bool Work_Check()
    {
        Debug.Log("��ʼ�����¯�Ƿ���Թ���...");
        // 2. ���������Ƿ�Ϊ��
        bool isInputEmpty = true;
        foreach (var slot in Input_inventory.Data.itemSlots)
        {
            if (slot._ItemData != null && slot._ItemData.Stack.Amount > 0)
            {
                isInputEmpty = false;
                break;
            }
        }

        if (isInputEmpty)
        {
            Debug.Log("��¯�޷������������Ϊ�ա�");
            return false;
        }

        // 1. ����Ƿ����㹻��ȼ��
        if (currentFuelAmount <= 0)
        {
            if (!AddFuel())
            {
                Debug.Log("��¯�޷�������û���㹻��ȼ�ϡ�");
                return false;
            }
        }

        return NewMethod();

        bool NewMethod()
        {
            // 3. ����Ƿ���ƥ����䷽
            List<CraftingIngredient> currentRecipeList = new List<CraftingIngredient>();
            for (int i = 0; i < Input_inventory.Data.itemSlots.Count; i++)
            {
                var item_slot = Input_inventory.Data.itemSlots[i];

                if (item_slot._ItemData == null)
                {
                    // ������Ϊ�գ����һ���յ� CraftingIngredient ���󵽵�ǰ�ϳ��嵥
                    currentRecipeList.Add(new CraftingIngredient(""));
                    continue;
                }
                // ������Ʒ���䵱ǰ������ӵ���ǰ�ϳ��嵥
                currentRecipeList.Add(new CraftingIngredient(item_slot._ItemData.IDName));
            }
            
            Recipe TempRecipe;

            // ��ȡ��
            var key = currentRecipeList[0].ToStringList(currentRecipeList);

            if (key == null)
            {
                Debug.LogError("ToStringList ������ null ����");
                return false;
            }

            // ��ȫ�����ֵ�
            if (GameRes.Instance.recipeDict.TryGetValue(key, out Recipe tempRecipe))
            {
                TempRecipe = tempRecipe;
            }
            else
            {
             //  Debug.LogError($"�ֵ��в����ڼ���{key}");
                // ����������ڵ��������������Ĭ��ֵ��
                TempRecipe = new Recipe(); // ������Ĭ��ֵ
            }
            if (TempRecipe == null)
            {
                Debug.Log("��¯�޷�������û��ƥ����䷽��");
                return false;
            }

            if (TempRecipe.recipeType != RecipeType.Smelting)
            {
                Debug.Log("��¯�޷��������䷽���Ͳ�ƥ�䡣");
                return false;
            }

            // 4. ���Ϊ���Թ���
            currentWorkingRecipe = GameRes.Instance.recipeDict[currentRecipeList[0].ToStringList(currentRecipeList)];
            Debug.Log("��¯���Թ��������ҵ�ƥ���䷽��");
            return true;
        }
    }

    public void Work_Update()
    {
        // ����ȼ�����������ʾ
        fuelSlider.value = currentFuelAmount;

        // ���¹������Ȼ������ʾ
        workSlider.value = workProgress;
        // ����Ƿ���Թ�������������������ֹͣ����
        if (!Work_Check())
        {
            Work_Stop();
            return;
        }
        //     2              2          0
        workProgress = (Time.time - workStartTime) * workSpeed;
       // Debug.Log("��ǰ�������ȣ�" + workProgress);
        // ���ݹ������Ⱥ�ȼ�������ʼ��ٵ�ǰȼ����
        //       1                 2               1
        currentFuelAmount -= 0.02f* fuelConsumeRate;
       // Debug.Log("��ǰȼ������" + currentFuelAmount);

        if (currentFuelAmount < 0)
            currentFuelAmount = 0;

        // ���������ɶȴﵽ�򳬹����ֵ������ɹ���
        if (workProgress >= maxWorkProgress)
        {
            Work_Stop();
            Work_End(Input_inventory, Output_inventory);
        }
    }

    public bool AddFuel()
    {
       
        foreach (var slot in Fuel_inventory.Data.itemSlots)
        {
            if (slot._ItemData == null)
            {
                continue;
            }

            string itemName = slot._ItemData.IDName;

            IFuel fuel = GameRes.Instance.AllPrefabs[itemName].GetComponent<IFuel>();

            if (fuel == null)
            {
                continue;
            }

            currentFuelAmount += fuel.MaxBurnTime;
            slot._ItemData.Stack.Amount -= 1;
            //slot.UI.RefreshUI();
            return true;
        }
        return false;
    }

    public void Work_Stop()
    {
        // ֹͣЭ�̵�ִ��
        if (workCoroutine != null)
        {
            StopCoroutine(workCoroutine);
            workCoroutine = null;
        }
        // ���¹���״̬���
        isWorking = false;
        Debug.Log("������ֹͣ");
    }

    public  void Work_End(Inventory inputInventory, Inventory outputInventory)
    {
        Craft(inputInventory, outputInventory, RecipeType.Smelting);
        //TODO ����������
        workProgress = 0;
        workSlider.value = 0;
        // ���õ�ǰ�������䷽
        currentWorkingRecipe = null;
        Work_Start();
    }
    #endregion

    #region ���鼶�����߼�

    public void Interact_Start(IInteracter interacter = null)
    {
        SwitchUI();
        //���������Ʒ�����е�Value��Children_Inventory_GameObject
        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            IInventoryData data = (IInventoryData)interacter.Item;
            inventory.DefaultTarget_Inventory = data.Children_Inventory_GameObject["�ֲ����"];
        }
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        // ���ڴ˸��½���״̬��������½������棨������ʵ�֣�
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        CloseUI();
        //���������Ʒ�����е�Value��Children_Inventory_GameObject
        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            inventory.DefaultTarget_Inventory = null;
        }
    }
    public void SwitchUI()
    {
        itemCanvas.enabled = !itemCanvas.enabled;
    }
    public void OpenUI()
    {
        itemCanvas.enabled = true;
    }
    //�ر�Ui
    public void CloseUI()
    {
        itemCanvas.enabled = false;
    }
    #endregion

    public override void Act()
    {
        // ʹ����Ʒʱ����δ����������������ֹͣ����
        if (!isWorking)
        {
            Work_Start();
        }
        else
        {
            Work_Stop();
        }
    }

}
