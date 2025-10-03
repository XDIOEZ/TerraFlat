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

    public override void StartExecuteSkill(RuntimeSkill runtimeSkill)
    {
        // ʹ����ɫ��ʾ��ʼ������Ϣ
        Debug.Log($"<color=green>{StartDebugTest}</color>");
        runtimeSkill.skillInstanceDict["Fireball"] = 
            Instantiate(Fireball).transform;
        
        // ʵ����λ������(Ŀ��λ��)�ƶ�1����λ������Լ�����˺�
        Vector2 direction = (runtimeSkill.targetPoint - (Vector2)runtimeSkill.skillSender.transform.position).normalized;
        Vector2 spawnPosition = (Vector2)runtimeSkill.skillSender.transform.position + direction*2;
        runtimeSkill.skillInstanceDict["Fireball"].transform.position = new Vector3(spawnPosition.x, spawnPosition.y, runtimeSkill.skillSender.transform.position.z);
    }

    public override void StayExecuteSkill(RuntimeSkill Data,float deltaTime)
    {
       // Debug.Log($"<color=yellow>��ǰ����:{Data.progress}/{Data.duration}</color>");
       // Debug.Log($"<color=yellow>����:{Data.skillInstanceDict["Fireball"].name}</color>");
        
        // ֱ�ӿ���2D�����ƶ�������Ŀ�����������
        Vector3 currentPosition = Data.skillInstanceDict["Fireball"].position;
        Vector2 targetPosition = Data.targetPoint;
        
        // �����ƶ�����ʼ�ճ���Ŀ��㣬��ʩ����λ�ü��㣩
        Vector2 direction = (targetPosition - (Vector2)Data.skillSender.transform.position).normalized;
        
        // �����ٶȺ�ʱ������ƶ�����
        float moveDistance = Data.skillData.speed * deltaTime;
        
        // ��������ٶ��ƶ�����
        Vector2 newPosition = currentPosition + (Vector3)(direction * moveDistance);
        Data.skillInstanceDict["Fireball"].position = new Vector3(newPosition.x, newPosition.y, currentPosition.z);
        
        // ��ѡ�����ݳ���ʱ������������
        Data.progress += deltaTime;
    }

    public override void StopExecuteSkill(RuntimeSkill runtimeSkill)
    {
        // ʹ�ú�ɫ��ʾֹͣ������Ϣ
        Debug.Log($"<color=red>{StopDebugTest}</color>");
        Destroy(runtimeSkill.skillInstanceDict["Fireball"].gameObject);
    }
}