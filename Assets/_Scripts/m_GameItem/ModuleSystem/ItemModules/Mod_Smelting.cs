using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class Mod_Smelting : Module
{
    #region 数据定义
    [MemoryPackable]
    [System.Serializable]
    public partial class ModSmeltingData
    {
        [ShowInInspector]
        public Dictionary<string, Inventory_Data> InvData = new Dictionary<string, Inventory_Data>();
        
        [Tooltip("当前的熔炼进度")]
        public float SmeltingProgress = 10f;
        
        [Tooltip("熔炉的最大熔炼速度")]
        public float MaxSmeltingSpeed = 10f;
        
        [Tooltip("熔炉的当前熔炼速度")]
        public float SmeltingSpeed = 10f;
        
        [Tooltip("当前熔炉内温度")]
        public float Temperature = 20f;
        
        [Tooltip("可达最大温度值（由燃料决定）")]
        public float MaxTemperature = 0f;
        
        [Tooltip("熔炉本身的最大温度限制（熔炉的物理限制）")]
        public float MaxTemperatureLimit = 1000f;
        
        [Tooltip("是否正在熔炼")]
        public bool IsSmelting = false;
        
        [Tooltip("温度上升速度")]
        public float TemperatureUpSpeed = 10f;
        
        [Tooltip("温度下降速度")]
        public float TemperatureDownSpeed = 30f;
    }
    #endregion

    #region 序列化字段与引用
    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; } set { SaveData = (Ex_ModData_MemoryPackable)value; } }
    public ModSmeltingData Data = new ModSmeltingData();

    // 临时引用
    public Inventory inputInventory;
    public Inventory outputInventory;
    public Inventory fuelInventory;
    public Mod_Fuel mod_Fuel; // 燃料模块
    public BasePanel panel;
    public Button WorkButton;
    
    [Header("UI组件")]
    [Tooltip("熔炼进度条")]
    public Slider progressSlider;
    [Tooltip("燃料容量条")]
    public Slider fuelSlider;
    [Tooltip("温度显示条")]
    public Slider temperatureSlider;
    [Tooltip("温度数值文本")]
    public TextMeshProUGUI TemperatureText;
    #endregion

    #region Unity生命周期
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
            // 检查燃料模块是否处于点燃状态
            if (mod_Fuel.GetIgnitedState())
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
                    if (slot != null && slot.itemData != null && slot.itemData.Stack.Amount > 0)
                    {
                        slot.itemData.Stack.Amount -= 1; // 扣 1 个燃料物品
                        slot.RefreshUI();

                        Ex_ModData_MemoryPackable fuelData = fuelItem as Ex_ModData_MemoryPackable;
                        if (fuelData != null)
                        {
                            fuelData.OutData(out FuelData fuel);
                            mod_Fuel.AddFuel(fuel.Fuel.x);

                            // 点燃燃料
                            mod_Fuel.SetIgnited(true);
                            
                            // 温度上限取决于燃料
                            Data.MaxTemperature = fuel.MaxTemperature;
                        }

                        SmeltingProcess(deltaTime); // 继续熔炼
                    }
                    else
                    {
                        // 真正燃料耗尽 → 停止熔炼
                        Data.IsSmelting = false;
                        Debug.Log("燃料耗尽，熔炼停止！");
                    }
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
            
            // 如果燃料模块是点燃的，让它也熄灭
            if (mod_Fuel.GetIgnitedState())
            {
                mod_Fuel.SetIgnited(false);
            }
        }

        // 同步所有UI
        UpdateUI();
    }

    public override void Load()
    {
        // 从 SaveData 读取
        SaveData.ReadData(ref Data);
        
        // 同步数据
        if (Data.InvData.Count == 0)
        {
            if (inputInventory != null && inputInventory.Data != null)
                Data.InvData[inputInventory.Data.Name] = inputInventory.Data;
            if (outputInventory != null && outputInventory.Data != null)
                Data.InvData[outputInventory.Data.Name] = outputInventory.Data;
            if (fuelInventory != null && fuelInventory.Data != null)
                Data.InvData[fuelInventory.Data.Name] = fuelInventory.Data;
        }
        else
        {
            if (inputInventory != null && Data.InvData.ContainsKey(inputInventory.Data.Name))
                inputInventory.Data = Data.InvData[inputInventory.Data.Name];
            if (outputInventory != null && Data.InvData.ContainsKey(outputInventory.Data.Name))
                outputInventory.Data = Data.InvData[outputInventory.Data.Name];
            if (fuelInventory != null && Data.InvData.ContainsKey(fuelInventory.Data.Name))
                fuelInventory.Data = Data.InvData[fuelInventory.Data.Name];
        }

        // 按钮事件
        if (WorkButton != null)
            WorkButton.onClick.AddListener(OnButtonClick);

        // 如果有手持模块，设置默认目标
        if (item != null && item.itemMods != null && item.itemMods.ContainsKey_ID(ModText.Hand))
        {
            var handInv = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()?._Inventory;
            if (inputInventory != null)
                inputInventory.DefaultTarget_Inventory = handInv;
            if (outputInventory != null)
                outputInventory.DefaultTarget_Inventory = handInv;
        }

        // 初始化库存
        inputInventory?.Init();
        outputInventory?.Init();
        fuelInventory?.Init();

        // 获取交互模块引用
        if (item != null && item.itemMods != null)
        {
            var interactMod = item.itemMods.GetMod_ByID(ModText.Interact);
            if (interactMod != null)
            {
                interactMod.OnAction_Start += Interact_Start;
                interactMod.OnAction_Start += _ => panel?.Toggle();
                interactMod.OnAction_Stop += _ => panel?.Close();
            }
        }

        panel?.Close();

        inputInventory?.RefreshUI();
        outputInventory?.RefreshUI();
        fuelInventory?.RefreshUI();
    }

    public override void Save()
    {
        // 安全检查
        if (Data == null)
            Data = new ModSmeltingData();
            
        if (Data.InvData == null)
            Data.InvData = new Dictionary<string, Inventory_Data>();
            
        // 保存库存数据
        if (inputInventory != null && inputInventory.Data != null)
            Data.InvData[inputInventory.Data.Name] = inputInventory.Data;
        if (outputInventory != null && outputInventory.Data != null)
            Data.InvData[outputInventory.Data.Name] = outputInventory.Data;
        if (fuelInventory != null && fuelInventory.Data != null)
            Data.InvData[fuelInventory.Data.Name] = fuelInventory.Data;
            
        SaveData?.WriteData(Data);
    }
    #endregion

    #region 熔炼核心逻辑
    private void SmeltingProcess(float deltaTime)
    {
        // 检查输入槽是否有物品
        bool hasInputItem = false;
        if (inputInventory != null && inputInventory.Data != null && inputInventory.Data.itemSlots != null)
        {
            foreach (var slot in inputInventory.Data.itemSlots)
            {
                if (slot != null && slot.itemData != null)
                {
                    hasInputItem = true;
                    break;
                }
            }
        }

        // 计算实际的最大温度（受限于熔炉本身的最大温度限制）
        float actualMaxTemp = Data.MaxTemperature > 0 ? Mathf.Min(Data.MaxTemperature, Data.MaxTemperatureLimit) : Data.MaxTemperatureLimit;

        // 如果没有物品 → 进度归零（表示干烧）
        if (!hasInputItem)
        {
            Data.SmeltingProgress = 0f;
            // 温度仍然会上升到燃料允许的上限，但不超过熔炉限制
            Data.Temperature = Mathf.Min(Data.Temperature + Data.TemperatureUpSpeed * 2f * deltaTime, actualMaxTemp);
            // 继续消耗燃料
            mod_Fuel?.ConsumeFuel(deltaTime);
            return; // 不进入熔炼逻辑
        }

        // ===== 以下是正常熔炼逻辑 =====

        // 温度随时间上升，但不超过熔炉限制
        Data.Temperature = Mathf.Min(Data.Temperature + Data.TemperatureUpSpeed * deltaTime, actualMaxTemp);

        // 根据温度计算当前熔炼速度
        float tempRatio = Data.Temperature / actualMaxTemp;
        Data.SmeltingSpeed = Mathf.Lerp(1f, Data.MaxSmeltingSpeed, tempRatio);

        // 按当前速度推进进度
        Data.SmeltingProgress += Data.SmeltingSpeed * deltaTime;

        // 消耗燃料
        mod_Fuel?.ConsumeFuel(deltaTime);

        // 熔炼完成
        if (Data.SmeltingProgress >= 100f)
        {
            Data.SmeltingProgress = 0f;
            CompleteSmelting();
        }
    }

    public void CompleteSmelting()
    {
        try
        {
            // 安全检查
            if (inputInventory == null || inputInventory.Data == null)
            {
                Debug.LogError("输入库存为空，无法完成熔炼");
                return;
            }
            
            if (outputInventory == null || outputInventory.Data == null)
            {
                Debug.LogError("输出库存为空，无法完成熔炼");
                return;
            }

            // 生成配方键列表
            List<string> recipeKeys = GenerateRecipeKey_List(inputInventory);
            
            Recipe recipe = null;
            string matchedKey = null;
            
            // 尝试匹配每个配方键
            foreach (string recipeKey in recipeKeys)
            {
                if (GameRes.Instance != null && 
                    GameRes.Instance.recipeDict != null && 
                    GameRes.Instance.recipeDict.TryGetValue(recipeKey, out recipe))
                {
                    matchedKey = recipeKey;
                    break;
                }
            }
            
            // 验证配方
            if (recipe == null)
            {
                Debug.LogError($"熔炼失败：找不到配方 {string.Join(" 或 ", recipeKeys)}");
                return;
            }

            CookRecipe cookRecipe = recipe as CookRecipe;
            if (cookRecipe == null)
            {
                Debug.LogError($"配方类型错误：{matchedKey} 不是 CookRecipe");
                return;
            }

            // 温度检查
            if (cookRecipe.Temperature > Data.Temperature)
            {
                Debug.LogWarning($"熔炼失败：所需温度 {cookRecipe.Temperature} 当前温度 {Data.Temperature} → 材料有损失！");

                // 惩罚：随机扣除 1~2 个输入材料
                System.Random rand = new System.Random();
                if (inputInventory.Data.itemSlots != null)
                {
                    foreach (var slot in inputInventory.Data.itemSlots)
                    {
                        if (slot != null && slot.itemData != null && slot.itemData.Stack.Amount > 0)
                        {
                            // 扣除数量 = 1 或 2，但不超过当前数量
                            float lossAmount = rand.Next(1, 3); // 1~2
                            lossAmount = Mathf.Min(lossAmount, slot.itemData.Stack.Amount); // 不超过现有数量

                            slot.itemData.Stack.Amount -= lossAmount;
                            Debug.LogWarning($"惩罚扣除：{slot.itemData.IDName} x{lossAmount}");

                            if (slot.itemData.Stack.Amount <= 0)
                            {
                                // 清空物品
                                inputInventory.Data.RemoveItemAll(slot, inputInventory.Data.itemSlots.IndexOf(slot));
                            }
                        }
                    }
                }

                // 刷新 UI
                inputInventory.RefreshUI();
                return;
            }

            // 验证输入槽位数量
            if (!ValidateSlotCount(inputInventory, recipe))
                return;

            // 准备输出物品
            var outputItems = PrepareOutputItems(recipe);
            if (outputItems == null)
                return;

            // 检查资源和空间
            if (!CheckResourcesAndSpace(inputInventory, outputInventory, recipe, outputItems))
            {
                Debug.LogError("熔炼失败：材料不足或输出空间不足");
                return;
            }

            // 执行熔炼
            ExecuteSmelting(inputInventory, outputInventory, recipe, outputItems);
        }
        catch (Exception ex)
        {
            Debug.LogError($"熔炼过程中发生错误: {ex.Message}");
        }
    }
    #endregion

    #region 配方处理逻辑
    /// <summary>
    /// 生成配方键列表（支持Tag模式和itemName模式）
    /// </summary>
    private List<string> GenerateRecipeKey_List(Inventory inputInv)
    {
        List<string> recipeKeys = new List<string>();
        
        // 安全检查
        if (inputInv == null || inputInv.Data == null || inputInv.Data.itemSlots == null)
            return recipeKeys;
        
        // 生成基于物品名称的配方键
        Input_List inputList = new Input_List();
        foreach (ItemSlot slot in inputInv.Data.itemSlots)
        {
            if (slot == null || slot.itemData == null)
            {
                inputList.AddNameItem("");
            }
            else
            {
                inputList.AddNameItem(slot.itemData.IDName);
            }
        }
        recipeKeys.Add(inputList.ToString());
        
        // 生成基于Tag的配方键（为每个有Tag的物品生成一个版本）
        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            if (slot != null && slot.itemData != null && slot.itemData.Tags != null)
            {
                // 为每个包含Tag的物品生成一个基于Tag的配方键版本
                Input_List tagInputList = new Input_List();
                for (int j = 0; j < inputInv.Data.itemSlots.Count; j++)
                {
                    if (j == i && slot.itemData.Tags.MakeTag != null && slot.itemData.Tags.MakeTag.values != null && slot.itemData.Tags.MakeTag.values.Count > 0)
                    {
                        // 使用第一个Type标签
                        if (slot.itemData.Tags.MakeTag.values.Count > 0)
                        {
                            tagInputList.AddTagItem(slot.itemData.Tags.MakeTag.values[0]);
                        }
                        else
                        {
                            tagInputList.AddNameItem(slot.itemData?.IDName ?? "");
                        }
                    }
                    else
                    {
                        var otherSlot = inputInv.Data.itemSlots[j];
                        tagInputList.AddNameItem(otherSlot?.itemData?.IDName ?? "");
                    }
                }
                recipeKeys.Add(tagInputList.ToString());
            }
        }
        
        return recipeKeys;
    }

    private bool ValidateSlotCount(Inventory inputInv, Recipe recipe)
    {
        if (inputInv == null || inputInv.Data == null || recipe == null || recipe.inputs == null)
            return false;
            
        if (inputInv.Data.itemSlots == null || recipe.inputs.RowItems_List == null)
            return false;

        if (inputInv.Data.itemSlots.Count != recipe.inputs.RowItems_List.Count)
        {
            Debug.LogError($"输入槽位数量不匹配：配方需要 {recipe.inputs.RowItems_List.Count} 个输入槽，当前有 {inputInv.Data.itemSlots.Count} 个");
            return false;
        }
        return true;
    }

    private List<ItemData> PrepareOutputItems(Recipe recipe)
    {
        var itemsToAdd = new List<ItemData>();
        
        if (recipe == null || recipe.outputs == null || recipe.outputs.results == null)
            return null;
            
        foreach (var output in recipe.outputs.results)
        {
            if (string.IsNullOrEmpty(output.ItemName))
            {
                Debug.LogError($"配方输出项名称为空（配方：{recipe.name}）");
                return null;
            }
            
            if (GameRes.Instance == null || GameRes.Instance.AllPrefabs == null)
            {
                Debug.LogError($"GameRes实例或预制体字典为空：{output.ItemName}（配方：{recipe.name}）");
                return null;
            }
            
            if (!GameRes.Instance.AllPrefabs.TryGetValue(output.ItemName, out var prefab) || prefab == null)
            {
                Debug.LogError($"预制体不存在：{output.ItemName}（配方：{recipe.name}）");
                return null;
            }
            
            Item outputitem = prefab.GetComponent<Item>();
            if (outputitem == null)
            {
                Debug.LogError($"预制体 {output.ItemName} 上找不到Item组件（配方：{recipe.name}）");
                return null;
            }
            
            ItemData newItem = outputitem.Get_NewItemData();
            if (newItem == null)
            {
                Debug.LogError($"无法创建 {output.ItemName} 的ItemData（配方：{recipe.name}）");
                return null;
            }
            
            newItem.Stack.Amount = output.amount;
            itemsToAdd.Add(newItem);
        }
        
        return itemsToAdd;
    }

    private bool CheckResourcesAndSpace(Inventory inputInv, Inventory outputInv, 
        Recipe recipe, List<ItemData> outputItems)
    {
        // 检查输入材料
        if (inputInv == null || inputInv.Data == null || inputInv.Data.itemSlots == null ||
            recipe == null || recipe.inputs == null || recipe.inputs.RowItems_List == null)
            return false;

        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            if (slot == null || slot.itemData == null)
                return false;

            if (slot.itemData.Stack.Amount < required.amount)
                return false;
        }

        // 检查输出空间
        if (outputInv == null || outputInv.Data == null || outputItems == null)
            return false;
            
        foreach (var item in outputItems)
        {
            if (item == null || !outputInv.Data.TryAddItem(item, false))
                return false;
        }

        return true;
    }

    private void ExecuteSmelting(Inventory inputInv, Inventory outputInv, 
        Recipe recipe, List<ItemData> outputItems)
    {
        if (inputInv == null || inputInv.Data == null || 
            outputInv == null || outputInv.Data == null || 
            recipe == null || recipe.inputs == null || recipe.inputs.RowItems_List == null ||
            outputItems == null)
        {
            Debug.LogError("执行熔炼时参数为空");
            return;
        }

        Debug.Log($"开始熔炼：{recipe.name}");
        Debug.Log($"输入材料：{string.Join(",", recipe.inputs.RowItems_List.Select(r => $"{r.ItemName}x{r.amount}"))}");
        Debug.Log($"产出物品：{string.Join(", ", outputItems.Select(item => $"{item.Stack.Amount}x{item.IDName}"))}");

        // 添加产物
        foreach (var item in outputItems)
        {
            if (item != null)
            {
                outputInv.Data.TryAddItem(item);
                Debug.Log($"添加产物：{item.Stack.Amount}x{item.IDName}");
            }
        }

        // 扣除输入材料
        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0 || slot == null || slot.itemData == null) continue;

            Debug.Log($"扣除材料：{required.ItemName} x{required.amount}，当前有 {slot.itemData.Stack.Amount}");

            slot.itemData.Stack.Amount -= required.amount;
            if (slot.itemData.Stack.Amount <= 0)
            {
                Debug.Log($"输入槽 {i} 的 {required.ItemName} 已用尽并移除");
                inputInv.Data.RemoveItemAll(slot, i);
            }
            else
            {
                Debug.Log($"输入槽 {i} 剩余 {required.ItemName} x{slot.itemData.Stack.Amount}");
            }
            inputInv.RefreshUI(i);
        }
        
        // 执行配方动作
        if (recipe.action != null)
        {
            foreach(var action in recipe.action)
            {
                if (action != null && inputInv.Data.itemSlots != null && 
                    action.slotIndex >= 0 && action.slotIndex < inputInv.Data.itemSlots.Count)
                {
                    action.Apply(inputInv.Data.itemSlots[action.slotIndex]);
                }
            }
        }

        outputInv.RefreshUI();
        inputInv.RefreshUI();
        Debug.Log($"熔炼完成：{recipe.name}");
    }

    // 保留旧版本的检查方法以保持兼容性
    private bool CheckEnough(Inventory inputInventory_,
                               Inventory outputInventory_,
                               Input_List inputList,
                               List<ItemData> itemsToAdd)
    {
        // 检查每个插槽的物品是否满足要求
        if (inputInventory_ == null || inputInventory_.Data == null || inputInventory_.Data.itemSlots == null ||
            inputList == null || inputList.RowItems_List == null ||
            outputInventory_ == null || outputInventory_.Data == null ||
            itemsToAdd == null)
            return false;

        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = inputList.RowItems_List[i];

            // 如果该插槽不需要物品则跳过
            if (required.amount == 0) continue;

            // 检查物品存在且名称匹配
            if (slot == null || slot.itemData == null ||
                slot.itemData.IDName != required.ItemName)
                return false;

            // 检查数量足够
            if (slot.itemData.Stack.Amount < required.amount)
                return false;
        }

        // 检查输出空间
        foreach (var item in itemsToAdd)
        {
            if (item == null || !outputInventory_.Data.TryAddItem(item, false))
                return false;
        }

        return true;
    }
    #endregion

    #region UI与交互处理
private void UpdateUI()
{
    // 熔炼进度条
    if (progressSlider != null)
        progressSlider.value = Data.SmeltingProgress / 100f;

    // 燃料条
    if (fuelSlider != null && mod_Fuel != null && mod_Fuel.Data != null)
        fuelSlider.value = mod_Fuel.Data.Fuel.y > 0 ? mod_Fuel.Data.Fuel.x / mod_Fuel.Data.Fuel.y : 0;

    // 温度条（使用熔炉限制温度作为最大值）
    if (temperatureSlider != null)
    {
        // 始终使用MaxTemperatureLimit作为最大值显示给玩家参考
        float maxTempForDisplay = Data.MaxTemperatureLimit;
        temperatureSlider.value = maxTempForDisplay > 0 ? Data.Temperature / maxTempForDisplay : 0;
    }
    
    // 温度数值文本
    if (TemperatureText != null)
    {
        // 显示实际的温度限制（燃料限制和炉子物理限制中的较小值）
        float actualMaxTemp = Data.MaxTemperature > 0 ? Mathf.Min(Data.MaxTemperature, Data.MaxTemperatureLimit) : Data.MaxTemperatureLimit;
        TemperatureText.text = $"{Mathf.RoundToInt(Data.Temperature)}°C / {Mathf.RoundToInt(actualMaxTemp)}°C (炉子上限: {Mathf.RoundToInt(Data.MaxTemperatureLimit)}°C)";
    }
}

    private void OnButtonClick()
    {
        // 安全检查
        if (fuelInventory == null || fuelInventory.Data == null)
        {
            Debug.LogWarning("燃料库存未初始化！");
            return;
        }

        // 查找行为tag中为Ignition的物品
        var ignitionItem = fuelInventory.Data.FindItemByTagTypeAndTag("FunctionTag", "Ignition");
        if (ignitionItem == null)
        {
            Debug.LogWarning("无法点燃：缺少点火装置！");
            return;
        }

        // 如果已经在熔炼中，不允许主动停止
        if (Data.IsSmelting)
        {
            Debug.Log("熔炼已经开始，无法主动停止。只有燃料耗尽时才会停止。");
            return;
        }
        
        // 开始熔炼
        Data.IsSmelting = true;
        
        // 设置默认最大温度为熔炉限制温度（如果还没有燃料提供温度的话）
        if (Data.MaxTemperature <= 0)
        {
            Data.MaxTemperature = Data.MaxTemperatureLimit;
        }
        
        // 点燃燃料模块
        mod_Fuel?.SetIgnited(true);
        Debug.Log("熔炉已点燃并开始熔炼！");
    }

    public void Interact_Start(Item item_)
    {
        if (item_ == null || item_.itemMods == null)
            return;
            
        var handInventory = item_.itemMods.GetMod_ByID(ModText.Hand)?.GetComponent<IInventory>()?._Inventory;

        if (inputInventory != null)
            inputInventory.DefaultTarget_Inventory = handInventory;
        if (outputInventory != null)
            outputInventory.DefaultTarget_Inventory = handInventory;
        if (fuelInventory != null)
            fuelInventory.DefaultTarget_Inventory = handInventory;
    }
    #endregion

    #region 燃烧状态控制
    /// <summary>
    /// 设置燃烧状态
    /// </summary>
    /// <param name="isBurning">是否燃烧</param>
    public void SetBurningState(bool isBurning)
    {
        Data.IsSmelting = isBurning;
        mod_Fuel?.SetIgnited(isBurning);
        
        if (isBurning)
        {
            Debug.Log("熔炉开始燃烧！");
        }
        else
        {
            Debug.Log("熔炉停止燃烧！");
        }
    }
    
    /// <summary>
    /// 获取燃烧状态
    /// </summary>
    /// <returns>是否正在燃烧</returns>
    public bool GetBurningState()
    {
        return Data.IsSmelting && (mod_Fuel?.GetIgnitedState() ?? false);
    }
    
    /// <summary>
    /// 切换燃烧状态
    /// </summary>
    public void ToggleBurningState()
    {
        SetBurningState(!GetBurningState());
    }
    #endregion
}