
using UnityEngine;
using TheKiwiCoder;

public class EatFood : ActionNode
{
    public Mod_Food Food;
    public Mod_Food Self;
    [Header("��ʳ��Χ")]
    public float EatingRange = 1f;
    [Header("��һ�γ�һ�ڵ�ʱ��")]
    public float LastEatingTime;
    [Header("��ʳ���ʱ��")]
    public float EatingTime = 1f;
    [Header("ʳ��Tag")]
    public string FoodTag = "Food";
    protected override void OnStart() {
        Self = context.gameObject.GetComponentInChildren<Mod_Food>();
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
                        Food = item.Mods[ModText.Food] as Mod_Food;

                        Food.BeEat(Self);
                        LastEatingTime = Time.time;
                        

                        //�ж��Ƿ��Ѿ��Ա���
                        if(Food.Data.nutrition.GetHungerRate() > 0.9f)
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
