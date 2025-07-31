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

    public UI_FloatData UIValues;

    public GameObject PanleInstance;

    public Mod_Stamina Stamina;

    public bool ShowCanvas
    {
        get => Data.ShowCanvas;
        set
        {
            if (Data.ShowCanvas != value)
            {
                Data.ShowCanvas = value;

                // ����ֵ���ö�Ӧ����庯��
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
        public Nutrition nutrition = new();//Ӫ��ֵ
        public float Max_EatingProgress = 3;//������
        public float AbsorptionRate = 1f;//������
        public bool ShowCanvas = false;//�����ʾ״̬
        public GameValue_float nutritionConsumeSpeed = new(1f);

        public bool FeelGood = false; // �� ��������
    }

    public override void Awake()
    {
        if (_Data.Name == "")
        {
            _Data.Name = ModText.Food;
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

    }
    public override void Action(float timeDelta)
    {
        // Ӫ������
        float totalEnergy = ConsumeNutrition(timeDelta);
        //��������-ǰ���Ǵ��ھ���ģ��
        if (Stamina != null)
            Stamina.AddStamina(totalEnergy);

        DataUpdate?.Invoke();
    }

    private float ConsumeNutrition(float timeDelta)
    {
        // �жϵ�ǰ�����Ƿ������������Ƿ����Ӫ�������ٶ�
        bool shouldSlow = Stamina != null && Stamina.IsStaminaFull;

        // ����������֮ǰδ����ʱ�����������ٶȲ����״̬
        if (shouldSlow && !Data.FeelGood)
        {
            Data.nutritionConsumeSpeed.MultiplicativeModifier *= 0.5f;
            Data.FeelGood = true;
        }
        // ������������֮ǰ���ڼ���״̬ʱ���ָ������ٶȲ�����״̬
        else if (!shouldSlow && Data.FeelGood)
        {
            Data.nutritionConsumeSpeed.MultiplicativeModifier *= 2f;
            Data.FeelGood = false;
        }

        // ���㱾���������� = ʱ������ * ������ * ��ǰ�����ٶ�����ֵ
        float delta = timeDelta * Data.AbsorptionRate * Data.nutritionConsumeSpeed.Value;
        float remainingDelta = delta;
        float totalEnergy = 0f;

        // ��������̼ˮ��������ܳ�����ǰ̼ˮ��
        float usedCarb = Mathf.Min(Data.nutrition.Carbohydrates, remainingDelta);
        remainingDelta -= usedCarb;
        Data.nutrition.Carbohydrates -= usedCarb;
        totalEnergy += usedCarb;

        float usedFat = 0f;
        float usedProtein = 0f;
        float usedWater = 0f;

        // ����ʣ������������֬�������ܳ�����ǰ֬����
        if (remainingDelta > 0)
        {
            usedFat = Mathf.Min(Data.nutrition.Fat, remainingDelta);
            remainingDelta -= usedFat;
            Data.nutrition.Fat -= usedFat;
            totalEnergy += usedFat;
        }

        // ����ʣ�����������ڵ����ʣ����ܳ�����ǰ��������
        if (remainingDelta > 0)
        {
            usedProtein = Mathf.Min(Data.nutrition.Protein, remainingDelta);
            remainingDelta -= usedProtein;
            Data.nutrition.Protein -= usedProtein;
            totalEnergy += usedProtein;
        }

        // TODO��ˮ�������ǳ����Եģ��������ٶ��ܵ�ǰ��������Ӱ��
        // ����̼ˮʱˮ��������Ϊ1��֬��Ϊ2��������Ϊ3
        usedWater = usedCarb * 1f + usedFat * 2f + usedProtein * 3f;

        // �۳���Ӧ��ˮ�֣�ˮ�ֲ������0
        Data.nutrition.Water = Mathf.Max(0, Data.nutrition.Water - usedWater);

        // ά������Ȼ���ģ��ٶ�Ϊ0.01��ʱ������
        float naturalVitaminLoss = timeDelta * 0.01f;
        Data.nutrition.Vitamins = Mathf.Max(0, Data.nutrition.Vitamins - naturalVitaminLoss);

        // ���ر������ĵ�������ֵ
        return totalEnergy;
    }



    [Button("��ʾ���")]
    public void ShowPanle()
    {
        //TODO ��ʹ���ֶξ����Ƿ���ʾ��� ����ͨ������Ƿ����GameObject������
        if (PanleInstance != null) return; // ����Ѵ����򷵻�

        GameObject panel = Instantiate(PanelPrefab, transform);
        UIValues = panel.GetComponent<UI_FloatData>();
        PanleInstance = panel;
        DataUpdate += RefreshUI;

        UIValues.Sliders["̼ˮ"].maxValue = Data.nutrition.Max_Carbohydrates.Value;
        UIValues.Sliders["֬��"].maxValue = Data.nutrition.Max_Fat.Value;
        UIValues.Sliders["������"].maxValue = Data.nutrition.Max_Protein.Value;
        UIValues.Sliders["ˮ"].maxValue = Data.nutrition.Max_Water.Value;
        UIValues.Sliders["ά����"].maxValue = Data.nutrition.Max_Vitamins.Value;

        RefreshUI();
        Data.ShowCanvas = true; // ȷ��״̬ͬ��
    }

    [Button("�������")]
    public void HidePanle()
    {
        if (PanleInstance == null) return; // ��岻�����򷵻�

        Destroy(PanleInstance);
        DataUpdate -= RefreshUI;
        UIValues = null;
        Data.ShowCanvas = false; // ȷ��״̬ͬ��
    }

    [Button("ˢ�����")]
    public void RefreshUI()
    {
        UIValues.Sliders["̼ˮ"].value = Data.nutrition.Carbohydrates;
        UIValues.Sliders["֬��"].value = Data.nutrition.Fat;
        UIValues.Sliders["������"].value = Data.nutrition.Protein;

        UIValues.Sliders["ˮ"].value = Data.nutrition.Water;
        UIValues.Sliders["ά����"].value = Data.nutrition.Vitamins;
    }

    public void BeEat(Mod_Food other_Food)
    {
        ShakeItem(item.transform);

        EatingProgress++;

        if (EatingProgress >= Data.Max_EatingProgress)
        {
            // ���ٶѵ�����
            item.Item_Data.Stack.Amount--;
            // UI ����֪ͨ
            item.OnUIRefresh?.Invoke();
            // Ӫ��ֵ����
            Data.nutrition.Max();
            // ���ȹ���
            EatingProgress = 0;

            other_Food.Data.nutrition = other_Food.Data.nutrition + Data.nutrition;

            DataUpdate.Invoke();

            if (item.Item_Data.Stack.Amount <= 0)
            {
                Destroy(item.gameObject); // ��������
            }
        }
    }

    #region ���붯��
    [Button("����")]
    void ShakeItem(Transform transform, float duration = 0.2f, float strength = 0.2f, int vibrato = 0)
    {
        if (vibrato == 0)
        {
            //����һ������Ķ���ƫ����
            vibrato = Random.Range(15, 30);
        }
        // �� DOTween ���ֲ�����
        transform.DOShakePosition(duration, strength, vibrato).SetEase(Ease.OutQuad);

        // ���÷�װ������Ӵ�������
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
        ExData.WriteData(Data);
    }
}