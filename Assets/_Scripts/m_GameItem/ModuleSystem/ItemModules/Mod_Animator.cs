using UltEvents;
using UnityEngine;

public class Mod_Animator : Module
{
    public Animator animator;
    public bool IsAttacking;
    private bool lastIsAttacking;

    public UltEvent OnAttackStart = new UltEvent();
    public UltEvent OnAttackStop = new UltEvent();

    public override void Awake()
    {
          _Data.ID = ModText.Animator;
    }
    void Update()
    {
        // 检测攻击状态变化
        if (IsAttacking != lastIsAttacking)
        {
            if (IsAttacking)
            {
                // 攻击开始
                OnAttackStart.Invoke();
            }
            else
            {
                // 攻击结束
                OnAttackStop.Invoke();
            }

            // 更新上一次攻击状态
            lastIsAttacking = IsAttacking;
        }
    }

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }



    public override void Load()
    {
    }

    public override void Save()
    {
    }
    public override void Act()
    {
        base.Act();
    }
}