using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/�ж�/����Ƿ���Թ���")]
public class AI_CheckCanAttack : ActionNode
{
    public float TriggerDistance = 0.5f; // ֻ������

    protected override void OnStart() { }

    protected override void OnStop() { }

    protected override State OnUpdate()
    {
        float dist = Vector2.Distance(context.mover.TargetPosition, context.Damage.transform.position);

        return dist <= TriggerDistance ? State.Success : State.Failure;
    }
}
