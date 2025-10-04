using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/�ж�/����Ƿ���Թ���")]
public class AI_CheckCanAttack : ActionNode
{
    [Header("��󹥻�����")]
    public float maxAttackDistance = 0.5f; // ��󹥻�����
    
    [Header("��С��������")]
    public float minAttackDistance = 0.0f; // ��С��������

    protected override void OnStart() { }

    protected override void OnStop() { }

    protected override State OnUpdate()
    {
        float dist = Vector2.Distance(context.mover.TargetPosition, context.Damage.transform.position);
        
        // �������Ƿ�����Ч������Χ�ڣ�������С������С�������룩
        return (dist >= minAttackDistance && dist <= maxAttackDistance) ? State.Success : State.Failure;
    }
}