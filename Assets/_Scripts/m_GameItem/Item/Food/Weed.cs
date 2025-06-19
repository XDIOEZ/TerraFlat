using DG.Tweening;
using Sirenix.OdinInspector;
using UltEvents;
using UnityEngine;
public class Weed : Item, IFood,IPlant
{
    [SerializeField]
    private Data_Food Data;
    public override ItemData Item_Data
    {
        get => Data;
        set => Data = (Data_Food)value;
    }
    // 实现 IHungry 接口的 Foods 属性
    public Nutrition NutritionData
    {
        get => Data.NutritionData; // 如果 Data 未初始化，返回默认值
        set => Data.NutritionData = value;
    }
    public float EatingValue = 0;
    [Header("进食难度")]
    public float MaxEatingValue = 10;
    public IFood SelfFood { get => this; set => throw new System.NotImplementedException(); }


    // 实现 IHungry 接口的 OnNutrientChanged 事件
    public UltEvent OnNutrientChanged { get; set; } = new UltEvent();

    public override void Act()
    {
       
        OnNutrientChanged?.Invoke();
    }

    //成长
    [Button]
    public void Grow(float growValue)
    {
        
        // 限制能量不超过最大值
        Data.NutritionData.Food = Mathf.Min(Data.NutritionData.Food + growValue, Data.NutritionData.MaxFood);
        Data.NutritionData.Water = Mathf.Min(Data.NutritionData.Water + growValue, Data.NutritionData.MaxWater);

        //如果Food和Water都满了 
        if (Data.NutritionData.Food >= Data.NutritionData.MaxFood && Data.NutritionData.Water >= Data.NutritionData.MaxWater)
        {
            //开始繁殖
            //检测周围是否半径为2的圆形范围内有没有其他植物
            Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(transform.position, 2f);

            int plantCount = 0;
            foreach (Collider2D collider in nearbyColliders)
            {
                // 排除自己
                if (collider.gameObject == this.gameObject) continue;

                // 检测是否为植物
                Item item = collider.GetComponent<Item>();
                if (item != null && item.Item_Data.ItemTags.Item_TypeTag.Exists(x => x == "Plant"))
                {
                    plantCount++;
                }
            }

            //如果没有其他植物或植物数量<=2
            if (plantCount <= 2)
            {
                //通过物理检测周围是否有其他植物 是2D游戏
                //开始复制
                Item newItem = RunTimeItemManager.Instance.InstantiateItem(Data.IDName);

                if (newItem != null)
                {
                    //将Item随机放置在以自身为中心的半径为1.5的圆形范围内
                    float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    float randomDistance = Random.Range(0.5f, 1.5f);

                    Vector2 offset = new Vector2(
                        Mathf.Cos(randomAngle) * randomDistance,
                        Mathf.Sin(randomAngle) * randomDistance
                    );

                    Vector2 newPosition = (Vector2)transform.position + offset;
                    newItem.transform.position = newPosition;

                    // 繁殖后消耗部分营养
                    Data.NutritionData.Food *= 0.1f;
                    Data.NutritionData.Water *= 0.1f;

                    //设置新植物的初始状态 其营养值为0.1f
                    newItem.GetComponent<IFood>().NutritionData.Food*=0.1f;
                    newItem.GetComponent<IFood>().NutritionData.Water *= 0.1f;
                }
            }
        }
    }

    public Nutrition BeEat(float eatSpeed)
    {
        if (Item_Data == null || NutritionData == null) return null;

        // 使用 DOTween 做抖动动画
        SelfFood.ShakeItem(this.transform);

        EatingValue += eatSpeed;
        if (EatingValue >= MaxEatingValue)
        {
            Item_Data.Stack.Amount--;

            UpdatedUI_Event?.Invoke();

            NutritionData.Food = NutritionData.MaxFood;
            NutritionData.Water = NutritionData.MaxWater;

            EatingValue = 0;

            if (Item_Data.Stack.Amount <= 0)
            {
                //停止DoTween动画
                transform.DOKill();
                Destroy(gameObject);
            }
            return NutritionData;
        }
        return null;
    }
}

public interface IPlant
{
    void Grow(float growValue);
}