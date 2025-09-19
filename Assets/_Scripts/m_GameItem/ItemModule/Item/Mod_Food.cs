using DG.Tweening;
using MemoryPack;
using NUnit.Framework.Interfaces;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UltEvents;
using static OfficeOpenXml.ExcelErrorValue;

public partial class Mod_Food : Module
{
    public Ex_ModData_MemoryPackable ExData;
    public override ModuleData _Data { get => ExData; set => ExData = (Ex_ModData_MemoryPackable)value; }

    public Mod_Food_Data Data = new Mod_Food_Data();

    public float EatingProgress = 0;

    public UltEvent DataUpdate = new UltEvent();

    public GameObject PanelPrefab;

    public GameObject PanleInstance;

    public UI_FloatData_Slider UIValues;

    public Mod_Stamina Stamina;

    public bool ShowCanvas
    {
        get => Data.ShowCanvas;
        set
        {
            if (Data.ShowCanvas != value)
            {
                Data.ShowCanvas = value;

                // 根据值调用对应的面板函数
                if (Data.ShowCanvas)
                {
                    ShowPanle();
                }
                else
                {
                    HidePanle();
                }
            }
        }
    }

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
        //TODO 添加子对象的面板位置作为 持久化数据  在实例化面板时保存面板位置  在关闭面板时恢复面板位置 在Save函数中 如果面板存在 就保存面板的位置
        public Vector2 PanelPosition = new Vector2(0, 0);
    }

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


        if (Data.ShowCanvas)
        {
            ShowPanle();
        }
        else
        {
            HidePanle();
        }



        if (item.Mods.ContainsKey(ModText.Stamina))
        Stamina = item.Mods[ModText.Stamina] as Mod_Stamina;


            if (item != null)
            {
            item.OnAct += Act;
            }

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
        // 营养消耗
        float totalEnergy = ConsumeNutrition(timeDelta);
        //精力补充-前提是存在精力模块
        if (Stamina != null)
            Stamina.AddStamina(totalEnergy);

        DataUpdate?.Invoke();
    }

    private float ConsumeNutrition(float timeDelta)
    {
        // 判断当前精力是否已满，决定是否减缓营养消耗速度
        bool shouldSlow = Stamina != null && Stamina.IsStaminaFull;

        // 当精力满且之前未减速时，减慢消耗速度并标记状态
        if (shouldSlow && !Data.FeelGood)
        {
            Data.nutritionConsumeSpeed.MultiplicativeModifier *= 0.5f;
            Data.FeelGood = true;
        }
        // 当精力不满且之前处于减速状态时，恢复消耗速度并重置状态
        else if (!shouldSlow && Data.FeelGood)
        {
            Data.nutritionConsumeSpeed.MultiplicativeModifier *= 2f;
            Data.FeelGood = false;
        }

        // 计算本次消耗总量 = 时间增量 * 吸收率 * 当前消耗速度修正值
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

        // TODO：水的消耗是持续性的，且消耗速度受当前消耗物质影响
        // 消耗碳水时水消耗速率为1，脂肪为2，蛋白质为3
        usedWater = usedCarb * 1f + usedFat * 2f + usedProtein * 3f;

        // 扣除相应的水分，水分不会低于0
        Data.nutrition.Water = Mathf.Max(0, Data.nutrition.Water - usedWater);

        // 维生素自然消耗，速度为0.01倍时间增量
        float naturalVitaminLoss = timeDelta * 0.01f;
        Data.nutrition.Vitamins = Mathf.Max(0, Data.nutrition.Vitamins - naturalVitaminLoss);

        // 返回本次消耗的总能量值
        return totalEnergy;
    }



    [Button("显示面板")]
    public void ShowPanle()
    {
        if (PanleInstance != null) return;

        GameObject panel = Instantiate(PanelPrefab, transform);
        UIValues = panel.GetComponent<UI_FloatData_Slider>();
        PanleInstance = panel;
        DataUpdate += RefreshUI;

        UIValues.Sliders["碳水"].maxValue = Data.nutrition.Max_Carbohydrates.Value;
        UIValues.Sliders["脂肪"].maxValue = Data.nutrition.Max_Fat.Value;
        UIValues.Sliders["蛋白质"].maxValue = Data.nutrition.Max_Protein.Value;
        UIValues.Sliders["水"].maxValue = Data.nutrition.Max_Water.Value;
        UIValues.Sliders["维生素"].maxValue = Data.nutrition.Max_Vitamins.Value;

        RefreshUI();
        Data.ShowCanvas = true;


        // ✅ 从 UI_Drag 中获取 rectTransform 并恢复位置
        var s = panel.GetComponentInChildren<UI_Drag>();
        if (s != null)
        {
            s.rectTransform.anchoredPosition = Data.PanelPosition;
        }
    }




    [Button("隐藏面板")]
    public void HidePanle()
    {
        if (PanleInstance == null) return;

        // ✅ 从 UI_Drag 中获取 rectTransform 并保存位置
        var s = PanleInstance.GetComponentInChildren<UI_Drag>();
        if (s != null)
        {
            Data.PanelPosition = s.rectTransform.anchoredPosition;
        }

        Destroy(PanleInstance);
        DataUpdate -= RefreshUI;
        UIValues = null;
        Data.ShowCanvas = false;
    }



    [Button("刷新面板")]
    public void RefreshUI()
    {
        UIValues.Sliders["碳水"].value = Data.nutrition.Carbohydrates;
        UIValues.Sliders["脂肪"].value = Data.nutrition.Fat;
        UIValues.Sliders["蛋白质"].value = Data.nutrition.Protein;

        UIValues.Sliders["水"].value = Data.nutrition.Water;
        UIValues.Sliders["维生素"].value = Data.nutrition.Vitamins;
    }

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
            Data.nutrition.Max();
            EatingProgress = 0; // 吃进度归零

            // 吃掉目标食物的营养值
            Data.nutrition = Data.nutrition + BeEater.Data.nutrition;

            DataUpdate.Invoke();  // 通知数据更新

            // 如果被吃食物的堆叠数量为 0，销毁该食物
            if (BeEater.item.itemData.Stack.Amount <= 0)
            {
                Destroy(BeEater.item.gameObject);  // 销毁被吃的食物
            }
        }
    }



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
            var dominant = new ColorThief.ColorThief();
            UnityEngine.Color mainColor = dominant.GetColor(sr.sprite.texture).UnityColor;

            GameObject particle = GameRes.Instance.InstantiatePrefab(prefabName, targetTransform.position);
            ParticleSystem ps = particle.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                main.startColor = mainColor;
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
            var s = PanleInstance.GetComponentInChildren<UI_Drag>();
            if (s != null)
            {
                Data.PanelPosition = s.rectTransform.anchoredPosition;
            }
        }
        //TODO 将Transform从Dotween中恢复
        DOTween.Kill(item.transform); // 终止所有与该对象相关的 tween（如果存在）

        ExData.WriteData(Data);
        
    }
}