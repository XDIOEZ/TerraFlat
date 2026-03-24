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

        List<Item> allFoodItems = new List<Item>();
        
        // 从多个tag中收集食物
        foreach (string tag in FoodTags)
        {
            if (context.itemDetector.Type_Tag_Item_Dict.TryGetValue(tag, out List<Item> items))
            {
                allFoodItems.AddRange(items);
            }
        }

        foreach (var item in allFoodItems)
        {
            //判断物体是否在进食范围内
            if (Vector2.Distance(context.transform.position, item.transform.position) > EatingRange)
            {
                continue;
            }

            Food = item.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;

            Food.BeEat(Self);

            LastEatingTime = Time.time;


            //判断是否已经吃饱了
            if (Food.Data.nutrition.GetFoodRate() > 0.9f)
            {
                return State.Success;
            }

            return State.Running;
        }

        return State.Failure;
    }
}

//TODO 获取感知范围内的食物  遍历食物检查是否在进食范围内  在嘴巴边上  吃掉  吃完后判断是否已经吃饱了
//TODO 吃饱了返回true 没有吃的了返回Failure