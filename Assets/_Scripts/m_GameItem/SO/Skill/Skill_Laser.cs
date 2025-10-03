using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "Skill_Laser", menuName = "Skill/Skill_Laser", order = 0)]
public class Skill_Laser : BaseSkillAction
{
    [Tooltip("������Ԥ����")]
    public GameObject LaserPrefab;
    [Tooltip("�����߻��е�λ��ЧԤ����")]
    public GameObject LaserEffectPrefab;
    
    public override void StartExecuteSkill(RuntimeSkill Data)
    {
        // ʵ����������Ԥ���岢����RuntimeSkill����
        GameObject laserObject = Instantiate(LaserPrefab);
        Laser_Skill laserSkill = laserObject.GetComponent<Laser_Skill>();
        
        if (laserSkill != null)
        {
            // ��������ʱ����
            laserSkill.runtimeSkill = Data;
            laserSkill.laserEffectPrefab = LaserEffectPrefab;
            laserSkill.lineRenderer = laserObject.GetComponent<LineRenderer>();
            
            // �������������ֵ�
            Data.skillInstanceDict["Laser"] = laserObject.transform;
        }
        else
        {
            Debug.LogError("LaserPrefab��ȱ��Laser_Skill�����");
        }
    }

    public override void StayExecuteSkill(RuntimeSkill Data, float deltaTime)
    {
        // �߼�������Laser_Skill����д���
        Data.progress += deltaTime;
    }

    public override void StopExecuteSkill(RuntimeSkill Data)
    {
        // ���ٶ��󲢴��ֵ����Ƴ�
        if (Data.skillInstanceDict.ContainsKey("Laser"))
        {
            Destroy(Data.skillInstanceDict["Laser"].gameObject);
            Data.skillInstanceDict.Remove("Laser");
        }
    }
}