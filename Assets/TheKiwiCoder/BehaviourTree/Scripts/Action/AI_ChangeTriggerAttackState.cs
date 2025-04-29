using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
[NodeMenu("ActionNode/�ж�/�޸Ĺ���״̬")]
public class AI_ChangeTriggerAttackState : ActionNode
{
    public ITriggerAttack triggerAttack;
    [Header("�����л���״̬")]
    public KeyState TOKeyState;
    protected override void OnStart() {
        if(triggerAttack== null)
        {
            triggerAttack = context.gameObject.GetComponentInChildren<ITriggerAttack> ();
        }
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() {
        switch(TOKeyState)
        {
            case KeyState.Start:
                triggerAttack.StartTriggerAttack();
                break;
            case KeyState.Hold:
                triggerAttack.StayTriggerAttack();
                break;
            case KeyState.End:
                triggerAttack.StopTriggerAttack();
                break;
        }
       

        return State.Success;
    }
    
}
