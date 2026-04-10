using AYellowpaper.SerializedCollections;
using DG.Tweening;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
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

    #region 数据定义
    [ShowInInspector]
    public ModData_FoodData FoodModData = new ModData_FoodData();
    public override ModuleData _Data
    {
        get => FoodModData;
        set => FoodModData = ModData_FoodData.FromModuleData(value);
    }

    [FoldoutGroup("食物数据")]
    [ShowInInspector]
    [LabelText("食物模块数据")]
    public Food Data
    {
        get
        {
            FoodModData ??= new ModData_FoodData();
            return FoodModData.EnsureFoodData();
        }
        set
        {
            FoodModData ??= new ModData_FoodData();
            FoodModData.SyncFromFood(value);
            FoodModData.ApplyToFoodData();
        }
    }

    [FoldoutGroup("运行时")]
    [ReadOnly]
    [LabelText("进食进度")]
    public float EatingProgress = 0;

    [FoldoutGroup("运行时")]
    [LabelText("数据更新事件")]
    public UltEvent DataUpdate = new UltEvent();

    [FoldoutGroup("UI")]
    [LabelText("面板预制体")]
    public GameObject PanelPrefab;

    [FoldoutGroup("UI")]
    [ReadOnly]
    [LabelText("面板实例")]
    public GameObject PanleInstance;

    [FoldoutGroup("UI")]
    [ReadOnly]
    [LabelText("面板控制器")]
    public BasePanel panelUI; // 替换UI_FloatData_Slider为BasePanel

    [FoldoutGroup("观察者")]
    [LabelText("逻辑观察者")]
    [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true, DraggableItems = true, ShowPaging = false)]
    [SerializeReference]
    public List<ModuleObserverBase> observers = new List<ModuleObserverBase>();

    #endregion

    #region 生命周期方法
    public override void Awake()
    {
        ModData_FoodData.SharedCreateTargetItemData = CreateSpoilageTargetItemData;

        if (_Data.ID == "")
        {
            _Data.ID = ModText.Food;
        }
    }

    public override void Load()
    {
        FoodModData ??= new ModData_FoodData();
        FoodModData.ApplyToFoodData();

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

        FoodModData ??= new ModData_FoodData();
        FoodModData.SyncFromFood(Data);
        FoodModData.ApplyToFoodData();
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

        UpdateNutritionDataText("碳水", Data.nutrition.Carbohydrates, Data.nutrition.Max_Carbohydrates);
        UpdateNutritionDataText("脂肪", Data.nutrition.Fat, Data.nutrition.Max_Fat);
        UpdateNutritionDataText("蛋白质", Data.nutrition.Protein, Data.nutrition.Max_Protein);
        UpdateNutritionDataText("水", Data.nutrition.Water, Data.nutrition.Max_Water);
        UpdateNutritionDataText("维生素", Data.nutrition.Vitamins, Data.nutrition.Max_Vitamins);
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

    /// <summary>
    /// 更新营养值文本显示（当前值/最大值）
    /// </summary>
    private void UpdateNutritionDataText(string nutritionName, float currentValue, float maxValue)
    {
        var dataText = panelUI.GetText($"DataText_{nutritionName}");
        if (dataText != null)
        {
                int currentInt = Mathf.RoundToInt(currentValue);
                int maxInt = Mathf.RoundToInt(maxValue);
                dataText.text = $"{currentInt}/{maxInt}";
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

    public static ItemData CreateSpoilageTargetItemData(string targetItemID)
    {
        if (string.IsNullOrWhiteSpace(targetItemID))
        {
            return null;
        }

        if (GameRes.Instance == null)
        {
            Debug.LogError($"[Mod_Food] 构建腐败产物失败，GameRes.Instance为空，目标ID={targetItemID}");
            return null;
        }

        GameObject targetPrefab = GameRes.Instance.GetPrefab(targetItemID);
        if (targetPrefab == null)
        {
            Debug.LogError($"[Mod_Food] 构建腐败产物失败，目标预制体不存在，目标ID={targetItemID}");
            return null;
        }

        Item targetItem = targetPrefab.GetComponent<Item>();
        if (targetItem == null || targetItem.itemData == null)
        {
            Debug.LogError($"[Mod_Food] 构建腐败产物失败，目标预制体缺少Item或itemData，目标ID={targetItemID}");
            return null;
        }

        ItemData clonedData = FastCloner.FastCloner.DeepClone(targetItem.itemData);
        if (clonedData == null || clonedData.Stack == null)
        {
            Debug.LogError($"[Mod_Food] 构建腐败产物失败，目标物品克隆失败，目标ID={targetItemID}");
            return null;
        }

        return clonedData;
    }

    #endregion

}