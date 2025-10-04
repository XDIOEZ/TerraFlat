using UltEvents;
using UnityEngine;

public class Mod_AnimatorReceiver : Module
{
    public bool IsAttacking;
    private bool lastIsAttacking;
    
    [Tooltip("上一次的CanUseSkill状态")]
    private bool lastCanUseSkill;

    [Tooltip("技能ID")]
    public int SkillId;
    [Tooltip("是否能使用技能")]
    public bool CanUseSkill;

    public UltEvent OnAttackStart = new UltEvent();
    public UltEvent OnAttackStop = new UltEvent();
    public UltEvent<int> OnSkillStart = new ();
    public UltEvent<int> OnSkillStop = new ();

    public override void Awake()
    {
          _Data.ID = ModText.AnimatorReceiver;
    }
    
    void Update()
    {
        // 检测CanUseSkill的变化 如果为true就执行对应的SkillId
        if (CanUseSkill != lastCanUseSkill)
        {
            if (CanUseSkill)
            {
                // 可以使用技能，触发技能开始事件，并传递SkillId
                OnSkillStart.Invoke(SkillId);
            }
            else
            {
                // 技能使用结束，触发技能停止事件，并传递SkillId
                OnSkillStop.Invoke(SkillId);
            }
            
            // 更新上一次的CanUseSkill状态
            lastCanUseSkill = CanUseSkill;
        }

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
        // 初始化lastCanUseSkill状态
        lastCanUseSkill = CanUseSkill;
        lastIsAttacking = IsAttacking;
    }

    public override void Save()
    {
    }
    
    public override void Act()
    {
        base.Act();
    }
}