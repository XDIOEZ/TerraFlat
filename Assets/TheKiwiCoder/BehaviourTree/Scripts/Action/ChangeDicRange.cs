using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using DG.Tweening;
[NodeMenu("ActionNode/�ж�/�޸ļ�ⷶΧ")]
public class ChangeDicRange : ActionNode
{

    [Tooltip("����ֵ��ƫ�ƽ��ڴ˻����ϼ���")]
    public float BaseValue = 5f;

    [Tooltip("ƫ�Ʋ�����xΪ��Сƫ�ƣ���Ϊ������yΪ���ƫ�ƣ���Ϊ����")]
    public Vector2 Offset = new Vector2(0,0);

    [Tooltip("ƽ�������ĳ���ʱ�䣨�룩��Ϊ 0 ��ʾ������Ч")]
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
