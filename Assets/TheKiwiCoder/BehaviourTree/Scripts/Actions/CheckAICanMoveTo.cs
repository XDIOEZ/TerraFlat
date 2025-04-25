using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;

public class CheckAICanMoveTo : ActionNode
{
    public NavMeshAgent navMesh;
    protected override void OnStart() 
    {
        if (navMesh == null)
        {
            navMesh = context.gameObject.GetComponent<NavMeshAgent>();
        }
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
   
        // ���·����Ч����Ŀ�겻�ɴ������ʧ��
        if (navMesh.pathStatus == NavMeshPathStatus.PathPartial)
        {
            //   67 78 89 910 1011 1213 1314 1415 1516 1617 1718 1920 2021
            Debug.Log("�ƶ�״̬�쳣");
            return State.Failure;
        }
        else
        {
            Debug.Log("�ƶ�״̬����");
            return State.Success;
        }
     
    }
}
