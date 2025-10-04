using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/���/����Ƿ����ʹ�ü���")]
public class AISkillCooldownTime : ActionNode
{
    [Header("��ȴʱ�䣨�룩")]
    public float cooldownTime = 1.0f;
    
    [Header("������Ϣ�����鿴��")]
    [SerializeField] private float cooldownStartTime = 0f;
    [SerializeField] private bool isCoolingDown = false;

    protected override void OnStart() 
    {
        // �����û�п�ʼ��ȴ����ʼ��ȴ��ʱ
        if (!isCoolingDown)
        {
            StartCooldown();
        }
    }

    protected override void OnStop() 
    {
    }

    protected override State OnUpdate() 
    {
        // ���û������ȴ�У�����Success
        if (!isCoolingDown)
        {
            return State.Success;
        }
        
        // �����ȴ�Ƿ����
        if (Time.time - cooldownStartTime >= cooldownTime)
        {
            // ��ȴ��ɣ�����״̬
            isCoolingDown = false;
            return State.Success;
        }
        
        // ������ȴ�У�����Failure
        return State.Failure;
    }
    
    // ��ʼ��ȴ��ʱ
    public void StartCooldown()
    {
        cooldownStartTime = Time.time;
        isCoolingDown = true;
    }
    
    // ����Ƿ�������ȴ��
    public bool IsCoolingDown()
    {
        if (isCoolingDown && Time.time - cooldownStartTime >= cooldownTime)
        {
            isCoolingDown = false; // �Զ���������ɵ���ȴ
        }
        return isCoolingDown;
    }
    
    // ��ȡʣ����ȴʱ��
    public float GetRemainingCooldownTime()
    {
        if (!isCoolingDown)
            return 0f;
            
        float remaining = cooldownTime - (Time.time - cooldownStartTime);
        return Mathf.Max(0f, remaining);
    }
    
    // ������ȴ
    public void ResetCooldown()
    {
        isCoolingDown = false;
    }
}