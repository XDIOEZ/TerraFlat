using DG.Tweening;
using log4net.Util;
using MemoryPack;
using NaughtyAttributes;
using UltEvents;
using UnityEngine;
using Transform = UnityEngine.Transform;
public class Grass : Item, IFood
{
    [SerializeField]
    private FoodData Data;
    public override ItemData Item_Data
    {
        get => Data;
        set
        {
            Data = (FoodData)value;
        }
    }
    // ʵ�� IHungry �ӿڵ� Foods ����
    public Hunger_Water Foods
    {
        get => Data.Energy_food; // ��� Data δ��ʼ��������Ĭ��ֵ
        set => Data.Energy_food = value;
    }
    public float EatingValue = 0;
    public IFood SelfFood { get => this; set => throw new System.NotImplementedException(); }


    // ʵ�� IHungry �ӿڵ� OnNutrientChanged �¼�
    public UltEvent OnNutrientChanged { get; set; } = new UltEvent();

    public override void Act()
    {
        // �����������������ֵ
        Data.Energy_food.Food = Mathf.Min(Data.Energy_food.Food + 1, Data.Energy_food.MaxFood);
        OnNutrientChanged?.Invoke();
    }

    public Hunger_Water BeEat(float eatSpeed)
    {
        if (Item_Data == null || Foods == null) return null;

        // ʹ�� DOTween ����������
        SelfFood.ShakeItem(this.transform);

        EatingValue += eatSpeed;
        if (EatingValue >= Foods.MaxFood)
        {
            Item_Data.Stack.Amount--;

            UpdatedUI_Event?.Invoke();

            Foods.Food = Foods.MaxFood;
            Foods.Water = Foods.MaxWater;

            EatingValue = 0;

            if (Item_Data.Stack.Amount <= 0)
            {
                //ֹͣDoTween����
                transform.DOKill();
                Destroy(gameObject);
            }
            return Foods;
        }
        return null;
    }
}

public interface IFood
{
    [ShowNativeProperty]
    Hunger_Water Foods { get; set; }

    Hunger_Water BeEat(float BeEatSpeed);

    public IFood SelfFood { get => this; set => throw new System.NotImplementedException(); }

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

}

