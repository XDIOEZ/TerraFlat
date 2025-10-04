using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "New Skill", menuName = "Skill/Skill_Test", order = 0)]
public class Skill_Fireball : BaseSkillAction
{
    [Tooltip("���ܿ�ʼִ��ʱ�ĵ�����Ϣ")]
    public string StartDebugTest = "���ܿ�ʼִ��";

    [Tooltip("����")]
    public GameObject Fireball;
    
    [Tooltip("����ִ��ֹͣʱ�ĵ�����Ϣ")]
    public string StopDebugTest = "����ִ��ֹͣ";
    
    // �洢����ĳ�ʼ���з���
    private Vector2 fireballDirection = Vector2.zero;

    public override void StartExecuteSkill(RuntimeSkill runtimeSkill)
    {
        // ʹ����ɫ��ʾ��ʼ������Ϣ
        Debug.Log($"<color=green>{StartDebugTest}</color>");
        runtimeSkill.skillInstanceDict["Fireball"] = 
            Instantiate(Fireball).transform;

        // ʵ����λ������(Ŀ��λ��)�ƶ�1����λ������Լ�����˺�
        Vector2 spawnPosition = (Vector2)runtimeSkill.skillManager.transform.position + fireballDirection * 2;

        spawnPosition += runtimeSkill.skillManager.castingPointOffset["Fireball"];

       // ���㲢�洢����ĳ�ʼ���з���
        fireballDirection = (runtimeSkill.targetPoint - spawnPosition).normalized;

        runtimeSkill.skillInstanceDict["Fireball"].transform.position = new Vector3(spawnPosition.x, spawnPosition.y, runtimeSkill.skillSender.transform.position.z);
        
        // ���û����ʼ����
        if (fireballDirection != Vector2.zero)
        {
            float angle = Mathf.Atan2(fireballDirection.y, fireballDirection.x) * Mathf.Rad2Deg;
            runtimeSkill.skillInstanceDict["Fireball"].rotation = Quaternion.Euler(0, 0, angle);
        }
    }

    public override void StayExecuteSkill(RuntimeSkill Data,float deltaTime)
    {
       // Debug.Log($"<color=yellow>��ǰ����:{Data.progress}/{Data.duration}</color>");
       // Debug.Log($"<color=yellow>����:{Data.skillInstanceDict["Fireball"].name}</color>");
        
        // ֱ�ӿ���2D�����ƶ������ճ�ʼ����ֱ�߷���
        Vector3 currentPosition = Data.skillInstanceDict["Fireball"].position;
        
        // �����ٶȺ�ʱ������ƶ�����
        float moveDistance = Data.skillData.speed * deltaTime;
        
        // ����ʼ������ٶ��ƶ�����
        Vector2 newPosition = currentPosition + (Vector3)(fireballDirection * moveDistance);
        Data.skillInstanceDict["Fireball"].position = new Vector3(newPosition.x, newPosition.y, currentPosition.z);
        
        // ��ѡ�����ݳ���ʱ������������
        Data.progress += deltaTime;
    }

    public override void StopExecuteSkill(RuntimeSkill runtimeSkill)
    {
        // ʹ�ú�ɫ��ʾֹͣ������Ϣ
        Debug.Log($"<color=red>{StopDebugTest}</color>");
        Destroy(runtimeSkill.skillInstanceDict["Fireball"].gameObject);
        // ���÷���
        fireballDirection = Vector2.zero;
    }
}