using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntityAnimationController : MonoBehaviour
{
    public Animator animator;

    public Mover mover;

    public void Awake()
    {
        animator = GetComponent<Animator>();
        //获取同一层级物体中的Mover组件
        mover = transform.parent.GetComponentInChildren<Mover>();

    }

    public void Start()
    {
        mover.OnMoveStart += () => Move_Anim(true);
        mover.OnMoveEnd += () => Move_Anim(false);
    }

    public void Move_Anim(bool isMoving)
    {
        animator.SetBool("IsMoving", isMoving);
    }

    public void Run_Anim(bool isRunning)
    {
        animator.SetBool("IsRunning", isRunning);
    }
}
