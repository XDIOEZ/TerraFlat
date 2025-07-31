using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using Sirenix.OdinInspector;

[NodeMenu("ActionNode/世界/检测游戏时间")]
public class GameLightDetector : ActionNode
{
    [Tooltip("触发睡眠的太阳光照强度阈值")]
    [Range(0, 1)]
    public float SunlightIntensityThreshold = 0.5f;

    [Tooltip("当前太阳光照强度"), ReadOnly, ShowInInspector]
    public float CurrentSunLightIntensity => SaveLoadManager.Instance.SaveData.Active_MapData.SunlightIntensity;

    protected override void OnStart() {

    }

    protected override void OnStop() {
    }

    protected override State OnUpdate()
    {
        if (SaveLoadManager.Instance.SaveData.Active_MapData == null)
        {
            return State.Failure;
        }
        
        if (SaveLoadManager.Instance.SaveData.Active_MapData.SunlightIntensity 
            < SunlightIntensityThreshold)
        {
            return State.Success;
        }
        else 
        {
           
            return State.Failure;
        }
    }
}
