using UltEvents;
using UnityEngine;

public class Mod_AnimatorReceiver : Module
{
    public bool IsAttacking;
    private bool lastIsAttacking;
    
    [Tooltip("��һ�ε�CanUseSkill״̬")]
    private bool lastCanUseSkill;

    [Tooltip("����ID")]
    public int SkillId;
    [Tooltip("�Ƿ���ʹ�ü���")]
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
        // ���CanUseSkill�ı仯 ���Ϊtrue��ִ�ж�Ӧ��SkillId
        if (CanUseSkill != lastCanUseSkill)
        {
            if (CanUseSkill)
            {
                // ����ʹ�ü��ܣ��������ܿ�ʼ�¼���������SkillId
                OnSkillStart.Invoke(SkillId);
            }
            else
            {
                // ����ʹ�ý�������������ֹͣ�¼���������SkillId
                OnSkillStop.Invoke(SkillId);
            }
            
            // ������һ�ε�CanUseSkill״̬
            lastCanUseSkill = CanUseSkill;
        }

        // ��⹥��״̬�仯
        if (IsAttacking != lastIsAttacking)
        {
            if (IsAttacking)
            {
                // ������ʼ
                OnAttackStart.Invoke();
            }
            else
            {
                // ��������
                OnAttackStop.Invoke();
            }

            // ������һ�ι���״̬
            lastIsAttacking = IsAttacking;
        }
    }

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }



    public override void Load()
    {
        // ��ʼ��lastCanUseSkill״̬
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