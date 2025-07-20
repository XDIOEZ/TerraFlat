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
    #region 属性
    [Tooltip("熔炉数据")]
    public Data_Worker _furnaceData;

    [Tooltip("是否正在工作")]
    public bool isWorking;

    [Tooltip("是否可以工作")]
    public bool canWork;

    [Tooltip("最大燃料量(单位:秒)")]
    public float maxFuelAmount = 128;

    [Tooltip("当前燃料量")]
    public float currentFuelAmount = 0;

    [Tooltip("工作速度(单位:秒)")]
    public float workSpeed = 1;

    [Tooltip("当前工作完成度(单位:秒)")]
    public float workProgress = 0;

    [Tooltip("最大工作完成度(单位:秒)")]
    public float maxWorkProgress = 8;

    [Tooltip("燃料消耗率(单位/秒)")]
    public float fuelConsumeRate = 1;

    // 输入槽
    [Tooltip("输入槽")]
    public Inventory Input_inventory;
    // 输出槽
    [Tooltip("输出槽")]
    public Inventory Output_inventory;
    // 燃料槽
    [Tooltip("燃料槽")]
    public Inventory Fuel_inventory;
    [Tooltip("工作按钮")]
    public Button workButton;
    [Tooltip("物品面板")]
    public Canvas itemCanvas;
    [Tooltip("关闭按钮")]
    public Button closeButton;
    [Tooltip("燃料量显示")]
    public Slider fuelSlider;
    [Tooltip("工作进度显示")]
    public Slider workSlider;

    public SelectSlot SelectSlot { get; set; }


    public UltEvent _onInventoryData_Dict_Changed;
    public UltEvent OnInventoryData_Dict_Changed { get => _onInventoryData_Dict_Changed; set => _onInventoryData_Dict_Changed = value; }


    [Tooltip("工作进度显示")]
    public Recipe currentWorkingRecipe;

    // 新增：记录工作开始时间和协程引用
    private float workStartTime;
    private Coroutine workCoroutine;
    #endregion

    //实例化时初始化Inventory

    new void Start()
    {
        base.Start();
        workButton.onClick.AddListener(Work_Start);
        closeButton.onClick.AddListener(CloseUI);
        fuelSlider.maxValue = maxFuelAmount;
        workSlider.maxValue = maxWorkProgress;
        OnInventoryData_Dict_Changed += SetChildInventoryData;

        InitializeInventory("输入槽", ref Input_inventory);
        InitializeInventory("输出槽", ref Output_inventory);
        InitializeInventory("燃料槽", ref Fuel_inventory);

        CloseUI();
    }

    private void InitializeInventory(string name, ref Inventory inventory)
    {
        //如果已经存在了就不再初始化
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
        Debug.Log("设置子物品的InventoryData");
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
            Debug.LogWarning("设置熔炉数据");
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
    #region 合成方法

    private static bool CheckEnough(Inventory inputInventory_, Inventory outputInventory_, List<CraftingIngredient> currentRecipeList, Input_List input_list, List<ItemData> itemsToAdd)
    {
        #region 检查测原材料是否足够
        // 遍历当前合成清单，检查是否有足够的原材料
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

                        Debug.LogError($"合成失败:输出插槽堵塞或者缺少原材料{item_slot._ItemData.Stack.Amount}+{input_list.RowItems_List[i].amount}");
                        return false;
                    }
                }
            }

            return true;

        }
        // 尝试将所有待添加的物品添加到输出容器
        foreach (var item in itemsToAdd)
        {
            /*if (!outputInventory_.CanAddTheItem(item))
            {
                // 如果添加失败，直接返回
                return false;
            }*/
        }
        return true;//成功通过检测
        #endregion
    }

    // 实际合成方法实现
    public static bool Craft(Inventory inputInventory_, Inventory outputInventory_, RecipeType RecipeType)
    {
        #region 获取当前合成清单

        List<CraftingIngredient> currentRecipeList = new List<CraftingIngredient>();

        foreach (var item_slot in inputInventory_.Data.itemSlots)
        {
            if (item_slot._ItemData == null)
            {
                // 如果插槽为空，添加一个空的 CraftingIngredient 对象到当前合成清单
                currentRecipeList.Add(new CraftingIngredient(""));
                continue;
            }
            // 将该物品及其当前数量添加到当前合成清单
            currentRecipeList.Add(new CraftingIngredient(item_slot._ItemData.IDName));
        }

        #endregion

        string recipeKey = Recipe.ToStringList(currentRecipeList);
        Output_List output_list;
        Input_List input_list;
        List<ItemData> itemsToAdd = new List<ItemData>();

        if (GameRes.Instance.recipeDict.ContainsKey(recipeKey) && (GameRes.Instance.recipeDict[recipeKey].recipeType == RecipeType))
        {
            // 获取匹配配方的输出列表
            output_list = GameRes.Instance.recipeDict[recipeKey].outputs;
            input_list = GameRes.Instance.recipeDict[recipeKey].inputs;
            #region 获取输出物品的ItemData
            // 遍历输出列表中的每个输出项
            foreach (var output in output_list.results)
            {
                // 同步加载输出物品的预制体
                GameObject prefab = GameRes.Instance.AllPrefabs[output.resultItem];
                if (prefab != null)
                {
                    // 克隆预制体的物品数据
                    ItemData output_item = prefab.GetComponent<Item>().DeepClone().Item_Data;
                    // 设置输出物品的数量
                    output_item.Stack.Amount = output.resultAmount;
                    // 将输出物品添加到待添加列表
                    itemsToAdd.Add(output_item);
                }
                else
                {
                    // 如果未能找到预制体，输出错误信息
                    Debug.LogError($"未能找到预制体：{output.resultItem}");
                    return false;
                }
            }
            #endregion
            if (!CheckEnough(inputInventory_, outputInventory_, currentRecipeList, input_list, itemsToAdd))
            {
                Debug.LogError("合成失败:输出插槽堵塞或者缺少原材料");
                return false;
            }

            #region 合成
            foreach (var item in itemsToAdd)
            {
                outputInventory_.Data.TryAddItem(item);
            }
            // 根据合成清单，删除输入插槽内的物品

            // 找到对应的 output_item
            if (input_list != null)
            {
                for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
                {
                    var item_slot = inputInventory_.Data.itemSlots[i];

                    if (item_slot._ItemData != null && item_slot._ItemData.IDName == currentRecipeList[i].ItemName)
                    {
                        if (item_slot._ItemData.Stack.Amount >= input_list.RowItems_List[i].amount)
                        {
                            // 如果物品数量减为 0，从输入容器中移除该物品
                            item_slot._ItemData.Stack.Amount -= input_list.RowItems_List[i].amount;

                            if (item_slot._ItemData.Stack.Amount == 0)
                            {
                                inputInventory_.Data.RemoveItemAll(item_slot, i);
                            }
                            // 刷新 UI
                            //item_slot.UI.RefreshUI();
                        }
                    }
                }
            }

            return true;//成功合成
            #endregion
        }
        Debug.LogError("配方不存在或不匹配");
        return false;

    }

    #endregion

    #region 工作方块逻辑
    [Button("开始工作")]
    public void Work_Start()
    {
        //当前有携程就先停止
        if (workCoroutine != null)
        {
            StopCoroutine(workCoroutine);
            workCoroutine = null;
        }
        // 检测是否可以工作
        if (!Work_Check())
        {
            Debug.LogWarning("工作条件不满足，无法开始工作！");
            return;
        }
        // 记录当前工作的开始时间
        workStartTime = Time.time;
        // 重置工作进度（如果需要）
        workProgress = 0;
        // 启动 Work_Coroutine 协程，并保存引用
        workCoroutine = StartCoroutine(Work_Coroutine());
        // 更新状态标记
        isWorking = true;
    }

    // 每0.02秒执行一次的协程函数
    public IEnumerator Work_Coroutine()
    {
        while (true)
        {
            Work_Update();
            yield return new WaitForSeconds(0.02f);
        }
    }
    /// <summary>
    /// 检查熔炉是否可以开始工作
    /// </summary>
    /// <returns>是否可以工作</returns>
    public bool Work_Check()
    {
        Debug.Log("开始检查熔炉是否可以工作...");
        // 2. 检测输入槽是否为空
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
            Debug.Log("熔炉无法工作，输入槽为空。");
            return false;
        }

        // 1. 检测是否有足够的燃料
        if (currentFuelAmount <= 0)
        {
            if (!AddFuel())
            {
                Debug.Log("熔炉无法工作，没有足够的燃料。");
                return false;
            }
        }

        return NewMethod();

        bool NewMethod()
        {
            // 3. 检测是否有匹配的配方
            List<CraftingIngredient> currentRecipeList = new List<CraftingIngredient>();
            for (int i = 0; i < Input_inventory.Data.itemSlots.Count; i++)
            {
                var item_slot = Input_inventory.Data.itemSlots[i];

                if (item_slot._ItemData == null)
                {
                    // 如果插槽为空，添加一个空的 CraftingIngredient 对象到当前合成清单
                    currentRecipeList.Add(new CraftingIngredient(""));
                    continue;
                }
                // 将该物品及其当前数量添加到当前合成清单
                currentRecipeList.Add(new CraftingIngredient(item_slot._ItemData.IDName));
            }
            
            Recipe TempRecipe;

            // 获取键
            var key = currentRecipeList[0].ToStringList(currentRecipeList);

            if (key == null)
            {
                Debug.LogError("ToStringList 返回了 null 键！");
                return false;
            }

            // 安全访问字典
            if (GameRes.Instance.recipeDict.TryGetValue(key, out Recipe tempRecipe))
            {
                TempRecipe = tempRecipe;
            }
            else
            {
             //  Debug.LogError($"字典中不存在键：{key}");
                // 处理键不存在的情况（例如设置默认值）
                TempRecipe = new Recipe(); // 或其他默认值
            }
            if (TempRecipe == null)
            {
                Debug.Log("熔炉无法工作，没有匹配的配方。");
                return false;
            }

            if (TempRecipe.recipeType != RecipeType.Smelting)
            {
                Debug.Log("熔炉无法工作，配方类型不匹配。");
                return false;
            }

            // 4. 标记为可以工作
            currentWorkingRecipe = GameRes.Instance.recipeDict[currentRecipeList[0].ToStringList(currentRecipeList)];
            Debug.Log("熔炉可以工作，已找到匹配配方。");
            return true;
        }
    }

    public void Work_Update()
    {
        // 更新燃料量滑块的显示
        fuelSlider.value = currentFuelAmount;

        // 更新工作进度滑块的显示
        workSlider.value = workProgress;
        // 检测是否可以工作，若不满足条件则停止工作
        if (!Work_Check())
        {
            Work_Stop();
            return;
        }
        //     2              2          0
        workProgress = (Time.time - workStartTime) * workSpeed;
       // Debug.Log("当前工作进度：" + workProgress);
        // 根据工作进度和燃料消耗率减少当前燃料量
        //       1                 2               1
        currentFuelAmount -= 0.02f* fuelConsumeRate;
       // Debug.Log("当前燃料量：" + currentFuelAmount);

        if (currentFuelAmount < 0)
            currentFuelAmount = 0;

        // 如果工作完成度达到或超过最大值，则完成工作
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
        // 停止协程的执行
        if (workCoroutine != null)
        {
            StopCoroutine(workCoroutine);
            workCoroutine = null;
        }
        // 更新工作状态标记
        isWorking = false;
        Debug.Log("工作已停止");
    }

    public  void Work_End(Inventory inputInventory, Inventory outputInventory)
    {
        Craft(inputInventory, outputInventory, RecipeType.Smelting);
        //TODO 进度条归零
        workProgress = 0;
        workSlider.value = 0;
        // 重置当前工作的配方
        currentWorkingRecipe = null;
        Work_Start();
    }
    #endregion

    #region 方块级交互逻辑

    public void Interact_Start(IInteracter interacter = null)
    {
        SwitchUI();
        //遍历这个物品的所有的Value，Children_Inventory_GameObject
        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            IInventoryData data = (IInventoryData)interacter.Item;
            inventory.DefaultTarget_Inventory = data.Children_Inventory_GameObject["手部插槽"];
        }
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        // 可在此更新交互状态，比如更新交互界面（视需求实现）
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        CloseUI();
        //遍历这个物品的所有的Value，Children_Inventory_GameObject
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
    //关闭Ui
    public void CloseUI()
    {
        itemCanvas.enabled = false;
    }
    #endregion

    public override void Act()
    {
        // 使用物品时，若未工作则启动，否则停止工作
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
