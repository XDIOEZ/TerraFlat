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
    // 实现 IHungry 接口的 Foods 属性
    public Hunger_Water Foods
    {
        get => Data.Energy_food; // 如果 Data 未初始化，返回默认值
        set => Data.Energy_food = value;
    }
    public float EatingValue = 0;
    public IFood SelfFood { get => this; set => throw new System.NotImplementedException(); }


    // 实现 IHungry 接口的 OnNutrientChanged 事件
    public UltEvent OnNutrientChanged { get; set; } = new UltEvent();

    public override void Act()
    {
        // 限制能量不超过最大值
        Data.Energy_food.Food = Mathf.Min(Data.Energy_food.Food + 1, Data.Energy_food.MaxFood);
        OnNutrientChanged?.Invoke();
    }

    public Hunger_Water BeEat(float eatSpeed)
    {
        if (Item_Data == null || Foods == null) return null;

        // 使用 DOTween 做抖动动画
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
                //停止DoTween动画
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

}

