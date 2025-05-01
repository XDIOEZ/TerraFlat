using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using Unity.Collections;

public class DisPlay : DecoratorNode
{
    // �ڼ��������ʾ��ǰ״̬�������InInspector���ԣ�
    public State currentState;

    protected override void OnStart()
    {
        // ����״̬����ѡ��
        currentState = State.Running;
    }

    protected override void OnStop()
    {
        // ���������״̬����ѡ��
        currentState = State.Failure;
    }

    protected override State OnUpdate()
    {
        Debug.Log(currentState+"--"+child.description);
        currentState = child.Update();

        return currentState;
    }
}