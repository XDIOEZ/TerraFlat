using AYellowpaper.SerializedCollections;
using DG.Tweening;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UltEvents;

public partial class Mod_Food : Module
{
    [MemoryPackable]
    public partial class ObserverSnapshot
    {
        public string TypeName;
        public byte[] Payload;
    }

    [System.Serializable]
    public class FoodSpoilageSettings
    {
        [Tooltip("是否启用食物腐败")]
        public bool EnableSpoilage = true;

        [Tooltip("腐败扫描间隔（秒）")]
        [MinValue(0.1f)]
        public float TickIntervalSeconds = 1f;

        [Tooltip("容器默认腐败倍率")]
        [MinValue(0f)]
        public float DefaultContainerRate = 1f;

        [Tooltip("默认腐败间隔（秒）")]
        [MinValue(1f)]
        public float DefaultSpoilageIntervalSeconds = 1800f;

        [Tooltip("默认腐败目标物品ID")]
        public string DefaultSpoilageTargetItemID = "RottenFood";

        [Tooltip("按容器名配置腐败倍率")]
        public SerializedDictionary<string, float> ContainerRateByInventoryName = new();

        [Tooltip("按物品ID配置腐败间隔（秒）")]
        public SerializedDictionary<string, float> SpoilageIntervalByItemID = new();

        [Tooltip("按物品ID配置腐败目标物品ID")]
        public SerializedDictionary<string, string> SpoilageTargetByItemID = new();

        public void EnsureDefaults()
        {
            if (TickIntervalSeconds <= 0f)
            {
                TickIntervalSeconds = 1f;
            }

            if (DefaultContainerRate < 0f)
            {
                DefaultContainerRate = 0f;
            }

            if (DefaultSpoilageIntervalSeconds <= 0f)
            {
                DefaultSpoilageIntervalSeconds = 1800f;
            }

            if (string.IsNullOrWhiteSpace(DefaultSpoilageTargetItemID))
            {
                DefaultSpoilageTargetItemID = "RottenFood";
            }

            ContainerRateByInventoryName ??= new SerializedDictionary<string, float>();
            SpoilageIntervalByItemID ??= new SerializedDictionary<string, float>();
            SpoilageTargetByItemID ??= new SerializedDictionary<string, string>();
        }

        public float ResolveContainerRate(string inventoryName)
        {
            if (!string.IsNullOrWhiteSpace(inventoryName) &&
                ContainerRateByInventoryName != null &&
                ContainerRateByInventoryName.TryGetValue(inventoryName, out float rate))
            {
                return Mathf.Max(0f, rate);
            }

            return Mathf.Max(0f, DefaultContainerRate);
        }

        public float ResolveSpoilageInterval(string itemID, float moduleInterval)
        {
            if (!string.IsNullOrWhiteSpace(itemID) &&
                SpoilageIntervalByItemID != null &&
                SpoilageIntervalByItemID.TryGetValue(itemID, out float interval))
            {
                return Mathf.Max(0f, interval);
            }

            if (moduleInterval > 0f)
            {
                return moduleInterval;
            }

            return Mathf.Max(0f, DefaultSpoilageIntervalSeconds);
        }

        public string ResolveSpoilageTarget(string itemID, string moduleTarget)
        {
            if (!string.IsNullOrWhiteSpace(itemID) &&
                SpoilageTargetByItemID != null &&
                SpoilageTargetByItemID.TryGetValue(itemID, out string targetID) &&
                !string.IsNullOrWhiteSpace(targetID))
            {
                return targetID;
            }

            if (!string.IsNullOrWhiteSpace(moduleTarget))
            {
                return moduleTarget;
            }

            return DefaultSpoilageTargetItemID;
        }
    }

    #region 数据定义
    public Ex_ModData_MemoryPackable ExData;
    public override ModuleData _Data { get => ExData; set => ExData = (Ex_ModData_MemoryPackable)value; }

    public Food Data = new Food();

    public float EatingProgress = 0;

    public UltEvent DataUpdate = new UltEvent();

    public GameObject PanelPrefab;
    [ReadOnly]
    public GameObject PanleInstance;
    [ReadOnly]
    public BasePanel panelUI; // 替换UI_FloatData_Slider为BasePanel

    [SerializeReference]
    public List<ModuleObserverBase> observers = new List<ModuleObserverBase>();

    private static readonly Dictionary<int, float> _inventorySpoilageTickTimerById = new Dictionary<int, float>();

    #endregion

    #region 生命周期方法
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Food;
        }
    }

    public override void Load()
    {
        ExData.ReadData(ref Data);

        item.itemMods.GetMod_ByID(ModText.Controller, out GameController Controller);
        if (Controller != null)
        {
            Controller._inputActions.Win10.Tab.performed += _ => TogglePanel();
        }

        EnsureObservers();
        ApplyObserverState();

        // 根据保存的状态决定是否显示面板
        if (Data.ShowCanvas)
        {
            ShowPanel();
        }

        if (item != null)
        {
            item.OnAct += Act;
        }

    }
    public override void Save()
    {
        Data.ObserverState = BuildObserverState();

        if (item != null)
        {
            item.OnAct -= Act;
        }

        // 保存面板位置
        if (PanleInstance != null)
        {
            SavePanelPosition();
        }

        // 终止所有与该对象相关的 tween
        DOTween.Kill(item?.transform);

        ExData.WriteData(Data);
    }
    /// <summary>
    /// 调用吃的行为
    /// </summary>
    public override void Act()
    {
        var Player_FoodModule = item.Owner.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
        Player_FoodModule.Eat(BeEater: this);
    }

    public override void ModUpdate(float timeDelta)
    {

        // 驱动所有逻辑插件观察者
        foreach (var observer in observers)
        {
            observer.OnUpdate(timeDelta);
        }

        // 营养消耗
        ConsumeNutrition(timeDelta * Data.nutritionConsumeRate);

        DataUpdate?.Invoke();
    }
    #endregion

    #region 营养管理
    public float ConsumeNutrition(float timeDelta)
    {
        // 移除对精力状态的检查，不再根据精力是否满来调整消耗速度
        // 始终保持恒定的消耗速度

        // 计算本次消耗总量 = 时间增量 * 吸收率 * 消耗速度
        float delta = timeDelta * Data.nutritionConsumeSpeed.Value;
        float remainingDelta = delta;
        float totalEnergy = 0f;

        // 优先消耗碳水化合物，不能超过当前碳水量
        float usedCarb = Mathf.Min(Data.nutrition.Carbohydrates, remainingDelta);
        remainingDelta -= usedCarb;
        Data.nutrition.Carbohydrates -= usedCarb;
        totalEnergy += usedCarb;

        float usedFat = 0f;
        float usedProtein = 0f;
        float usedWater = 0f;

        // 消耗剩余量部分用于脂肪，不能超过当前脂肪量
        if (remainingDelta > 0)
        {
            usedFat = Mathf.Min(Data.nutrition.Fat, remainingDelta);
            remainingDelta -= usedFat;
            Data.nutrition.Fat -= usedFat;
            totalEnergy += usedFat;
        }

        // 消耗剩余量部分用于蛋白质，不能超过当前蛋白质量
        if (remainingDelta > 0)
        {
            usedProtein = Mathf.Min(Data.nutrition.Protein, remainingDelta);
            remainingDelta -= usedProtein;
            Data.nutrition.Protein -= usedProtein;
            totalEnergy += usedProtein;
        }

        // 水的消耗是持续性的，且消耗速度受当前消耗物质影响
        // 消耗碳水时水消耗速率为1，脂肪为2，蛋白质为3
        usedWater = usedCarb * 1f + usedFat * 2f + usedProtein * 3f;
        usedWater *= Data.WaterConsumeSpeedRate;
        // 扣除相应的水分，水分不会低于0
        Data.nutrition.Water = Mathf.Max(0, Data.nutrition.Water - usedWater);

        // 维生素自然消耗，速度为0.01倍时间增量
        float naturalVitaminLoss = timeDelta * 0.01f;
        Data.nutrition.Vitamins = Mathf.Max(0, Data.nutrition.Vitamins - naturalVitaminLoss);

        // 返回本次消耗的总能量值
        return totalEnergy;
    }
    #endregion

    #region 面板管理
    [Button("显示面板")]
    public void ShowPanel()
    {
        if (PanleInstance != null) return;

        // 实例化操作已移动到TogglePanel方法中
        TogglePanel();
    }



    [Button("隐藏面板")]
    public void HidePanel()
    {
        if (PanleInstance == null) return;
        panelUI.Close();
    }

    /// <summary>
    /// 保存面板位置
    /// </summary>
    private void SavePanelPosition()
    {
        // 优先从拖拽组件获取位置
        var dragComponent = PanleInstance.GetComponentInChildren<UI_Drag>();
        if (dragComponent != null)
        {
            Data.PanelPosition = dragComponent.rectTransform.anchoredPosition;
        }
        else
        {
            // 如果没有拖拽组件，从面板本身获取位置
            var panelRectTransform = PanleInstance.GetComponent<RectTransform>();
            if (panelRectTransform != null)
            {
                Data.PanelPosition = panelRectTransform.anchoredPosition;
            }
        }
    }

    /// <summary>
    /// 切换面板显示状态
    /// </summary>
    [Button("切换面板")]
    public void TogglePanel()
    {
        // 检测面板是否已经实例化，如果未实例化则先实例化
        if (PanleInstance == null)
        {
            panelUI = UIManager.Instance.CreatePanelFromGameObject(PanelPrefab);
            PanleInstance = panelUI.gameObject;
            DataUpdate += RefreshUI;
            RefreshUI();
            panelUI.Open();
            return;
        }

        // 根据当前面板状态进行切换
        if (panelUI.IsOpen())
        {
            HidePanel();
        }
        else
        {
            // 设置面板为打开状态
            panelUI.Open();
            Data.ShowCanvas = true;
        }
    }



    #endregion

    #region UI更新
    [Button("刷新面板")]
    public void RefreshUI()
    {
        if (panelUI == null) return;

        UpdateNutritionSlider("碳水", Data.nutrition.Carbohydrates, Data.nutrition.Max_Carbohydrates);
        UpdateNutritionSlider("脂肪", Data.nutrition.Fat, Data.nutrition.Max_Fat);
        UpdateNutritionSlider("蛋白质", Data.nutrition.Protein, Data.nutrition.Max_Protein);
        UpdateNutritionSlider("水", Data.nutrition.Water, Data.nutrition.Max_Water);
        UpdateNutritionSlider("维生素", Data.nutrition.Vitamins, Data.nutrition.Max_Vitamins);
    }

    /// <summary>
    /// 更新营养值滑块显示
    /// </summary>
    private void UpdateNutritionSlider(string sliderName, float currentValue, float maxValue)
    {
        var slider = panelUI.GetSlider(sliderName);
        if (slider != null)
        {
            slider.maxValue = maxValue;
            slider.value = currentValue;
        }
    }
    #endregion

    #region 进食行为
    public void BeEat(Mod_Food Eater)
    {
        ShakeItem(item.transform);

        EatingProgress++;

        if (EatingProgress >= Data.Max_EatingProgress)
        {
            // 减少堆叠数量
            item.itemData.Stack.Amount--;
            // UI 更新通知
            item.OnUIRefresh?.Invoke();
            // 进度归零
            EatingProgress = 0;

            Eater.Data.nutrition = Eater.Data.nutrition + Data.nutrition;

            DataUpdate.Invoke();

            if (item.itemData.Stack.Amount <= 0)
            {
                Destroy(item.gameObject); // 吃完销毁
            }
        }
    }

    public void Eat(Mod_Food BeEater)
    {
        ShakeItem(BeEater.item.transform);  // 播放摇晃动画或者其他视觉效果

        BeEater.EatingProgress++;  // 更新被吃食物的进度

        if (BeEater.EatingProgress >= BeEater.Data.Max_EatingProgress)
        {
            // 减少被吃食物的堆叠数量
            BeEater.item.itemData.Stack.Amount--;
            // UI 更新通知
            BeEater.item.OnUIRefresh?.Invoke();

            BeEater.EatingProgress = 0; // 吃进度归零

            // 吃掉目标食物的营养值
            Data.nutrition = Data.nutrition + BeEater.Data.nutrition;

            DataUpdate.Invoke();  // 通知数据更新

            // 如果被吃食物的堆叠数量为 0，销毁该食物
            if (BeEater.item.itemData.Stack.Amount <= 0)
            {
                Destroy(BeEater.item.gameObject);  // 销毁被吃的食物
            }
            else
            {

            }
        }
    }
    #endregion

    #region 代码动画
    [Button("抖动")]
    void ShakeItem(Transform transform, float duration = 0.2f, float strength = 0.2f, int vibrato = 0)
    {
        if (vibrato == 0)
        {
            //产生一个随机的抖动偏移量
            vibrato = UnityEngine.Random.Range(15, 30);
        }
        // 用 DOTween 做局部抖动
        transform.DOShakePosition(duration, strength, vibrato).SetEase(Ease.OutQuad);

        // 调用封装后的粒子创建方法
        CreateMainColorParticle(transform, "Particle_BeEat");
    }
    private GameObject CreateMainColorParticle(UnityEngine.Transform targetTransform, string prefabName)
    {
        SpriteRenderer sr = targetTransform.GetComponentInChildren<SpriteRenderer>();

        if (sr != null && sr.sprite != null)
        {
            GameObject particle = GameRes.Instance.InstantiatePrefab(prefabName, targetTransform.position);
            ParticleSystem ps = particle.GetComponent<ParticleSystem>();

            if (ps != null)
            {
                // 检查纹理是否可读写
                Texture2D texture = sr.sprite.texture;
                if (texture != null && texture.isReadable)
                {
                    // 如果纹理可读取，则获取主色调
                    var dominant = new ColorThief.ColorThief();
                    UnityEngine.Color mainColor = dominant.GetColor(texture).UnityColor;
                    var main = ps.main;
                    main.startColor = mainColor;
                }
                else
                {
                    // 如果纹理不可读取，则使用默认白色
                    var main = ps.main;
                    main.startColor = Color.white;
                }
            }

            return particle;
        }

        return null;
    }

    #endregion



    #region 工具方法

    private void EnsureObservers()
    {
        foreach (var observer in observers)
        {
            observer.OnInit(this);
        }
    }

    private void ApplyObserverState()
    {
        if (Data.ObserverState == null || Data.ObserverState.Length == 0)
        {
            return;
        }

        var snapshots = MemoryPack.MemoryPackSerializer.Deserialize<List<ObserverSnapshot>>(Data.ObserverState);
        if (snapshots == null || snapshots.Count == 0)
        {
            return;
        }

        var map = new Dictionary<string, byte[]>();
        foreach (var snapshot in snapshots)
        {
            if (snapshot?.TypeName != null)
            {
                map[snapshot.TypeName] = snapshot.Payload;
            }
        }

        foreach (var observer in observers)
        {
            var key = observer.GetType().FullName;
            if (key != null && map.TryGetValue(key, out var payload))
            {
                observer.OnLoad(payload);
            }
        }
    }

    private byte[] BuildObserverState()
    {
        var snapshots = new List<ObserverSnapshot>(observers.Count);
        foreach (var observer in observers)
        {
            var payload = observer.OnSave(this);
            snapshots.Add(new ObserverSnapshot
            {
                TypeName = observer.GetType().FullName,
                Payload = payload
            });
        }

        return MemoryPack.MemoryPackSerializer.Serialize(snapshots);
    }

    private void AddObserver(ModuleObserverBase observer)
    {
        if (observer == null)
        {
            return;
        }

        if (!observers.Contains(observer))
        {
            observers.Add(observer);
        }
    }

    private bool TryGetObserver<T>(out T observer) where T : ModuleObserverBase
    {
        observer = null;

        foreach (var o in observers)
        {
            if (o is T typed)
            {
                observer = typed;
                return true;
            }
        }

        return false;
    }

    #endregion

    #region 食物腐败

    public static void EnsureInventorySpoilageConfig(Inventory inventory)
    {
        if (inventory == null)
        {
            throw new ArgumentNullException(nameof(inventory));
        }

        if (inventory.FoodSpoilageSettings == null)
        {
            inventory.FoodSpoilageSettings = new FoodSpoilageSettings();
        }

        inventory.FoodSpoilageSettings.EnsureDefaults();
        EnsureInventorySpoilageRate(inventory.FoodSpoilageSettings, ModText.Bag, 1f);
        EnsureInventorySpoilageRate(inventory.FoodSpoilageSettings, ModText.Hand, 1f);
        EnsureInventorySpoilageRate(inventory.FoodSpoilageSettings, ModText.Hotbar, 1f);
    }

    public static void ResetInventorySpoilageRuntime(Inventory inventory)
    {
        if (inventory == null)
        {
            return;
        }

        int inventoryId = RuntimeHelpers.GetHashCode(inventory);
        _inventorySpoilageTickTimerById.Remove(inventoryId);
    }

    public static void UpdateInventorySpoilage(Inventory inventory, float deltaTime)
    {
        if (inventory == null || inventory.Data == null || inventory.Data.itemSlots == null || inventory.Data.itemSlots.Count == 0)
        {
            return;
        }

        EnsureInventorySpoilageConfig(inventory);
        FoodSpoilageSettings settings = inventory.FoodSpoilageSettings;
        if (!settings.EnableSpoilage)
        {
            return;
        }

        int inventoryId = RuntimeHelpers.GetHashCode(inventory);
        _inventorySpoilageTickTimerById.TryGetValue(inventoryId, out float tickTimer);
        tickTimer += deltaTime;
        if (tickTimer < settings.TickIntervalSeconds)
        {
            _inventorySpoilageTickTimerById[inventoryId] = tickTimer;
            return;
        }

        float spoilageDelta = tickTimer;
        _inventorySpoilageTickTimerById[inventoryId] = 0f;

        string inventoryName = string.IsNullOrWhiteSpace(inventory.Data.Name) ? ModText.Bag : inventory.Data.Name;
        float spoilageRate = settings.ResolveContainerRate(inventoryName);
        if (spoilageRate <= 0f)
        {
            return;
        }

        float scaledSpoilageDelta = spoilageDelta * spoilageRate;
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            ItemSlot slot = inventory.Data.itemSlots[i];
            if (TryProcessSpoilage(slot, scaledSpoilageDelta, settings, inventoryName, i))
            {
                slot.RefreshUI();
                inventory.Data.Event_RefreshUI.Invoke(i);
                inventory.Data.Event_OnDataChanged.Invoke(slot);
            }
        }
    }

    private static void EnsureInventorySpoilageRate(FoodSpoilageSettings settings, string inventoryName, float defaultRate)
    {
        if (settings == null || string.IsNullOrWhiteSpace(inventoryName))
        {
            return;
        }

        if (!settings.ContainerRateByInventoryName.ContainsKey(inventoryName))
        {
            settings.ContainerRateByInventoryName[inventoryName] = defaultRate;
        }
    }

    public static bool TryProcessSpoilage(ItemSlot slot, float spoilageDeltaSeconds, FoodSpoilageSettings settings, string inventoryName, int slotIndex)
    {
        if (slot == null || slot.itemData == null)
        {
            return false;
        }

        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.EnsureDefaults();
        if (!settings.EnableSpoilage)
        {
            return false;
        }

        ItemData itemData = slot.itemData;
        if (itemData.Stack == null)
        {
            Debug.LogError($"[Mod_Food] 腐败处理失败，Stack为空，容器={inventoryName}, 槽位={slotIndex}");
            return false;
        }

        if (itemData.Tags == null || !itemData.Tags.ContainsTag("Food"))
        {
            return false;
        }

        if (!TryGetFoodModuleData(itemData, out Ex_ModData_MemoryPackable foodModuleData))
        {
            return false;
        }

        Food foodData = null;
        foodModuleData.ReadData(ref foodData);
        if (foodData == null)
        {
            foodData = new Food();
        }

        if (!foodData.EnableSpoilage)
        {
            return false;
        }

        float intervalSeconds = settings.ResolveSpoilageInterval(itemData.IDName, foodData.SpoilageIntervalSeconds);
        if (intervalSeconds <= 0f)
        {
            return false;
        }

        string targetItemID = settings.ResolveSpoilageTarget(itemData.IDName, foodData.SpoilageTargetItemID);
        if (string.IsNullOrWhiteSpace(targetItemID))
        {
            Debug.LogError($"[Mod_Food] 腐败处理失败，目标物品ID为空，物品={itemData.IDName}, 容器={inventoryName}, 槽位={slotIndex}");
            foodData.SpoilageElapsedSeconds = 0f;
            foodModuleData.WriteData(foodData);
            return false;
        }

        if (itemData.IDName == targetItemID)
        {
            foodData.SpoilageElapsedSeconds = 0f;
            foodModuleData.WriteData(foodData);
            return false;
        }

        foodData.SpoilageElapsedSeconds += Mathf.Max(0f, spoilageDeltaSeconds);
        if (foodData.SpoilageElapsedSeconds < intervalSeconds)
        {
            foodModuleData.WriteData(foodData);
            return false;
        }

        if (!TryBuildSpoiledItemData(itemData, targetItemID, out ItemData spoiledData))
        {
            foodData.SpoilageElapsedSeconds = 0f;
            foodModuleData.WriteData(foodData);
            return false;
        }

        slot.itemData = spoiledData;
        Debug.Log($"[FoodSpoilage] 替换成功，容器={inventoryName}, 槽位={slotIndex}, 原物品={itemData.IDName}, 新物品={spoiledData.IDName}, 数量={itemData.Stack.Amount:F0}");
        return true;
    }

    private static bool TryGetFoodModuleData(ItemData itemData, out Ex_ModData_MemoryPackable foodModuleData)
    {
        foodModuleData = null;
        if (itemData == null || itemData.ModuleDataDic == null)
        {
            return false;
        }

        foreach (var moduleData in itemData.ModuleDataDic.Values)
        {
            if (moduleData == null || moduleData.ID != ModText.Food)
            {
                continue;
            }

            foodModuleData = moduleData as Ex_ModData_MemoryPackable;
            if (foodModuleData == null)
            {
                Debug.LogError($"[Mod_Food] 食物模块数据类型错误，物品={itemData.IDName}, 模块ID={moduleData.ID}");
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool TryBuildSpoiledItemData(ItemData sourceItemData, string targetItemID, out ItemData spoiledData)
    {
        spoiledData = null;

        if (GameRes.Instance == null)
        {
            Debug.LogError($"[Mod_Food] 食物腐败失败，GameRes.Instance为空，目标ID={targetItemID}");
            return false;
        }

        GameObject targetPrefab = GameRes.Instance.GetPrefab(targetItemID);
        if (targetPrefab == null)
        {
            Debug.LogError($"[Mod_Food] 食物腐败失败，目标预制体不存在，目标ID={targetItemID}");
            return false;
        }

        Item targetItem = targetPrefab.GetComponent<Item>();
        if (targetItem == null || targetItem.itemData == null)
        {
            Debug.LogError($"[Mod_Food] 食物腐败失败，目标预制体缺少Item或itemData，目标ID={targetItemID}");
            return false;
        }

        spoiledData = FastCloner.FastCloner.DeepClone(targetItem.itemData);
        if (spoiledData == null || spoiledData.Stack == null)
        {
            Debug.LogError($"[Mod_Food] 食物腐败失败，目标物品克隆失败，目标ID={targetItemID}");
            return false;
        }

        spoiledData.Stack.Amount = sourceItemData.Stack.Amount;
        spoiledData.Stack.CanBePickedUp = sourceItemData.Stack.CanBePickedUp;
        spoiledData.Guid = sourceItemData.Guid;
        spoiledData.transform = sourceItemData.transform;
        spoiledData.inHand = sourceItemData.inHand;
        return true;
    }

    #endregion

}