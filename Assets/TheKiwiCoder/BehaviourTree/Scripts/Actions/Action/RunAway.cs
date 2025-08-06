using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/行动/逃跑")]
public class RunAway : ActionNode
{
    [Tooltip("逃跑方向输出")]
    public Vector2 RunAwayDirection;

    [Tooltip("逃跑的距离")]
    public float RunAwayDistance = 10f;

    [Tooltip("随机偏移角度（度），180表示 ±90° 各向随机")]
    public float RandomAngle = 180f;

    protected override void OnStart()
    {
        RunAwayDirection = Vector2.zero;
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        Vector2 finalDirection = Vector2.zero;
        var attackerUIDs = context.damageReciver.Data.AttackersUIDs;
        var items = context.itemDetector.CurrentItemsInArea;
        Vector2 selfPos = context.transform.position;

        // 累加所有攻击者的反向向量
        for (int i = 0; i < attackerUIDs.Count; i++)
        {
            int attackerGuid = attackerUIDs[i];
            float weight = (i + 1f) / attackerUIDs.Count; // 越靠后权重越大

            foreach (var item in items)
            {
                if (item.itemData.Guid == attackerGuid)
                {
                    Vector2 attackerPos = item.transform.position;
                    Vector2 awayDir = (selfPos - attackerPos).normalized;
                    finalDirection += awayDir * weight;
                }
            }
        }

        if (finalDirection != Vector2.zero)
        {
            // 归一化基础逃跑方向
            RunAwayDirection = finalDirection.normalized;

            // 基础目标向量
            Vector2 baseTarget = RunAwayDirection * RunAwayDistance*2;

            // 计算随机偏移角度，范围 [-RandomAngle/2, +RandomAngle/2]
            float halfAngle = RandomAngle * 0.5f;
            float offset = Random.Range(-halfAngle, halfAngle);

            // 将 baseTarget 绕 Z 轴旋转 offset 度
            Vector3 rotated = Quaternion.Euler(0f, 0f, offset) * baseTarget;

            // 最终目标点（相对于自身位置）
            Vector2 newTargetPos = (Vector2)context.transform.position + (Vector2)rotated;

            // 下发给 mover
            context.mover.TargetPosition = newTargetPos;

            return State.Success;
        }
        else
        {
            return State.Failure;
        }
    }
}
