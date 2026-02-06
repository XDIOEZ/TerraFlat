using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class Mod_AnimationAction : MonoBehaviour
{
    public Animator animator;//武器的动画树

    [Button]
    public void PlayAttackAnimation(string animationName)
    {
        animator.Play(animationName);
    }
}
