using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UltEvents;
using UnityEngine;
using UnityEngine.AI;

public class Mover_AI : MonoBehaviour,IMover,IAI_NavMesh
{
    public Vector3 targetPosition;
    public Transform target;
    public NavMeshAgent agent;

    public bool isMoving;

    public UltEvent OnStartMoving { get; set; }
    public UltEvent OnStopMoving { get; set; }
    public Vector3 Position { get { return transform.position; } set { transform.position = value; } }
    public Vector3 TargetPosition
    {
        get
        {
            return targetPosition;
        }
        set
        {
            targetPosition = value;
            agent.SetDestination(value);
            //Debug.Log("New target position: " + value);

            // 如果路径无效（如目标不可达），返回失败
            if (agent.pathStatus == NavMeshPathStatus.PathPartial)
            {
                //   67 78 89 910 1011 1213 1314 1415 1516 1617 1718 1920 2021
                Debug.Log("移动状态异常");
                
            }
            else
            {
                //Debug.Log("移动状态正常");
            }
        }
    }
    public bool IsMoving
    {
        get
        {
            return isMoving;
        }

        set
        {
            isMoving = value;
        }
    }

    public float Speed
    {
        get
        {
            return agent.speed;
        }

        set
        {
            agent.speed = value;
        }
    }

    public NavMeshAgent Agent_Nav { get => agent; set => agent = value; }

    void Start()
    {
        agent= GetComponent<NavMeshAgent>();
        agent.updateUpAxis = false;
        agent.updateRotation = false;
    }

    public void FixedUpdate()
    {
        //Move();
    }
    [Button("检查目标是否可以到达")]
    public void CheckTargetCanArrive()
    {
        // 如果路径无效（如目标不可达），返回失败
        if (agent.pathStatus == NavMeshPathStatus.PathPartial)
        {
            //   67 78 89 910 1011 1213 1314 1415 1516 1617 1718 1920 2021
            Debug.Log("移动状态异常");

        }
        else
        {
            Debug.Log("移动状态正常");
        }
    }

    public void Move()
    {
        if (Vector2.Distance(transform.position, TargetPosition) <= agent.stoppingDistance)
        {
            IsMoving = false;
            Debug.Log("Arrived");
            OnStopMoving.Invoke();
            return;
        }

        if (target != null)
        {
            TargetPosition = target.position;
        }

        agent.SetDestination(TargetPosition);
        //print("Moving to " + TargetPosition);
      //  print("Distance to target: " + Vector2.Distance(transform.position, TargetPosition));
        if (IsMoving == false)
        {
            OnStartMoving.Invoke();
            IsMoving = true;
        }
    }
}