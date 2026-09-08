using System.Collections;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public class Mod_Furnace : Module, IInteractable, IItemModuleDependencyBinder
{
    private readonly List<IInventoryHeatTreatment> heatTreatments = new(); // 独立加热能力。
    /// <summary>从模块注册表缓存额外加热规则，不把水处理细节写进熔炉。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        heatTreatments.Clear();
        foreach (Module module in modules.Mods.Values)
            if (module is IInventoryHeatTreatment treatment) heatTreatments.Add(treatment);
    }
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
    private const string InputInventorySaveKey = "furnace.input";
    private const string OutputInventorySaveKey = "furnace.output";
    private const string FuelInventorySaveKey = "furnace.fuel";
    private Coroutine panelDestroyCoroutine;
    private Player currentInteractingPlayer;
    private Player smeltingActor;
    #endregion

    #region 生命周期

    public override void Load()
    {
        mod_Fuel = item.GetComponentInChildren<Mod_Fuel>();
        RestoreSavedState();
        InputInventory.InitData();
        OutputInventory.InitData();
        FuelInventory.InitData();
    }

    public void OnInteractStart(Item playerItem)
    {
        currentInteractingPlayer = playerItem as Player ?? playerItem?.GetComponentInParent<Player>();
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
        {
            currentInteractingPlayer = null;
            StartPanelDestroyCountdown();
        }
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
        Data ??= new ModSmeltingData();
        Data.InvData ??= new Dictionary<string, Inventory_Data>();
        Data.InvData[InputInventorySaveKey] = InputInventory?.Data;
        Data.InvData[OutputInventorySaveKey] = OutputInventory?.Data;
        Data.InvData[FuelInventorySaveKey] = FuelInventory?.Data;
        ModSaveData ??= new Ex_ModData_MemoryPackable();
        ModSaveData.WriteData(Data);
    }

    /// <summary>读取当前版本熔炉进度和三类库存；损坏数据直接中止加载。</summary>
    private void RestoreSavedState()
    {
        if (ModSaveData?.BitData == null || ModSaveData.BitData.Length == 0)
            return;

        ModSaveData.ReadData(ref Data);

        Data ??= new ModSmeltingData();
        Data.InvData ??= new Dictionary<string, Inventory_Data>();
        RestoreInventory(InputInventory, InputInventorySaveKey);
        RestoreInventory(OutputInventory, OutputInventorySaveKey);
        RestoreInventory(FuelInventory, FuelInventorySaveKey);
    }

    /// <summary>恢复一个熔炉库存的数据引用。</summary>
    private void RestoreInventory(Inventory targetInventory, string key)
    {
        if (targetInventory == null || !Data.InvData.TryGetValue(key, out Inventory_Data savedData) ||
            savedData == null)
            return;

        targetInventory.Data = savedData;
    }

    private void OnDestroy()
    {
        ClosePanelAndClearTransferContext();
        CancelPanelDestroyCountdown();
        smeltingActor = null;
    }

    private void ClosePanelAndClearTransferContext()
    {
        currentInteractingPlayer = null;
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
        ModSaveData ??= new Ex_ModData_MemoryPackable();
        ModSaveData.Name = ModText.Furnace;
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
        Data.SmeltingProgress +=
            Data.SmeltingSpeed *
            GameDifficultyService.Current.Production.SmeltingSpeedMultiplier *
            deltaTime;

        // 消耗燃料
        mod_Fuel?.ConsumeFuel(deltaTime);

        // 熔炼完成
        bool treated = false;
        foreach (IInventoryHeatTreatment treatment in heatTreatments)
            treated |= treatment.ProcessHeat(InputInventory, OutputInventory, Data.Temperature, deltaTime);
        if (treated)
        {
            Data.SmeltingProgress = 0f;
            return;
        }

        // 熔炼完成
        if (Data.SmeltingProgress >= 100f)
        {
            Data.SmeltingProgress = 0f;
            CompleteSmelting();
        }
    }

    /// <summary>先匹配完整材料，再检查温度及全部产物空间；一次事务同时扣料和出货。</summary>
    public void CompleteSmelting()
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority) return;
        var capabilities = new CraftingCapabilities { RecipeType = RecipeType.Smelting, AllowCompactGrid = true };
        if (!CraftingRecipeMatcher.TryMatch(InputInventory, capabilities, out CraftingRecipeMatch match, out _))
            return;
        RuntimeRecipe recipe = match.Recipe;
        if (Data.Temperature < recipe.Temperature) return;
        CraftingResult description = CraftingService.DescribeRecipe(recipe);
        if (!description.Success) return;
        IReadOnlyList<ItemData> outputs = description.Outputs;
        if (Data.Temperature > recipe.Temperature_Max)
        {
            ItemData charred = GameRes.Instance.CreateItemData("CharredMatter");
            if (charred == null) return;
            charred.Stack.Amount = 1;
            outputs = new[] { charred };
        }
        if (!CraftingOutputRules.Prepare(InputInventory, match, outputs, out _) ||
            !CraftingTransaction.TryCreate(InputInventory, OutputInventory, match, outputs, false,
                out CraftingTransaction transaction, out _) || !transaction.Commit(out _)) return;
        try
        {
            if (Data.Temperature <= recipe.Temperature_Max) RecipeActionRunner.Execute(recipe, InputInventory);
            transaction.Complete();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        PublishSmeltingSuccess(outputs);
    }

    /// <summary>
    /// 由服务器事务或自动化场景代表玩家提交一次完整熔炼；不打开 UI，也不绕过配方、温度、材料与空间校验。
    /// </summary>
    public void CompleteSmelting(Player actor)
    {
        if (actor != null)
            smeltingActor = actor;

        CompleteSmelting();
    }
    #endregion

    #region 熔炼反馈
    /// <summary>全部产物提交成功后，按产物逐项发布权威熔炼进度。</summary>
    private void PublishSmeltingSuccess(IReadOnlyList<ItemData> outputItems)
    {
        Player actor = smeltingActor ?? currentInteractingPlayer;
        if (actor == null || outputItems == null)
            return;

        for (int index = 0; index < outputItems.Count; index++)
        {
            ItemData output = outputItems[index];
            GameplayProgressEvents.PublishSmeltSucceeded(
                actor,
                output?.IDName,
                output?.Stack?.Amount ?? 1f);
        }
    }

    /// <summary>刷新熔炉温度、燃料和进度显示。</summary>
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
        smeltingActor = currentInteractingPlayer;

        // 点燃燃料模块
        mod_Fuel?.SetIgnited(true);
        if (mod_Fuel != null && mod_Fuel.GetIgnitedState())
        {
            GameplayProgressEvents.PublishFurnaceIgnited(
                currentInteractingPlayer,
                item?.itemData?.IDName);
        }
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
