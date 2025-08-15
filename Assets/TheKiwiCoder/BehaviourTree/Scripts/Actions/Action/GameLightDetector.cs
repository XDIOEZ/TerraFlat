using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using Sirenix.OdinInspector;

[NodeMenu("ActionNode/����/�����Ϸʱ��")]
public class GameLightDetector : ActionNode
{
    [Tooltip("����˯�ߵ�̫������ǿ����ֵ")]
    public Vector2 SunlightIntensityThreshold = new Vector2();

    [Tooltip("��ǰ̫������ǿ��"), ReadOnly, ShowInInspector]
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
