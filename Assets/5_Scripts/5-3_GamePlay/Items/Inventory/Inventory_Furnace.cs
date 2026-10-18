using System;
using System.Collections.Generic;
using MemoryPack;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Inventory_Furnace : Inventory
{

   public Mod_Inventory mod_Inventory;

    [Tooltip("输入容器，用于存放合成所需的原材料物品")]
    public Inventory InputInventory => mod_Inventory.InventoryInstances[0];
    [Tooltip("输出容器，用于存放合成后得到的物品")]
    public Inventory OutputInventory => mod_Inventory.InventoryInstances[1];
    [Tooltip("燃料容器，用于存放熔炉所需的燃料物品")]
    public Inventory FuelInventory => mod_Inventory.InventoryInstances[2];

    #region 序列化字段与引用
    public ModSmeltingData _Data = new ModSmeltingData();

    public Mod_Fuel mod_Fuel; // 燃料模块
    public float HeatingRatePercent => item?.itemMods?.GetMod_ByID<Mod_Furnace>(ModText.Furnace)?.heatingRatePercent ?? 100f;
    public Button WorkButton;

    [Header("UI组件")]
    [Tooltip("燃料容量条")]
    public Slider fuelSlider;
    [Tooltip("温度显示条")]
    public Slider temperatureSlider;
    [Tooltip("按百位截断后的粗略炉温文本，不显示十位和个位")]
    public TextMeshProUGUI TemperatureText;
    #endregion      
    #region Unity生命周期

    public override void OnValidate()
    {
        if (string.IsNullOrEmpty(Data.Name))
            Data.Name = ModText.Furnace;
    }

    public override void ModUpdate(float deltaTime)
    {
        base.ModUpdate(deltaTime);

        if (_Data.IsSmelting) // 已经处于熔炼状态
        {
            // 检查燃料模块是否处于点燃状态
            if (mod_Fuel.GetIgnitedState())
            {
                HeatMaterials(deltaTime);
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
                            mod_Fuel.AddFuel(fuel.Fuel.x);

                            // 点燃燃料
                            mod_Fuel.SetIgnited(true);

                            // 温度上限取决于燃料
                            _Data.MaxTemperature = fuel.MaxTemperature;
                        }

                        HeatMaterials(deltaTime); // 继续熔炼
                    }
                    else
                    {
                        // 真正燃料耗尽 → 停止熔炼
                        _Data.IsSmelting = false;
                        Debug.Log("燃料耗尽，熔炼停止！");
                    }
                }
                else
                {
                    // 真正燃料耗尽 → 停止熔炼
                    _Data.IsSmelting = false;
                    Debug.Log("燃料耗尽，熔炼停止！");
                }
            }
        }
        else
        {
            // 熄火后读取真实宿主环境，使用与普通物品相同的温差百分比自然传热。
            if (TryGetAmbientTemperature(out float ambientTemperature))
                _Data.Temperature = ThermalRuntime.AdvanceAmbient(_Data.Temperature, ambientTemperature, deltaTime);
            TransferMaterialHeat(deltaTime);

            // 如果燃料模块是点燃的，让它也熄灭
            if (mod_Fuel.GetIgnitedState())
            {
                mod_Fuel.SetIgnited(false);
            }
        }

        // 同步所有UI
        UpdateUI();
    }

    private bool TryGetAmbientTemperature(out float temperature)
    {
        if (MachineOwner != null)
        {
            temperature = MachineWorld.SampleAmbientTemperature(MachineOwner);
            return true;
        }
        Item owner = item != null && item.Owner != null ? item.Owner : item;
        if (owner != null)
        {
            if (ItemEnvironmentSources.TryGet(owner, out ItemEnvironmentSample localEnvironment))
            {
                temperature = (float)localEnvironment.TemperatureCelsius;
                return true;
            }
            if (TemperatureMgr.Instance != null &&
                TemperatureMgr.Instance.TryGetAmbientTemperature(owner.transform.position, out temperature)) return true;
        }
        temperature = default;
        return false;
    }

    public override void InitData()
    {
        base.InitData();
        mod_Fuel = item.itemMods.GetMod_ByID<Mod_Fuel>(ModText.Fuel);

        // 如果有手持模块，设置默认目标
        if (item != null && item.itemMods != null && item.itemMods.ContainsKey_ID(ModText.Hand))
        {
            var handInv = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>().GetDefaultTargetInventory();
            if (InputInventory != null)
                InputInventory.DefaultTarget_Inventory = handInv;
            if (OutputInventory != null)
                OutputInventory.DefaultTarget_Inventory = handInv;
        }

    }

    /// <summary>
    /// UI初始化（在面板创建后调用）
    /// </summary>
    public override void InitUI()
    {
        InputInventory.itemSlot_UI.Clear();
        OutputInventory.itemSlot_UI.Clear();
        FuelInventory.itemSlot_UI.Clear();

        InputInventory.itemSlot_UI.Add(basePanel.GetButton("输入_1").GetComponent<ItemSlot_UI>());
        InputInventory.itemSlot_UI.Add(basePanel.GetButton("输入_2").GetComponent<ItemSlot_UI>());
        InputInventory.itemSlot_UI.Add(basePanel.GetButton("输入_3").GetComponent<ItemSlot_UI>());

        OutputInventory.itemSlot_UI.Add(basePanel.GetButton("输出_1").GetComponent<ItemSlot_UI>());
        OutputInventory.itemSlot_UI.Add(basePanel.GetButton("输出_2").GetComponent<ItemSlot_UI>());
        OutputInventory.itemSlot_UI.Add(basePanel.GetButton("输出_3").GetComponent<ItemSlot_UI>());

        FuelInventory.itemSlot_UI.Add(basePanel.GetButton("燃料_1").GetComponent<ItemSlot_UI>());
        FuelInventory.itemSlot_UI.Add(basePanel.GetButton("燃料_2").GetComponent<ItemSlot_UI>());
        FuelInventory.itemSlot_UI.Add(basePanel.GetButton("燃料_3").GetComponent<ItemSlot_UI>());
        // 同步 UI 数据
        InputInventory.SyncData();
        OutputInventory.SyncData();
        FuelInventory.SyncData();

        // 初始化UI引用
        temperatureSlider = null; // 炉温只显示百位粗略值，不提供连续精确刻度。
        TemperatureText = base.basePanel.GetText("FWUI_FurnaceTemperatureValue");
        fuelSlider = base.basePanel.GetSlider("燃料显示条");
        WorkButton = base.basePanel.GetButton("合成按钮");

        // 按钮事件
        WorkButton.onClick.AddListener(OnButtonClick);

        // 初始化UI显示
        basePanel?.Close();
        UpdateUI();
        InputInventory?.RefreshUI();
        OutputInventory?.RefreshUI();
        FuelInventory?.RefreshUI();
    }

    public override void Save()
    {

    }
    /// <summary>
    /// 玩家开始交互
    /// </summary>
    public override void Interact_Start(Item playerItem)
    {
        if (playerItem.itemMods.GetMod_ByID(ModText.Hand, out Mod_Inventory handMod))
        {
            InputInventory.DefaultTarget_Inventory = handMod.inventory;
            OutputInventory.DefaultTarget_Inventory = handMod.inventory;
            FuelInventory.DefaultTarget_Inventory = handMod.inventory;
        }
        SwitchUI();
        InputInventory.SyncQuickTransferTarget(basePanel);
        Debug.Log($"玩家 {playerItem.name} 开始交互工作台");
    }
    #endregion


    #region 材料供热
    private void HeatMaterials(float deltaTime)
    {
        bool hasMaterials = InputInventory.Data.itemSlots.Exists(slot => slot?.itemData != null) ||
            OutputInventory.Data.itemSlots.Exists(slot => slot?.itemData != null);
        float maximum = _Data.MaxTemperature > 0f ? Mathf.Min(_Data.MaxTemperature, _Data.MaxTemperatureLimit) : _Data.MaxTemperatureLimit;
        _Data.Temperature = Mathf.Min(_Data.Temperature + _Data.TemperatureUpSpeed *
            (hasMaterials ? 1f : 2f) * deltaTime, maximum);
        TransferMaterialHeat(deltaTime);
        mod_Fuel?.ConsumeFuel(deltaTime);
    }

    private float GetInitialMaterialTemperature()
    {
        if (MachineOwner != null) return FurnaceMaterialHeating.SampleInitialMaterialTemperature(MachineOwner);
        Item owner = item != null && item.Owner != null ? item.Owner : item;
        if (owner != null)
        {
            if (ItemEnvironmentSources.TryGet(owner, out ItemEnvironmentSample localEnvironment))
                return (float)localEnvironment.TemperatureCelsius;
            if (TemperatureMgr.Instance != null &&
                TemperatureMgr.Instance.TryGetClimateBaseline(owner.transform.position, out float baseline)) return baseline;
        }
        return TryGetAmbientTemperature(out float ambient) ? ambient : TemperatureMgr.DefaultAmbientTemperature;
    }

    private void TransferMaterialHeat(float seconds)
    {
        float initialTemperature = GetInitialMaterialTemperature();
        FurnaceMaterialHeating.Process(InputInventory, OutputInventory, _Data.Temperature, seconds,
            HeatingRatePercent / 100f, initialMaterialTemperature: initialTemperature);
    }
    #endregion

    #region UI与交互处理
    private void UpdateUI()
    {
        // 燃料条
        if (fuelSlider != null && mod_Fuel != null && mod_Fuel.Data != null)
            fuelSlider.value = mod_Fuel.Data.Fuel.y > 0 ? mod_Fuel.Data.Fuel.x / mod_Fuel.Data.Fuel.y : 0;

        // 炉温只保留百位精度，不暴露十位、个位与炉体上限。
        if (TemperatureText != null)
            TemperatureText.text = FurnaceTemperatureFeedback.GetDisplayedTemperature(_Data.Temperature);
    }

    private void OnButtonClick()
    {
        // 安全检查
        if (FuelInventory == null || FuelInventory.Data == null)
        {
            Debug.LogWarning("燃料库存未初始化！");
            return;
        }

        // 在燃料栏中查找包含 Ignition 标签的物品
        var ignitionItem = FuelInventory.Data.FindFirstByTag("Ignition");
        if (ignitionItem == null)
        {
            Debug.LogWarning("无法点燃：缺少点火装置！");
            return;
        }

        // 如果已经在熔炼中，不允许主动停止
        if (_Data.IsSmelting)
        {
            Debug.Log("熔炼已经开始，无法主动停止。只有燃料耗尽时才会停止。");
            return;
        }

        // 开始熔炼
        _Data.IsSmelting = true;

        // 设置默认最大温度为熔炉限制温度（如果还没有燃料提供温度的话）
        if (_Data.MaxTemperature <= 0)
        {
            _Data.MaxTemperature = _Data.MaxTemperatureLimit;
        }

        // 点燃燃料模块
        mod_Fuel?.SetIgnited(true);
        Debug.Log("熔炉已点燃并开始熔炼！");
    }
    #endregion

    #region 燃烧状态控制
    /// <summary>
    /// 设置燃烧状态
    /// </summary>
    /// <param name="isBurning">是否燃烧</param>
    public void SetBurningState(bool isBurning)
    {
        _Data.IsSmelting = isBurning;
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
        return _Data.IsSmelting && (mod_Fuel?.GetIgnitedState() ?? false);
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
#region 数据定义
[MemoryPackable]
[System.Serializable]
public partial class ModSmeltingData
{
    [ShowInInspector]
    public Dictionary<string, Inventory_Data> InvData = new Dictionary<string, Inventory_Data>();

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

    [Tooltip("按副产物规则 id 保存的燃料累计消费数。")]
    public Dictionary<string, int> FuelByproductProgress = new Dictionary<string, int>();

    [Tooltip("按副产物规则 id 保存的待交付数量，避免目标库存满时丢失产出。")]
    public Dictionary<string, int> PendingFuelByproductCount = new Dictionary<string, int>();
}
#endregion
