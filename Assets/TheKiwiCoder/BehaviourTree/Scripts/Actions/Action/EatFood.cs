
using UnityEngine;
using TheKiwiCoder;

public class EatFood : ActionNode
{
    public Mod_Food Food;
    public Mod_Food Self;
    [Header("进食范围")]
    public float EatingRange = 1f;
    [Header("上一次吃一口的时间")]
    public float LastEatingTime;
    [Header("进食间隔时间")]
    public float EatingTime = 1f;
    [Header("食物Tag")]
    public string FoodTag = "Food";
    protected override void OnStart() {
        Self = context.gameObject.GetComponentInChildren<Mod_Food>();
        LastEatingTime = Time.time;
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        //是否处于进食间隔
        if(Time.time - LastEatingTime < EatingTime)
        {
            return State.Running;
        }

        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            //判断物体是否在进食范围内
            if (Vector2.Distance(context.transform.position, item.transform.position) > EatingRange)
            { 
                continue;
            }
            //判断物体是否是食物
            foreach (string tag in item.Item_Data.ItemTags.Item_TypeTag)
            {

                if (tag == FoodTag)
                {
                        Food = item.Mods[ModText.Food] as Mod_Food;

                        Food.BeEat(Self);
                        LastEatingTime = Time.time;
                        

                        //判断是否已经吃饱了
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
