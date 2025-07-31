using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using Sirenix.OdinInspector;

[NodeMenu("ActionNode/����/�����Ϸʱ��")]
public class GameLightDetector : ActionNode
{
    [Tooltip("����˯�ߵ�̫������ǿ����ֵ")]
    [Range(0, 1)]
    public float SunlightIntensityThreshold = 0.5f;

    [Tooltip("��ǰ̫������ǿ��"), ReadOnly, ShowInInspector]
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
