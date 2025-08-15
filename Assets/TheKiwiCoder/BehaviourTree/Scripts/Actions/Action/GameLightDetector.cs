using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using Sirenix.OdinInspector;

[NodeMenu("ActionNode/世界/检测游戏时间")]
public class GameLightDetector : ActionNode
{
    [Tooltip("触发睡眠的太阳光照强度阈值")]
    public Vector2 SunlightIntensityThreshold = new Vector2();

    [Tooltip("当前太阳光照强度"), ReadOnly, ShowInInspector]
    public float CurrentSunLightIntensity = 0;

    protected override void OnStart() {
        if (SaveLoadManager.Instance.SaveData.Active_MapData == null)
        {
            return;
        }
        CurrentSunLightIntensity = SaveLoadManager.Instance.SaveData.Active_MapData.SunlightIntensity;
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
            < SunlightIntensityThreshold.y && SaveLoadManager.Instance.SaveData.Active_MapData.SunlightIntensity
            > SunlightIntensityThreshold.y)
        {
            return State.Success;
        }
        else 
        {
           
            return State.Failure;
        }
    }
}
