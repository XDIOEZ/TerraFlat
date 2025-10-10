using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationController : MonoBehaviour
{
    [ShowNonSerializedField]
    public IMover mover;
    public Animator animator;
    // Start is called before the first frame update
    void Start()
    {
        mover = GetComponent<IMover>();
        animator = GetComponentInChildren<Animator>();

        mover.OnStartMoving += Move;
        mover.OnStopMoving += Idle;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Idle()
    {
        animator.SetBool("Move", false);

    }

    public void Move()
    {
        animator.SetBool("Move", true);
    }

    public void Sit()
    {
        animator.SetBool("Sit", true);
    }
    public void Stand()
    {
        animator.SetBool("Sit", false);
    }
}
