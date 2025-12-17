using DG.Tweening;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;
using UltEvents;

public partial class Mod_Food : Module
{
    #region 数据定义
    public Ex_ModData_MemoryPackable ExData;
    public override ModuleData _Data { get => ExData; set => ExData = (Ex_ModData_MemoryPackable)value; }

    public Mod_Food_Data Data = new Mod_Food_Data();

    public float EatingProgress = 0;

    public UltEvent DataUpdate = new UltEvent();

    public GameObject PanelPrefab;

    public GameObject PanleInstance;

    public BasePanel panelUI; // 替换UI_FloatData_Slider为BasePanel

    public Mod_Stamina Stamina;
    public DamageReceiver DamageReceiver;

    #region 数据结构
    [MemoryPackable]
    [System.Serializable]
    public partial class Mod_Food_Data
    {
        public Nutrition nutrition = new();//营养值
        public float Max_EatingProgress = 3;//最大进度
        public float AbsorptionRate = 1f;//吸收率
        public bool ShowCanvas = false;//面板显示状态
        public GameValue_float nutritionConsumeSpeed = new(1f);

        public bool FeelGood = false; // ← 加在这里
        // 添加子对象的面板位置作为持久化数据 在实例化面板时保存面板位置 在关闭面板时恢复面板位置 在Save函数中 如果面板存在 就保存面板的位置
        public Vector2 PanelPosition = new Vector2(0, 0);
        [Tooltip("状态良好时恢复血量的速度")]
        public float HealSpeed = 1f;
        [Tooltip("感觉蛋白质状态良好的阈值")]
        public float ProteinThreshold = 50f;
        [Tooltip("水份归零时，自己对自己造成的伤害(单位/秒)")]
        public float WaterSelfHurt = 1f;
        [Tooltip("蛋白质归零时，自己对自己造成的伤害(单位/秒)")]
        public float ProteinSelfHurt = 1f;
        [Tooltip("维生素出现亏欠时，对自己造成的伤害(单位/秒)")]
        public float VitaminSelfHurt = 1f;
        [Tooltip("精力恢复速度")]
        public float StaminaRecoverSpeed = 1f;
        [Tooltip("恢复精力时 饥饿消耗速度的额外增量(单位/倍率)")]
        public float StaminaConsumeSpeedRate = 0.5f;
        [Tooltip("水份消耗速度倍率")]
        public float WaterConsumeSpeedRate = 1f;
    }
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

        //模块引用赋值
        item.itemMods.GetMod_ByID(ModText.Stamina, out Stamina);
        item.itemMods.GetMod_ByID(ModText.Hp, out DamageReceiver);

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
    #endregion

    #region 核心逻辑
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
        float rate = 1f;
        
        // 精力补充 - 前提是存在精力模块
        if (Stamina != null && Stamina.Data.CurrentStamina < Stamina.Data.MaxStamina.Value)
        {
            rate += Data.StaminaConsumeSpeedRate;
            Stamina.AddStamina(Data.StaminaRecoverSpeed * timeDelta);
        }

        // 营养消耗
        ConsumeNutrition(timeDelta * rate);

        // 健康状态检测与处理
        if (DamageReceiver != null)
        {
            CheckNutritionStatus(timeDelta);
        }
        
        DataUpdate?.Invoke();
    }
    #endregion

    #region 营养管理
    /// <summary>
    /// 检测营养状态并处理相应的健康效果
    /// </summary>
    private void CheckNutritionStatus(float timeDelta)
    {
        // 检测蛋白质状态
        if (Data.nutrition.Protein <= 0)
        {
            // 蛋白质不足，造成伤害
            DamageReceiver.ForceHurt(Data.ProteinSelfHurt * timeDelta);
        }
        else if (Data.nutrition.Protein >= Data.ProteinThreshold)
        {
            // 蛋白质充足，恢复血量
            DamageReceiver.Heal(Data.HealSpeed * timeDelta, item);
        }

        // 检测水份状态
        if (Data.nutrition.Water <= 0)
        {
            // 水份不足，造成伤害
            DamageReceiver.ForceHurt(Data.WaterSelfHurt * timeDelta);
        }

        // 检测维生素状态
        if (Data.nutrition.Vitamins <= 0)
        {
            // 维生素不足，造成伤害
            DamageReceiver.ForceHurt(Data.VitaminSelfHurt * timeDelta);
        }
    }

    private float ConsumeNutrition(float timeDelta)
    {
        // 移除对精力状态的检查，不再根据精力是否满来调整消耗速度
        // 始终保持恒定的消耗速度
        
        // 计算本次消耗总量 = 时间增量 * 吸收率 * 消耗速度
        float delta = timeDelta * Data.AbsorptionRate * Data.nutritionConsumeSpeed.Value;
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

        GameObject panel = Instantiate(PanelPrefab, transform);
        panelUI = panel.GetComponent<BasePanel>();
        PanleInstance = panel;
        DataUpdate += RefreshUI;

        RefreshUI();

        // 设置面板为打开状态
        if (panelUI != null)
        {
            panelUI.Open();
            RestorePanelPosition(panel);
        }
        
        // 更新数据状态
        Data.ShowCanvas = true;
    }

    /// <summary>
    /// 恢复面板位置
    /// </summary>
    private void RestorePanelPosition(GameObject panel)
    {
        // 优先从拖拽组件获取位置
        var dragComponent = panel.GetComponentInChildren<UI_Drag>();
        if (dragComponent != null)
        {
            dragComponent.rectTransform.anchoredPosition = Data.PanelPosition;
        }
        else
        {
            // 如果没有拖拽组件，直接设置面板位置
            var panelRectTransform = panel.GetComponent<RectTransform>();
            if (panelRectTransform != null)
            {
                panelRectTransform.anchoredPosition = Data.PanelPosition;
            }
        }
    }

    [Button("隐藏面板")]
    public void HidePanel()
    {
        if (PanleInstance == null) return;

        SavePanelPosition();
        Destroy(PanleInstance);
        DataUpdate -= RefreshUI;
        panelUI = null;
        PanleInstance = null;
        
        // 更新数据状态
        Data.ShowCanvas = false;
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

    public bool IsPanelVisible()
    {
        return PanleInstance != null && panelUI != null && panelUI.IsOpen();
    }
    #endregion

    #region UI更新
    [Button("刷新面板")]
    public void RefreshUI()
    {
        if (panelUI == null) return;

        UpdateNutritionSlider("碳水", Data.nutrition.Carbohydrates, Data.nutrition.Max_Carbohydrates.Value);
        UpdateNutritionSlider("脂肪", Data.nutrition.Fat, Data.nutrition.Max_Fat.Value);
        UpdateNutritionSlider("蛋白质", Data.nutrition.Protein, Data.nutrition.Max_Protein.Value);
        UpdateNutritionSlider("水", Data.nutrition.Water, Data.nutrition.Max_Water.Value);
        UpdateNutritionSlider("维生素", Data.nutrition.Vitamins, Data.nutrition.Max_Vitamins.Value);
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
            // 营养值补满
            Data.nutrition.Max();
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

            // 当前食物的营养值补满
            BeEater.Data.nutrition.Max();
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
            vibrato = Random.Range(15, 30);
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

    public override void Save()
    {
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
    
#endregion
}
