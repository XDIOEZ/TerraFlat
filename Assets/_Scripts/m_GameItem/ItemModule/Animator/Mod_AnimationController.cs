using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_AnimationController : Module
{
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    public Animator animator;
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Animation;
        }
    }

    public override void Load()
    {
        animator = GetComponent<Animator>();
      //  throw new System.NotImplementedException();
    }

    public override void Save()
    {
       // throw new System.NotImplementedException();
    }
    public override void ModUpdate(float deltaTime)
    {

    }

    [Button]
    public void PlayAnimation( string animationName )
    {
          animator.Play( animationName );
    }

    [Button]
    public void ForcePlayAnimation(string animationName, int layer = 0)
    {
        if (animator == null) return;

        // 强制重置动画状态并播放
        animator.Play(animationName, layer, 0f);
    }
    [Button]
    public void SetBool(string parameterName, bool value)
    {
        animator.SetBool(parameterName, value);
    }
}
