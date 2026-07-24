using UnityEngine;
using TheKiwiCoder;
using System.Collections.Generic;

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
    [Header("食物Tags")]
    public List<string> FoodTags = new List<string> { "Food" }; // 改为列表形式
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

        for (int tagIndex = 0; tagIndex < FoodTags.Count; tagIndex++)
        {
            string tag = FoodTags[tagIndex];
            if (context.itemDetector.Type_Tag_Item_Dict.TryGetValue(tag, out List<Item> items))
            {
                for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                {
                    Item detectedItem = items[itemIndex];
                    if (detectedItem == null)
                        continue;

                    Vector2 offset = (Vector2)detectedItem.transform.position - (Vector2)context.transform.position;
                    if (offset.sqrMagnitude > EatingRange * EatingRange)
                        continue;

                    Food = detectedItem.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
                    if (Food == null)
                        continue;

                    Food.BeEat(Self);
                    LastEatingTime = Time.time;

                    if (Food.Data.nutrition.GetFoodRate() > 0.9f)
                        return State.Success;

                    return State.Running;
                }
            }
        }

        return State.Failure;
    }
}

//TODO 获取感知范围内的食物  遍历食物检查是否在进食范围内  在嘴巴边上  吃掉  吃完后判断是否已经吃饱了
//TODO 吃饱了返回true 没有吃的了返回Failure