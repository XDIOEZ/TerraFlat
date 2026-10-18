using UltEvents;
using UnityEngine;

public class Mod_AnimatorController_Receiver : Mod_AnimatorController
{
    #region 动画语义事件
    public bool IsAttacking;
    public int SkillId;
    public bool CanUseSkill;

    public UltEvent OnAttackStart = new UltEvent();
    public UltEvent OnAttackStop = new UltEvent();
    public UltEvent<int> OnSkillStart = new UltEvent<int>();
    public UltEvent<int> OnSkillStop = new UltEvent<int>();

    private bool lastIsAttacking;
    private bool lastCanUseSkill;

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data
    {
        get => ModSaveData;
        set => ModSaveData = (Ex_ModData_MemoryPackable)value;
    }
    #endregion

    #region 事件接收与生命周期
    private void Update()
    {
        if (!CanAdvancePresentation)
            return;

        if (IsDeadPresentation)
            IsAttacking = CanUseSkill = false;

        if (CanUseSkill != lastCanUseSkill)
        {
            lastCanUseSkill = CanUseSkill;
            if (CanUseSkill)
                OnSkillStart?.Invoke(SkillId);
            else
                OnSkillStop?.Invoke(SkillId);
        }

        if (IsAttacking != lastIsAttacking)
        {
            // 攻击事件只描述命中窗口，身体动作由实际攻击起手统一触发。
            lastIsAttacking = IsAttacking;
            if (IsAttacking)
                OnAttackStart?.Invoke();
            else
                OnAttackStop?.Invoke();
        }
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        lastCanUseSkill = CanUseSkill;
        lastIsAttacking = IsAttacking;
    }

    protected override void OnUnload()
    {
        base.OnUnload();
        IsAttacking = lastIsAttacking = false;
        CanUseSkill = lastCanUseSkill = false;
        SkillId = 0;
    }
    #endregion
}
