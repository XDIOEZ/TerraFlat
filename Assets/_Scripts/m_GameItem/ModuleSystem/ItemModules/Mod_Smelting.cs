using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class Mod_Smelting : Module
{
    [MemoryPackable]
    [System.Serializable]
    public partial class ModSmeltingData
    {
        [ShowInInspector]
        public Dictionary<string, Inventory_Data> InvData = new Dictionary<string, Inventory_Data>();
        //当前的熔炼进度
        public float SmeltingProgress = 10f;
        //熔炉的最大熔炼速度
        public float MaxSmeltingSpeed = 10f;
        //熔炉的当前熔炼速度
        public float SmeltingSpeed = 10f;
        //当前熔炉内温度
        public float Temperature = 20f;
        //可达最大温度值
        public float MaxTemperature = 0f;
        //IsSmelting 是否正在熔炼
        public bool IsSmelting = false;
        //温度上升速度
        public float TemperatureUpSpeed = 10f;
        //温度下降速度
        public float TemperatureDownSpeed = 30f;
    }

    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; } set { SaveData = (Ex_ModData_MemoryPackable)value; } }
    public ModSmeltingData Data;

    //临时引用
    public Inventory inputInventory;
    public Inventory outputInventory;
    public Inventory fuelInventory;
    public Mod_Fuel mod_Fuel;//燃料模块
    public BasePanel panel;
    public Button WorkButton;
    [Tooltip("熔炼进度条")]
    public Slider progressSlider;
    [Tooltip("燃料容量条")]
    public Slider fuelSlider;
    [Tooltip("温度显示条")]
    public Slider temperatureSlider;



    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Smelting;
        }
    }
    public override void ModUpdate(float deltaTime)
    {
        if (Data.IsSmelting) // 已经处于熔炼状态
        {
            // 如果燃料足够
            if (mod_Fuel.HasFuel())
            {
                SmeltingProcess(deltaTime);
            }
            else
            {
                // 检查燃料插槽是否还有燃料物品
                var fuelItem = fuelInventory.Data.GetModuleByID(ModText.Fuel);
                if (fuelItem != null)
                {
                    // 从物品转化为燃料值
                    ItemSlot slot = fuelInventory.Data.GetItemSlotByModuleID(fuelItem.ID);
                    slot.itemData.Stack.Amount -= 1; // 扣 1 个燃料物品
                    slot.RefreshUI();

                    Ex_ModData_MemoryPackable fuelData = fuelItem as Ex_ModData_MemoryPackable;
                    fuelData.OutData(out FuelData fuel);

                    mod_Fuel.AddFuel(fuel.Fuel.x);

                    // 温度上限取决于燃料
                    Data.MaxTemperature = fuel.MaxTemperature;

                    SmeltingProcess(deltaTime); // 继续熔炼
                }
                else
                {
                    // 真正燃料耗尽 → 停止熔炼
                    Data.IsSmelting = false;
                    Debug.Log("燃料耗尽，熔炼停止！");
                }
            }
        }
        else
        {
            // 未启动或已停止 → 温度缓慢下降到 20℃
            Data.Temperature = Mathf.Max(Data.Temperature - Data.TemperatureDownSpeed * deltaTime, 20f);
            Data.SmeltingSpeed = 0f;
        }

        // 同步所有UI
        UpdateUI();
    }

    private void UpdateUI()
    {
        // 熔炼进度条
        if (progressSlider != null)
            progressSlider.value = Data.SmeltingProgress / 100f;

        // 燃料条
        if (fuelSlider != null)
            fuelSlider.value = mod_Fuel.Data.Fuel.x / mod_Fuel.Data.Fuel.y;

        // 温度条
        if (temperatureSlider != null)
            temperatureSlider.value = Data.Temperature / Data.MaxTemperature;
    }



    private void SmeltingProcess(float deltaTime)
    {
        // 检查输入槽是否有物品
        bool hasInputItem = false;
        foreach (var slot in inputInventory.Data.itemSlots)
        {
            if (slot.itemData != null) // 假设 HasItem 是你判断该槽有物品的方法
            {
                hasInputItem = true;
                break;
            }
        }

        float maxTemp = Data.MaxTemperature > 0 ? Data.MaxTemperature : 1000f;

        // 如果没有物品 → 进度归零（表示干烧）
        if (!hasInputItem)
        {
            Data.SmeltingProgress = 0f;
            // 温度仍然会上升到燃料允许的上限
            Data.Temperature = Mathf.Min(Data.Temperature + Data.TemperatureUpSpeed * 2f * deltaTime, maxTemp);
            // 继续消耗燃料（可选，如果干烧不耗燃料就删掉这一行）
            mod_Fuel.ConsumeFuel(deltaTime);
            return; // 不进入熔炼逻辑
        }

        // ===== 以下是正常熔炼逻辑 =====

        // 温度随时间上升
        Data.Temperature = Mathf.Min(Data.Temperature + Data.TemperatureUpSpeed * deltaTime, maxTemp);

        // 根据温度计算当前熔炼速度
        float tempRatio = Data.Temperature / maxTemp;
        Data.SmeltingSpeed = Mathf.Lerp(1f, Data.MaxSmeltingSpeed, tempRatio);

        // 按当前速度推进进度
        Data.SmeltingProgress += Data.SmeltingSpeed * deltaTime;

        // 消耗燃料
        mod_Fuel.ConsumeFuel(deltaTime);

        // 熔炼完成
        if (Data.SmeltingProgress >= 100f)
        {
            Data.SmeltingProgress = 0f;
            CompleteSmelting();
        }
    }




    private void OnButtonClick()
    {
        // 检查是否有点火装置
        var ignitionItem = fuelInventory.Data.FindItemByTag_First("Ignition");
        if (ignitionItem == null)
        {
            Debug.LogWarning("无法点燃：缺少点火装置！");
            return;
        }

        Data.IsSmelting = true;
        Debug.Log("熔炉已点燃并开始熔炼！");
    }

    public void CompleteSmelting()
    {
        // 生成配方键
        string recipeKey = string.Join(",",
            inputInventory.Data.itemSlots.Select(slot => slot.itemData?.IDName ?? ""));

        // 找到配方
        if (!GameRes.Instance.recipeDict.TryGetValue(recipeKey, out Recipe recipe))
        {
            Debug.LogError($"熔炼失败：找不到配方 {recipeKey}");
            return;
        }

        CookRecipe cookRecipe = recipe as CookRecipe;
        if (cookRecipe == null)
        {
            Debug.LogError($"配方类型错误：{recipeKey} 不是 CookRecipe");
            return;
        }

        // 温度检查
        if (cookRecipe.Temperature > Data.Temperature)
        {
            Debug.LogWarning($"熔炼失败：所需温度 {cookRecipe.Temperature} 当前温度 {Data.Temperature} → 材料有损失！");

            // 惩罚：随机扣除 1~2 个输入材料
            System.Random rand = new System.Random();
            foreach (var slot in inputInventory.Data.itemSlots)
            {
                if (slot.itemData != null && slot.itemData.Stack.Amount > 0)
                {
                    // 扣除数量 = 1 或 2，但不超过当前数量
                    float lossAmount = rand.Next(1, 3); // 1~2
                    lossAmount = (Math.Min(lossAmount, slot.itemData.Stack.Amount)); // 不超过现有数量

                    slot.itemData.Stack.Amount -= lossAmount;
                    Debug.LogWarning($"惩罚扣除：{slot.itemData.IDName} x{lossAmount}");

                    if (slot.itemData.Stack.Amount <= 0)
                    {
                        // 清空物品
                        inputInventory.Data.RemoveItemAll(slot, inputInventory.Data.itemSlots.IndexOf(slot));
                    }
                }
            }

            // 刷新 UI
            inputInventory.RefreshUI();
            return;
        }
        // 准备输出物品
        var itemsToAdd = new List<ItemData>();
        foreach (var output in cookRecipe.outputs.results)
        {
            if (!GameRes.Instance.AllPrefabs.TryGetValue(output.ItemName, out var prefab) || prefab == null)
            {
                Debug.LogError($"预制体不存在：{output.ItemName}（配方：{recipe.name}）");
                return;
            }

            Item item = prefab.GetComponent<Item>();
            item.IsPrefabInit();

            ItemData newItem = item.itemData.DeepClone();
            newItem.Stack.Amount = output.amount;
            itemsToAdd.Add(newItem);
        }

        // 检查材料是否足够 & 输出空间
        if (!CheckEnough(inputInventory, outputInventory, cookRecipe.inputs, itemsToAdd))
        {
            Debug.LogError("熔炼失败：材料不足或输出空间不足");
            return;
        }

        // 添加产物
        foreach (var item in itemsToAdd)
        {
            outputInventory.Data.TryAddItem(item);
            Debug.Log($"熔炼产出：{item.Stack.Amount}x{item.IDName}");
        }

        // 扣除输入材料
        for (int i = 0; i < inputInventory.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory.Data.itemSlots[i];
            var required = cookRecipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            Debug.Log($"扣除材料：{required.ItemName} x{required.amount}");

            slot.itemData.Stack.Amount -= required.amount;
            if (slot.itemData.Stack.Amount <= 0)
            {
                inputInventory.Data.RemoveItemAll(slot, i);
            }
            inputInventory.RefreshUI(i);
        }

        Debug.Log($"熔炼完成：{recipe.name}");
        outputInventory.RefreshUI();
    }

    private bool CheckEnough(Inventory inputInventory_,
                               Inventory outputInventory_,
                               Input_List inputList,
                               List<ItemData> itemsToAdd)
    {
        // 检查每个插槽的物品是否满足要求
        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = inputList.RowItems_List[i];

            // 如果该插槽不需要物品则跳过
            if (required.amount == 0) continue;

            // 检查物品存在且名称匹配
            if (slot.itemData == null ||
                slot.itemData.IDName != required.ItemName)
                return false;

            // 检查数量足够
            if (slot.itemData.Stack.Amount < required.amount)
                return false;
        }

        // 检查输出空间
        foreach (var item in itemsToAdd)
            if (!outputInventory_.Data.TryAddItem(item, false))
                return false;

        return true;
    }

    public override void Load()
    {
        // 从 SaveData 读取
        SaveData.ReadData(ref Data);
        // 同步数据
        if (Data.InvData.Count == 0)
        {
            Data.InvData[inputInventory.Data.Name] = inputInventory.Data;
            Data.InvData[outputInventory.Data.Name] = outputInventory.Data;
            Data.InvData[fuelInventory.Data.Name] = fuelInventory.Data;
        }
        else
        {
            inputInventory.Data = Data.InvData[inputInventory.Data.Name];
            outputInventory.Data = Data.InvData[outputInventory.Data.Name];
            fuelInventory.Data = Data.InvData[fuelInventory.Data.Name];
        }

        // 按钮事件
        WorkButton.onClick.AddListener(OnButtonClick);

        // 如果有手持模块，设置默认目标
        if (item.itemMods.ContainsKey_ID(ModText.Hand))
        {
            var handInv = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;
            inputInventory.DefaultTarget_Inventory = handInv;
            outputInventory.DefaultTarget_Inventory = handInv;
        }

        // 初始化库存
        inputInventory.Init();
        outputInventory.Init();
        fuelInventory.Init();

        // 获取交互模块引用
        var interactMod = item.itemMods.GetMod_ByID(ModText.Interact);
        interactMod.OnAction_Start += Interact_Start;
        interactMod.OnAction_Start += _ => panel.Toggle();
        interactMod.OnAction_Cancel += _ => panel.Close();

        panel.Close();
    }

    public void Interact_Start(Item item_)
    {
        var handInventory = item_.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;

        inputInventory.DefaultTarget_Inventory = handInventory;
        outputInventory.DefaultTarget_Inventory = handInventory;
        fuelInventory.DefaultTarget_Inventory = handInventory;
    }


    public override void Save()
    {
        Data.InvData[inputInventory.Data.Name] = inputInventory.Data;
        Data.InvData[outputInventory.Data.Name] = outputInventory.Data;
        Data.InvData[fuelInventory.Data.Name] = fuelInventory.Data;
        SaveData.WriteData(Data);
    }
}
