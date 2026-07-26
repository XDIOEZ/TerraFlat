using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public class Mod_Furnace : Module, IInteractable
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.1f;

    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }
    public ModSmeltingData Data = new ModSmeltingData();
    [SerializeReference]
    public List<string> RawData = new List<string>();

    [Tooltip("输入容器，用于存放合成所需的原材料物品")]
    public Inventory InputInventory;
    [Tooltip("输出容器，用于存放合成后得到的物品")]
    public Inventory OutputInventory;
    [Tooltip("燃料容器，用于存放熔炉所需的燃料物品")]
    public Inventory FuelInventory;
    public Mod_Fuel mod_Fuel; // 燃料模块
    public List<string> ignitionItemIds = new List<string> { "FireSeed" }; // 可用于点火的火种ID
    public List<string> ignitionTags = new List<string> { "火种" }; // 可用于点火的火种标签
    public float ignitionFuelValueOverride = 8f; // 火种有效燃料值（较小）
    public float ignitionMaxTemperatureOverride = 180f; // 火种点火时提供的温度上限（较低）
    public BasePanel basePanel; // 熔炉面板
    public GameObject UI_Prefab; // 熔炉UI预制体
    private const float PanelDestroyDelay = 30f;
    private Coroutine panelDestroyCoroutine;
    #endregion

    #region 生命周期

    public override void Load()
    {
        mod_Fuel = item.GetComponentInChildren<Mod_Fuel>();
        ModSaveData.ReadData(ref RawData);
        InputInventory.InitData();
        OutputInventory.InitData();
        FuelInventory.InitData();
    }

    public void OnInteractStart(Item playerItem)
    {
        if (basePanel == null)
        {
            OpenUI();
        }

        var handInv = playerItem.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (handInv == null)
        {
            Debug.LogError("玩家手部容器为空！");
            return;
        }

        CancelPanelDestroyCountdown();
        basePanel.Toggle();

        bool isOpen = basePanel.IsOpen();
        InputInventory.DefaultTarget_Inventory = isOpen ? handInv : null;
        OutputInventory.DefaultTarget_Inventory = isOpen ? handInv : null;
        FuelInventory.DefaultTarget_Inventory = isOpen ? handInv : null;
        InputInventory.SyncQuickTransferTarget(basePanel);

        if (!isOpen)
            StartPanelDestroyCountdown();
    }

    public void OnInteractCancel(Item playerItem)
    {
        if (basePanel == null)
            return;

        ClosePanelAndClearTransferContext();
        StartPanelDestroyCountdown();
    }

    public override void Save()
    {
        ModSaveData.WriteData(RawData);
    }

    private void OnDestroy()
    {
        ClosePanelAndClearTransferContext();
        CancelPanelDestroyCountdown();
    }

    private void ClosePanelAndClearTransferContext()
    {
        InputInventory.DefaultTarget_Inventory = null;
        OutputInventory.DefaultTarget_Inventory = null;
        FuelInventory.DefaultTarget_Inventory = null;

        if (basePanel != null)
            basePanel.Close();

        InputInventory.SyncQuickTransferTarget(basePanel);
    }

    #region 面板延迟销毁

    private void StartPanelDestroyCountdown()
    {
        CancelPanelDestroyCountdown();
        panelDestroyCoroutine = StartCoroutine(CoDestroyPanelAfterDelay());
    }

    private void CancelPanelDestroyCountdown()
    {
        if (panelDestroyCoroutine == null)
            return;

        StopCoroutine(panelDestroyCoroutine);
        panelDestroyCoroutine = null;
    }

    private IEnumerator CoDestroyPanelAfterDelay()
    {
        yield return new WaitForSeconds(PanelDestroyDelay);

        if (basePanel != null && !basePanel.IsOpen())
        {
            basePanel.Destroy();
            basePanel = null;
            WorkButton = null;
            progressSlider = null;
            fuelSlider = null;
            temperatureSlider = null;
            TemperatureText = null;
        }

        panelDestroyCoroutine = null;
    }

    #endregion
    #endregion
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

    #region Unity生命周期

    public void OnValidate()
    {
        _Data.Name = ModText.Furnace;
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
                var fuelItem = FuelInventory.Data.GetModuleByID(ModText.Fuel);
                if (fuelItem != null)
                {
                    // 从物品转化为燃料值
                    ItemSlot slot = FuelInventory.Data.GetItemSlotByModuleID(fuelItem.ID);
                    if (slot != null && slot.itemData != null && slot.itemData.Stack.Amount > 0)
                    {
                        slot.itemData.Stack.Amount -= 1; // 扣 1 个燃料物品
                        slot.RefreshUI();

                        Ex_ModData_MemoryPackable fuelData = fuelItem as Ex_ModData_MemoryPackable;
                        if (fuelData != null)
                        {
                            fuelData.OutData(out FuelData fuel);
                            ResolveFuelParams(slot.itemData, fuel, out float fuelValue, out float maxTemperature);
                            mod_Fuel.AddFuel(fuelValue);

                            // 点燃燃料
                            mod_Fuel.SetIgnited(true);

                            // 温度上限取决于燃料
                            Data.MaxTemperature = maxTemperature;
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

    /// <summary>
    /// UI初始化（在面板创建后调用）
    /// </summary>
    public void OpenUI()
    {
        basePanel = UIManager.Instance.CreatePanelFromGameObject(UI_Prefab);
        BindSlotsByPrefix(InputInventory, "输入");
        BindSlotsByPrefix(OutputInventory, "输出");
        BindSlotsByPrefix(FuelInventory, "燃料");
       

        // 同步 UI 数据
        InputInventory.SyncData();
        OutputInventory.SyncData();
        FuelInventory.SyncData();

        // 初始化UI引用
        progressSlider = basePanel.GetSlider("熔炼进度条");
        temperatureSlider = basePanel.GetSlider("温度显示条");
        TemperatureText = basePanel.GetText("温度数值文本");
        fuelSlider = basePanel.GetSlider("燃料显示条");
        WorkButton = basePanel.GetButton("合成按钮");

        // 按钮事件
        WorkButton.onClick.AddListener(OnButtonClick);

        // 初始化UI显示
        basePanel?.Close();
        UpdateUI();
        InputInventory?.RefreshUI();
        OutputInventory?.RefreshUI();
        FuelInventory?.RefreshUI();
    }

    private void BindSlotsByPrefix(Inventory inventory, string prefix)
    {
        if (inventory == null || inventory.Data == null || inventory.Data.itemSlots == null)
        {
            Debug.LogWarning($"[Mod_Furnace] 跳过绑定，{prefix} Inventory 无效");
            return;
        }

        inventory.itemSlot_UI.Clear();

        int boundIndex = 0;
        int maxTry = Mathf.Max(inventory.Data.itemSlots.Count, 12);
        for (int i = 1; i <= maxTry; i++)
        {
            if (boundIndex >= inventory.Data.itemSlots.Count)
                break;

            var button = basePanel.GetButton($"{prefix}_{i}");
            if (button == null)
                continue;

            var slotUI = button.GetComponent<ItemSlot_UI>();
            if (slotUI == null)
                continue;

            inventory.BindSlotUI(slotUI, boundIndex);
            boundIndex++;
        }
    }
    #endregion


    #region 熔炼核心逻辑
    private void SmeltingProcess(float deltaTime)
    {
        // 检查输入槽是否有物品
        bool hasInputItem = false;
        if (InputInventory != null && InputInventory.Data != null && InputInventory.Data.itemSlots != null)
        {
            foreach (var slot in InputInventory.Data.itemSlots)
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
        // 安全检查
        if (InputInventory == null || InputInventory.Data == null)
        {
            Debug.LogError("输入库存为空，无法完成熔炼");
            return;
        }

        if (OutputInventory == null || OutputInventory.Data == null)
        {
            Debug.LogError("输出库存为空，无法完成熔炼");
            return;
        }

        // 生成配方键列表
        List<string> recipeKeys = GenerateRecipeKey_List(InputInventory);
        // 额外生成基于最小包围网格的配方键，作为优化后的匹配候选
        var optimizedRecipeKeys = CalculateMinimalBoundingGrid(InputInventory);
        if (optimizedRecipeKeys != null && optimizedRecipeKeys.Count > 0)
            recipeKeys.AddRange(optimizedRecipeKeys);

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

        // 温度检查 - 过高温度处理
        if (Data.Temperature > cookRecipe.Temperature_Max)
        {
            Debug.LogWarning($"温度过高：所需温度 {cookRecipe.Temperature} 当前温度 {Data.Temperature} → 产出烧焦物！");

            // 使用与正常合成相同的材料扣除逻辑，确保完整扣除所有材料
            if (recipe.inputs.inputOrder == RecipeInputRule.规则合成)
            {
                // 规则合成：使用传统的位置扣除逻辑
                ExecuteTraditionalDeduction(InputInventory, recipe);
            }
            else if (recipe.inputs.inputOrder == RecipeInputRule.无规则合成)
            {
                // 无规则合成：遍历所有槽位扣除材料
                foreach (var required in recipe.inputs.RowItems_List)
                {
                    if (required.amount == 0) continue;

                    float remainingAmountToConsume = required.amount;

                    // 遍历所有槽位查找匹配的物品
                    for (int i = 0; i < InputInventory.Data.itemSlots.Count && remainingAmountToConsume > 0; i++)
                    {
                        var slot = InputInventory.Data.itemSlots[i];
                        if (slot == null || slot.itemData == null) continue;

                        // 检查物品是否匹配需求
                        bool isMatch = false;
                        if (required.matchMode == MatchMode.ExactItem)
                        {
                            isMatch = slot.itemData.IDName == required.ItemName;
                        }
                        else if (required.matchMode == MatchMode.ByTag)
                        {
                            isMatch = slot.itemData.Tags != null &&
                                     slot.itemData.Tags != null &&
                                     slot.itemData.Tags.Contains(required.Tag);
                        }

                        if (isMatch && slot.itemData.Stack.Amount > 0)
                        {
                            // 计算本次可以消耗的数量
                            float consumeAmount = Mathf.Min(remainingAmountToConsume, slot.itemData.Stack.Amount);
                            slot.itemData.Stack.Amount -= consumeAmount;
                            remainingAmountToConsume -= consumeAmount;

                            Debug.LogWarning($"烧焦物扣除：插槽 {i}：消耗 {slot.itemData.IDName} x{consumeAmount}，剩余 {slot.itemData.Stack.Amount}");

                            // 如果物品用完，移除物品
                            if (slot.itemData.Stack.Amount <= 0)
                            {
                                Debug.Log($"插槽 {i}：{slot.itemData.IDName} 已耗尽，移除物品");
                                InputInventory.Data.RemoveItemAll(slot, i);
                            }

                            InputInventory.RefreshUI(i);
                        }
                    }
                }
            }

            // 产出烧焦物
            string charredMatterId = "CharredMatter";
            if (GameRes.Instance != null &&
                GameRes.Instance.AllPrefabs != null &&
                GameRes.Instance.AllPrefabs.TryGetValue(charredMatterId, out var prefab) &&
                prefab != null)
            {
                Item outputItem = prefab.GetComponent<Item>();
                if (outputItem != null)
                {
                    ItemData newItem = outputItem.Get_NewItemData();
                    if (newItem != null)
                    {
                        // 产出1个烧焦物
                        newItem.Stack.Amount = 1;
                        OutputInventory.Data.TryAddItem(newItem);
                        Debug.LogWarning($"产出烧焦物：{newItem.IDName} x{newItem.Stack.Amount}");
                    }
                }
            }

            // 刷新 UI
            InputInventory.RefreshUI();
            OutputInventory.RefreshUI();
            return;
        }

        // 温度不足检查
        else if (cookRecipe.Temperature > Data.Temperature)
        {
            Debug.LogWarning($"熔炼失败：所需温度 {cookRecipe.Temperature} 当前温度 {Data.Temperature} → 材料有损失！");

            // 惩罚：随机扣除 1~2 个输入材料
            System.Random rand = new System.Random();
            if (InputInventory.Data.itemSlots != null)
            {
                foreach (var slot in InputInventory.Data.itemSlots)
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
                            InputInventory.Data.RemoveItemAll(slot, InputInventory.Data.itemSlots.IndexOf(slot));
                        }
                    }
                }
            }

            // 刷新 UI
            InputInventory.RefreshUI();
            return;
        }

        // 验证输入槽位数量
        if (!ValidateSlotCount(InputInventory, recipe))
            return;

        // 准备输出物品
        var outputItems = PrepareOutputItems(recipe);
        if (outputItems == null)
            return;

        // 检查资源和空间
        if (!CheckResourcesAndSpace(InputInventory, OutputInventory, recipe, outputItems))
        {
            Debug.LogError("熔炼失败：材料不足或输出空间不足");
            return;
        }

        // 执行熔炼
        ExecuteSmelting(InputInventory, OutputInventory, recipe, outputItems);
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
        inputList.recipeType = RecipeType.Smelting;
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
                tagInputList.recipeType = RecipeType.Smelting;
                for (int j = 0; j < inputInv.Data.itemSlots.Count; j++)
                {
                    if (j == i && slot.itemData.Tags != null && slot.itemData.Tags != null && slot.itemData.Tags.Count > 0)
                    {
                        // 使用第一个Type标签
                        if (slot.itemData.Tags.Count > 0)
                        {
                            tagInputList.AddTagItem(slot.itemData.Tags[0]);
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

    /// <summary>
    /// 计算最小包围网格并生成对应的配方键（用于在更小网格中匹配配方）
    /// </summary>
    private List<string> CalculateMinimalBoundingGrid(Inventory inputInv)
    {
        var result = new List<string>();
        if (inputInv == null || inputInv.Data == null || inputInv.Data.itemSlots == null)
            return result;

        // 为避免非完全平方数量导致的索引越界，使用列数/行数的上取整计算
        int count = inputInv.Data.itemSlots.Count;
        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt((float)count / cols);

        int minRow = rows;
        int maxRow = -1;
        int minCol = cols;
        int maxCol = -1;

        for (int i = 0; i < count; i++)
        {
            if (inputInv.Data.itemSlots[i].itemData != null)
            {
                int row = i / cols;
                int col = i % cols;
                minRow = Mathf.Min(minRow, row);
                maxRow = Mathf.Max(maxRow, row);
                minCol = Mathf.Min(minCol, col);
                maxCol = Mathf.Max(maxCol, col);
            }
        }

        if (maxRow >= 0 && maxCol >= 0)
        {
            Input_List minimalGridList = new Input_List();
            minimalGridList.recipeType = RecipeType.Smelting;

            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    int slotIndex = row * cols + col;
                    if (slotIndex >= 0 && slotIndex < count && inputInv.Data.itemSlots[slotIndex].itemData != null)
                    {
                        minimalGridList.AddNameItem(inputInv.Data.itemSlots[slotIndex].itemData.IDName);
                    }
                    else
                    {
                        minimalGridList.AddNameItem("");
                    }
                }
            }

            // 有序
            minimalGridList.inputOrder = RecipeInputRule.规则合成;
            result.Add(minimalGridList.ToString());

            // 无序
            minimalGridList.inputOrder = RecipeInputRule.无规则合成;
            result.Add(minimalGridList.ToString());

            // 生成 tag 版本（遍历最小包围网格内每个有Tag的物品）
            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    int slotIndex = row * cols + col;
                    if (slotIndex < 0 || slotIndex >= count) continue;

                    var slot = inputInv.Data.itemSlots[slotIndex];
                    if (slot != null && slot.itemData != null && slot.itemData.Tags != null && slot.itemData.Tags != null && slot.itemData.Tags.Count > 0)
                    {
                        Input_List tagGridList = new Input_List();
                        tagGridList.recipeType = RecipeType.Smelting;

                        for (int r = minRow; r <= maxRow; r++)
                        {
                            for (int c = minCol; c <= maxCol; c++)
                            {
                                int currentSlotIndex = r * cols + c;
                                if (currentSlotIndex == slotIndex)
                                {
                                    tagGridList.AddTagItem(slot.itemData.Tags[0]);
                                }
                                else
                                {
                                    if (currentSlotIndex >= 0 && currentSlotIndex < count)
                                    {
                                        var other = inputInv.Data.itemSlots[currentSlotIndex];
                                        tagGridList.AddNameItem(other?.itemData?.IDName ?? "");
                                    }
                                    else
                                    {
                                        tagGridList.AddNameItem("");
                                    }
                                }
                            }
                        }

                        tagGridList.inputOrder = RecipeInputRule.规则合成;
                        result.Add(tagGridList.ToString());

                        tagGridList.inputOrder = RecipeInputRule.无规则合成;
                        result.Add(tagGridList.ToString());
                    }
                }
            }
        }

        return result;
    }

    private bool ValidateSlotCount(Inventory inputInv, Recipe recipe)
    {
        if (inputInv == null || inputInv.Data == null || recipe == null || recipe.inputs == null)
            return false;

        if (inputInv.Data.itemSlots == null || recipe.inputs.RowItems_List == null)
            return false;

        // if (inputInv.Data.itemSlots.Count != recipe.inputs.RowItems_List.Count)
        // {
        //     Debug.LogError($"输入槽位数量不匹配：配方需要 {recipe.inputs.RowItems_List.Count} 个输入槽，当前有 {inputInv.Data.itemSlots.Count} 个");
        //     return false;
        // }
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
        // 检查recipe.inputs是有规则合成还是无规则合成（参考工作台的高级检查逻辑）
        if (recipe == null || recipe.inputs == null || recipe.inputs.RowItems_List == null)
            return false;

        if (recipe.inputs.inputOrder == RecipeInputRule.规则合成)
        {
            // 计算输入槽位的网格大小
            if (inputInv == null || inputInv.Data == null || inputInv.Data.itemSlots == null)
                return false;

            // 对于非完全平方数，使用向上取整确保覆盖所有槽位
            int inputCount = inputInv.Data.itemSlots.Count;
            int recipeCount = recipe.inputs.RowItems_List.Count;

            int inputGridSize = Mathf.CeilToInt(Mathf.Sqrt(inputCount));
            int recipeGridSize = Mathf.CeilToInt(Mathf.Sqrt(recipeCount));

            // 如果配方网格大小小于输入网格大小，尝试使用最小包围网格匹配
            if (recipeGridSize < inputGridSize)
            {
                // 找出包含物品的最小矩形边界
                int minRow = inputGridSize;
                int maxRow = -1;
                int minCol = inputGridSize;
                int maxCol = -1;

                for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
                {
                    if (inputInv.Data.itemSlots[i] != null && inputInv.Data.itemSlots[i].itemData != null)
                    {
                        int row = i / inputGridSize;
                        int col = i % inputGridSize;

                        minRow = Mathf.Min(minRow, row);
                        maxRow = Mathf.Max(maxRow, row);
                        minCol = Mathf.Min(minCol, col);
                        maxCol = Mathf.Max(maxCol, col);
                    }
                }

                // 如果找到物品并且边界大小与配方匹配
                int boundedHeight = maxRow - minRow + 1;
                int boundedWidth = maxCol - minCol + 1;

                if (maxRow >= 0 && maxCol >= 0 && boundedHeight == recipeGridSize && boundedWidth == recipeGridSize)
                {
                    // 在最小包围网格内检查资源
                    for (int r = 0; r < recipeGridSize; r++)
                    {
                        for (int c = 0; c < recipeGridSize; c++)
                        {
                            int recipeIndex = r * recipeGridSize + c;
                            int inputIndex = (minRow + r) * inputGridSize + (minCol + c);

                            var required = recipe.inputs.RowItems_List[recipeIndex];
                            if (required.amount == 0) continue;

                            // 检查输入槽位是否有效且包含物品
                            if (inputIndex >= inputInv.Data.itemSlots.Count || inputInv.Data.itemSlots[inputIndex].itemData == null)
                                return false;

                            var slot = inputInv.Data.itemSlots[inputIndex];
                            if (slot.itemData.Stack.Amount < required.amount)
                                return false;
                        }
                    }
                    // 最小包围网格匹配成功，继续检查输出空间
                }
                else
                {
                    // 如果边界与配方不匹配，尝试传统的位置匹配作为后备方案
                    // 但只在输入和配方网格大小相同时执行
                    if (inputGridSize == recipeGridSize)
                    {
                        if (inputInv == null || inputInv.Data == null) return false;
                        int loopCount = Mathf.Min(inputInv.Data.itemSlots.Count, recipe.inputs.RowItems_List.Count);
                        for (int i = 0; i < loopCount; i++)
                        {
                            var slot = inputInv.Data.itemSlots[i];
                            var required = recipe.inputs.RowItems_List[i];

                            if (required.amount == 0) continue;

                            if (slot == null || slot.itemData == null || slot.itemData.Stack.Amount < required.amount)
                                return false;
                        }
                    }
                    else
                    {
                        // 如果网格大小不匹配且找不到合适的最小包围网格，返回失败
                        return false;
                    }
                }
            }
            else
            {
                // 原始逻辑：当配方网格大小大于等于输入网格大小时
                if (inputInv == null || inputInv.Data == null) return false;
                int loopCount = Mathf.Min(inputInv.Data.itemSlots.Count, recipe.inputs.RowItems_List.Count);
                for (int i = 0; i < loopCount; i++)
                {
                    if (i >= inputInv.Data.itemSlots.Count) break;

                    var slot = inputInv.Data.itemSlots[i];
                    var required = recipe.inputs.RowItems_List[i];

                    if (required.amount == 0) continue;

                    if (slot == null || slot.itemData == null)
                        return false;

                    if (slot.itemData.Stack.Amount < required.amount)
                        return false;
                }
            }
        }
        else if (recipe.inputs.inputOrder == RecipeInputRule.无规则合成)
        {
            // 无规则合成逻辑保持不变
            if (inputInv == null || inputInv.Data == null || inputInv.Data.itemSlots == null) return false;
            foreach (var required in recipe.inputs.RowItems_List)
            {
                if (required.amount == 0) continue;

                float foundAmount = 0;
                foreach (var slot in inputInv.Data.itemSlots)
                {
                    if (slot.itemData == null) continue;

                    bool isMatch = false;
                    if (required.matchMode == MatchMode.ExactItem)
                    {
                        isMatch = slot.itemData.IDName == required.ItemName;
                    }
                    else if (required.matchMode == MatchMode.ByTag)
                    {
                        isMatch = slot.itemData.Tags != null &&
                                 slot.itemData.Tags != null &&
                                 slot.itemData.Tags.Contains(required.Tag);
                    }

                    if (isMatch)
                    {
                        foundAmount += slot.itemData.Stack.Amount;
                    }
                }

                if (foundAmount < required.amount)
                    return false;
            }
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
        if (recipe.inputs.inputOrder == RecipeInputRule.规则合成)
        {
            // 计算输入槽位和配方的网格大小
            int inputGridSize = Mathf.CeilToInt(Mathf.Sqrt(inputInv.Data.itemSlots.Count));
            int recipeGridSize = Mathf.CeilToInt(Mathf.Sqrt(recipe.inputs.RowItems_List.Count));

            // 如果配方网格大小小于输入网格大小，尝试使用最小包围网格匹配扣除
            if (recipeGridSize < inputGridSize)
            {
                // 找出包含物品的最小矩形边界
                int minRow = inputGridSize;
                int maxRow = -1;
                int minCol = inputGridSize;
                int maxCol = -1;

                for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
                {
                    if (inputInv.Data.itemSlots[i] != null && inputInv.Data.itemSlots[i].itemData != null)
                    {
                        int row = i / inputGridSize;
                        int col = i % inputGridSize;

                        minRow = Mathf.Min(minRow, row);
                        maxRow = Mathf.Max(maxRow, row);
                        minCol = Mathf.Min(minCol, col);
                        maxCol = Mathf.Max(maxCol, col);
                    }
                }

                // 如果找到物品并且边界大小与配方匹配
                int boundedHeight = maxRow - minRow + 1;
                int boundedWidth = maxCol - minCol + 1;

                if (maxRow >= 0 && maxCol >= 0 && boundedHeight == recipeGridSize && boundedWidth == recipeGridSize)
                {
                    // 在最小包围网格内扣除材料
                    for (int r = 0; r < recipeGridSize; r++)
                    {
                        for (int c = 0; c < recipeGridSize; c++)
                        {
                            int recipeIndex = r * recipeGridSize + c;
                            int inputIndex = (minRow + r) * inputGridSize + (minCol + c);

                            var required = recipe.inputs.RowItems_List[recipeIndex];
                            if (required.amount == 0) continue;

                            // 检查输入槽位是否有效且包含物品
                            if (inputIndex < inputInv.Data.itemSlots.Count && inputInv.Data.itemSlots[inputIndex] != null && inputInv.Data.itemSlots[inputIndex].itemData != null)
                            {
                                var slot = inputInv.Data.itemSlots[inputIndex];
                                Debug.Log($"插槽 {inputIndex}：需要 {required.ItemName} x{required.amount}，当前有 {slot.itemData.Stack.Amount}");

                                slot.itemData.Stack.Amount -= required.amount;
                                if (slot.itemData.Stack.Amount <= 0)
                                {
                                    Debug.Log($"插槽 {inputIndex}：{required.ItemName} 已耗尽，移除物品");
                                    inputInv.Data.RemoveItemAll(slot, inputIndex);
                                }
                                else
                                {
                                    Debug.Log($"插槽 {inputIndex}：剩余 {required.ItemName} x{slot.itemData.Stack.Amount}");
                                }
                                inputInv.RefreshUI(inputIndex);
                            }
                        }
                    }
                }
                else
                {
                    // 如果边界与配方不匹配，使用传统的位置扣除作为后备方案
                    ExecuteTraditionalDeduction(inputInv, recipe);
                }
            }
            else
            {
                // 原始逻辑：当配方网格大小大于等于输入网格大小时
                ExecuteTraditionalDeduction(inputInv, recipe);
            }
        }
        else if (recipe.inputs.inputOrder == RecipeInputRule.无规则合成)
        {
            // 无序合成逻辑
            foreach (var required in recipe.inputs.RowItems_List)
            {
                if (required.amount == 0) continue;

                float remainingAmountToConsume = required.amount;

                // 遍历所有槽位查找匹配的物品
                for (int i = 0; i < inputInv.Data.itemSlots.Count && remainingAmountToConsume > 0; i++)
                {
                    var slot = inputInv.Data.itemSlots[i];
                    if (slot == null || slot.itemData == null) continue;

                    // 检查物品是否匹配需求
                    bool isMatch = false;
                    if (required.matchMode == MatchMode.ExactItem)
                    {
                        isMatch = slot.itemData.IDName == required.ItemName;
                    }
                    else if (required.matchMode == MatchMode.ByTag)
                    {
                        isMatch = slot.itemData.Tags != null &&
                                 slot.itemData.Tags != null &&
                                 slot.itemData.Tags.Contains(required.Tag);
                    }

                    if (isMatch && slot.itemData.Stack.Amount > 0)
                    {
                        // 计算本次可以消耗的数量
                        float consumeAmount = Mathf.Min(remainingAmountToConsume, slot.itemData.Stack.Amount);
                        slot.itemData.Stack.Amount -= consumeAmount;
                        remainingAmountToConsume -= consumeAmount;

                        Debug.Log($"插槽 {i}：消耗 {slot.itemData.IDName} x{consumeAmount}，剩余 {slot.itemData.Stack.Amount}");

                        // 如果物品用完，移除物品
                        if (slot.itemData.Stack.Amount <= 0)
                        {
                            Debug.Log($"插槽 {i}：{slot.itemData.IDName} 已耗尽，移除物品");
                            inputInv.Data.RemoveItemAll(slot, i);
                        }

                        inputInv.RefreshUI(i);
                    }
                }
            }
        }

        // 执行配方动作
        if (recipe.action != null)
        {
            foreach (var action in recipe.action)
            {
                if (action != null && inputInv.Data.itemSlots != null &&
                    action.slotIndex >= 0 && action.slotIndex < inputInv.Data.itemSlots.Count)
                {

                }
            }
        }

        outputInv.RefreshUI();
        inputInv.RefreshUI();
        Debug.Log($"熔炼完成：{recipe.name}");
    }

    // 提取传统的位置扣除逻辑为单独方法，方便复用
    private void ExecuteTraditionalDeduction(Inventory inputInv, Recipe recipe)
    {
        int loopCount = Mathf.Min(inputInv.Data.itemSlots.Count, recipe.inputs.RowItems_List.Count);
        for (int i = 0; i < loopCount; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0 || slot == null || slot.itemData == null) continue;

            Debug.Log($"插槽 {i}：需要 {required.ItemName} x{required.amount}，当前有 {slot.itemData.Stack.Amount}");

            slot.itemData.Stack.Amount -= required.amount;
            if (slot.itemData.Stack.Amount <= 0)
            {
                Debug.Log($"插槽 {i}：{required.ItemName} 已耗尽，移除物品");
                inputInv.Data.RemoveItemAll(slot, i);
            }
            else
            {
                Debug.Log($"插槽 {i}：剩余 {required.ItemName} x{slot.itemData.Stack.Amount}");
            }
            inputInv.RefreshUI(i);
        }
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
        if (FuelInventory == null || FuelInventory.Data == null)
        {
            Debug.LogWarning("燃料库存未初始化！");
            return;
        }

        // 如果已经在熔炼中，不允许主动停止
        if (Data.IsSmelting)
        {
            Debug.Log("熔炼已经开始，无法主动停止。只有燃料耗尽时才会停止。");
            return;
        }

        // 点火前必须先消耗1个燃料并注入燃料值，避免无燃料空点火
        var fuelItem = FuelInventory.Data.GetModuleByID(ModText.Fuel);
        if (fuelItem == null)
        {
            Debug.LogWarning("无法点火：燃料槽中没有燃料物品！");
            return;
        }

        ItemSlot fuelSlot = FuelInventory.Data.GetItemSlotByModuleID(fuelItem.ID);
        if (fuelSlot == null || fuelSlot.itemData == null || fuelSlot.itemData.Stack.Amount <= 0)
        {
            Debug.LogWarning("无法点火：燃料数量不足！");
            return;
        }

        Ex_ModData_MemoryPackable fuelData = fuelItem as Ex_ModData_MemoryPackable;
        if (fuelData == null)
        {
            Debug.LogError("无法点火：燃料模块数据异常！");
            return;
        }

        if (!IsIgnitionFuel(fuelSlot.itemData))
        {
            Debug.LogWarning($"无法点火：首个点火燃料必须为火种。当前={fuelSlot.itemData.IDName}");
            return;
        }

        fuelData.OutData(out FuelData fuel);
        fuelSlot.itemData.Stack.Amount -= 1;
        fuelSlot.RefreshUI();

        ResolveFuelParams(fuelSlot.itemData, fuel, out float fuelValue, out float maxTemperature);
        mod_Fuel.AddFuel(fuelValue);

        // 温度上限取决于当前消耗的燃料
        Data.MaxTemperature = maxTemperature;

        // 开始熔炼
        Data.IsSmelting = true;

        // 点燃燃料模块
        mod_Fuel?.SetIgnited(true);
        Debug.Log("熔炉已点燃并开始熔炼！");
    }

    private bool IsIgnitionFuel(ItemData itemData)
    {
        if (itemData == null)
            return false;

        if (ignitionItemIds != null && ignitionItemIds.Contains(itemData.IDName))
            return true;

        if (itemData.Tags == null || ignitionTags == null)
            return false;

        return itemData.Tags.ContainsAnyTag(ignitionTags);
    }

    private void ResolveFuelParams(ItemData itemData, FuelData rawFuelData, out float fuelValue, out float maxTemperature)
    {
        fuelValue = rawFuelData.Fuel.x;
        maxTemperature = rawFuelData.MaxTemperature;

        if (!IsIgnitionFuel(itemData))
            return;

        fuelValue = Mathf.Min(fuelValue, ignitionFuelValueOverride);
        maxTemperature = Mathf.Min(maxTemperature, ignitionMaxTemperatureOverride);
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
