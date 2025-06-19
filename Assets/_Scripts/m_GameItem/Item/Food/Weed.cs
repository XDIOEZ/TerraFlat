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
    // ʵ�� IHungry �ӿڵ� Foods ����
    public Nutrition NutritionData
    {
        get => Data.NutritionData; // ��� Data δ��ʼ��������Ĭ��ֵ
        set => Data.NutritionData = value;
    }
    public float EatingValue = 0;
    [Header("��ʳ�Ѷ�")]
    public float MaxEatingValue = 10;
    public IFood SelfFood { get => this; set => throw new System.NotImplementedException(); }


    // ʵ�� IHungry �ӿڵ� OnNutrientChanged �¼�
    public UltEvent OnNutrientChanged { get; set; } = new UltEvent();

    public override void Act()
    {
       
        OnNutrientChanged?.Invoke();
    }

    //�ɳ�
    [Button]
    public void Grow(float growValue)
    {
        
        // �����������������ֵ
        Data.NutritionData.Food = Mathf.Min(Data.NutritionData.Food + growValue, Data.NutritionData.MaxFood);
        Data.NutritionData.Water = Mathf.Min(Data.NutritionData.Water + growValue, Data.NutritionData.MaxWater);

        //���Food��Water������ 
        if (Data.NutritionData.Food >= Data.NutritionData.MaxFood && Data.NutritionData.Water >= Data.NutritionData.MaxWater)
        {
            //��ʼ��ֳ
            //�����Χ�Ƿ�뾶Ϊ2��Բ�η�Χ����û������ֲ��
            Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(transform.position, 2f);

            int plantCount = 0;
            foreach (Collider2D collider in nearbyColliders)
            {
                // �ų��Լ�
                if (collider.gameObject == this.gameObject) continue;

                // ����Ƿ�Ϊֲ��
                Item item = collider.GetComponent<Item>();
                if (item != null && item.Item_Data.ItemTags.Item_TypeTag.Exists(x => x == "Plant"))
                {
                    plantCount++;
                }
            }

            //���û������ֲ���ֲ������<=2
            if (plantCount <= 2)
            {
                //ͨ����������Χ�Ƿ�������ֲ�� ��2D��Ϸ
                //��ʼ����
                Item newItem = RunTimeItemManager.Instance.InstantiateItem(Data.IDName);

                if (newItem != null)
                {
                    //��Item���������������Ϊ���ĵİ뾶Ϊ1.5��Բ�η�Χ��
                    float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    float randomDistance = Random.Range(0.5f, 1.5f);

                    Vector2 offset = new Vector2(
                        Mathf.Cos(randomAngle) * randomDistance,
                        Mathf.Sin(randomAngle) * randomDistance
                    );

                    Vector2 newPosition = (Vector2)transform.position + offset;
                    newItem.transform.position = newPosition;

                    // ��ֳ�����Ĳ���Ӫ��
                    Data.NutritionData.Food *= 0.1f;
                    Data.NutritionData.Water *= 0.1f;

                    //������ֲ��ĳ�ʼ״̬ ��Ӫ��ֵΪ0.1f
                    newItem.GetComponent<IFood>().NutritionData.Food*=0.1f;
                    newItem.GetComponent<IFood>().NutritionData.Water *= 0.1f;
                }
            }
        }
    }

    public Nutrition BeEat(float eatSpeed)
    {
        if (Item_Data == null || NutritionData == null) return null;

        // ʹ�� DOTween ����������
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
                //ֹͣDoTween����
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