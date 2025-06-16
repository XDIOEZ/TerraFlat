using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using UnityEditorInternal.Profiling.Memory.Experimental;

public class EatFood : ActionNode
{
    public IFood Food;
    public FoodEater Self;
    [Header("��ʳ��Χ")]
    public float EatingRange = 1f;
    [Header("��һ�γ�һ�ڵ�ʱ��")]
    public float LastEatingTime;
    [Header("��ʳ���ʱ��")]
    public float EatingTime = 1f;
    [Header("ʳ��Tag")]
    public string FoodTag = "Food";
    protected override void OnStart() {
        Self = context.gameObject.GetComponentInChildren<FoodEater>();
        LastEatingTime = Time.time;
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        //�Ƿ��ڽ�ʳ���
        if(Time.time - LastEatingTime < EatingTime)
        {
            return State.Running;
        }

        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            //�ж������Ƿ��ڽ�ʳ��Χ��
            if (Vector2.Distance(context.transform.position, item.transform.position) > EatingRange)
            { 
                continue;
            }
            //�ж������Ƿ���ʳ��
                foreach (string tag in item.Item_Data.ItemTags.Item_TypeTag)
            {

                if (tag == FoodTag)
                {
                   
                        Food = item.GetComponent<IFood>();

                        if(Food.NutritionData.Food > 0)
                        {
                            Self.Eat(Food);
                            LastEatingTime = Time.time;
                        }

                        //�ж��Ƿ��Ѿ��Ա���
                        if(Self.hunger.Nutrition.Food >= Self.hunger.Nutrition.MaxFood)
                        {
                            return State.Success;
                        }

                        return State.Running;
                }
            }

        }


        return State.Failure;
    }
}
