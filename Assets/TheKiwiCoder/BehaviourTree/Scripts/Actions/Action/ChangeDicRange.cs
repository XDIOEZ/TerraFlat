using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using DG.Tweening;
[NodeMenu("ActionNode/行动/修改检测范围")]
public class ChangeDicRange : ActionNode
{

    [Tooltip("基础值，偏移将在此基础上计算")]
    public float BaseValue = 5f;

    [Tooltip("偏移参数，x为最小偏移（可为负），y为最大偏移（可为正）")]
    public Vector2 Offset = new Vector2(0,0);

    [Tooltip("平滑调整的持续时间（秒），为 0 表示立即生效")]
    [Range(0f, 3f)]
    public float Duration = 0.5f;

    protected override void OnStart() { }

    protected override void OnStop() { }

    protected override State OnUpdate()
    {
        float offset = (Offset.x == Offset.y)
            ? Offset.x
            : Random.Range(Offset.x, Offset.y);

        float targetValue = Mathf.Max(0f, BaseValue + offset);

        DOTween.Kill(context.itemDetector);

        if (Duration > 0f)
        {
            DOTween.To(() => context.itemDetector.DetectionRadius,
                       x => context.itemDetector.DetectionRadius = x,
                       targetValue,
                       Duration).SetTarget(context.itemDetector);
        }
        else
        {
            context.itemDetector.DetectionRadius = targetValue;
        }

        return State.Success;
    }
}
