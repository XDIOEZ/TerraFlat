using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/世界/检测游戏时间")]
public class GameTimeDetector : ActionNode
{
    [Tooltip("触发Sucess的游戏时间")]
    public Vector2 time;

    protected override void OnStart() {

    }

    protected override void OnStop() {
    }

    protected override State OnUpdate()
    {
        if (DayNightTimeManager.Instance == null)
        {
            return State.Failure;
        }
        if (DayNightTimeManager.Instance.currentDayTime < time.y && DayNightTimeManager.Instance.currentDayTime > time.x)
        {
            return State.Success;
        }
        else 
        {
           
            return State.Failure;
        }
    }
}
