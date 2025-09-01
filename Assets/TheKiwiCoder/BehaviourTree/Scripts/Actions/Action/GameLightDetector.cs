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
      /*  if (SaveDataManager.Instance.SaveData.Active_MapData == null)
        {
            return;
        }
        CurrentSunLightIntensity = SaveDataManager.Instance.SaveData.Active_MapData.SunlightIntensity;*/
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate()
    {
        /*
        if (SaveDataManager.Instance.SaveData.Active_MapData == null)
        {
            return State.Failure;
        }
        
        if (SaveDataManager.Instance.SaveData.Active_MapData.SunlightIntensity 
            < SunlightIntensityThreshold.y && SaveDataManager.Instance.SaveData.Active_MapData.SunlightIntensity
            > SunlightIntensityThreshold.y)
        {
           
        }
        else 
        {
           
    
        }*/
        return State.Failure;
    }
}
