using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using UnityEditorInternal.Profiling.Memory.Experimental;

public class EatFood : ActionNode
{
    public IFood Food;
    public IHunger Self;
    public float EatingRange = 1f;
    public float StartEatingTime;
    public float EatingTime = 1f;
    public float EatingSpeedRate = 20f;
    protected override void OnStart() {
        Self = context.gameObject.GetComponent<IHunger>();
        StartEatingTime = Time.time;
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        if(Time.time - StartEatingTime < EatingTime)
        {
            Debug.Log("间隔时间未到");
            return State.Running;
        }

        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            foreach (string tag in item.Item_Data.ItemTags.Item_TypeTag)
            {
                if (tag == "Food")
                {
                    if(Vector2.Distance(context.transform.position, item.transform.position) < EatingRange)
                    {
                        Food = item.GetComponent<IFood>();

                        if(Food.Foods.Food > 0)
                        {
                            Food.BeEat(1/Self.EatingSpeed * EatingSpeedRate) ;
                            Self.Eat(1/Self.EatingSpeed * EatingSpeedRate);
                            StartEatingTime = Time.time;
                        }

                        if(Self.Foods.Food >= Self.Foods.MaxFood)
                        {
                            return State.Success;
                        }
                        return State.Running;
                    }
                }
            }

        }
        return State.Failure;
    }
}
