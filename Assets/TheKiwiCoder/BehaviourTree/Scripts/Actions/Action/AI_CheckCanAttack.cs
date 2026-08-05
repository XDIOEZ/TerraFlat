using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/行动/检测是否可以攻击")]
public class AI_CheckCanAttack : ActionNode
{
    [Header("最大攻击距离")]
    public float maxAttackDistance = 0.5f; // 最大攻击距离
    
    [Header("最小攻击距离")]
    public float minAttackDistance = 0.0f; // 最小攻击距离

    public Color debugColor;

    protected override void OnStart() { }

    protected override void OnStop() { }

    protected override State OnUpdate()
    {
        float dist = Vector2.Distance(context.mover.TargetPosition, context.Damage.transform.position);
        
        // 检查距离是否在有效攻击范围内（大于最小距离且小于最大距离）
        return (dist >= minAttackDistance && dist <= maxAttackDistance) ? State.Success : State.Failure;
    }

    public override void OnDrawGizmos()
{
    if (context == null || context.Damage == null)
        return;

    Vector3 attackPosition = context.Damage.transform.position;
    
    // 使用debugColor绘制最大攻击距离
    Gizmos.color = debugColor;
    Gizmos.DrawWireSphere(attackPosition, maxAttackDistance);
    
    // 绘制最小攻击距离（如果大于0）
    if (minAttackDistance > 0)
    {
        Gizmos.DrawWireSphere(attackPosition, minAttackDistance);
    }
}


}