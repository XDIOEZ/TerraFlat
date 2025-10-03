using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "Skill_Laser", menuName = "Skill/Skill_Laser", order = 0)]
public class Skill_Laser : BaseSkillAction
{
    [Tooltip("镭射线预制体")]
    public GameObject LaserPrefab;
    [Tooltip("镭射线击中点位特效预制体")]
    public GameObject LaserEffectPrefab;
    
    public override void StartExecuteSkill(RuntimeSkill Data)
    {
        // 实例化镭射线预制体并传递RuntimeSkill数据
        GameObject laserObject = Instantiate(LaserPrefab);
        Laser_Skill laserSkill = laserObject.GetComponent<Laser_Skill>();
        
        if (laserSkill != null)
        {
            // 传递运行时数据
            laserSkill.runtimeSkill = Data;
            laserSkill.laserEffectPrefab = LaserEffectPrefab;
            laserSkill.lineRenderer = laserObject.GetComponent<LineRenderer>();
            
            // 将激光对象存入字典
            Data.skillInstanceDict["Laser"] = laserObject.transform;
        }
        else
        {
            Debug.LogError("LaserPrefab上缺少Laser_Skill组件！");
        }
    }

    public override void StayExecuteSkill(RuntimeSkill Data, float deltaTime)
    {
        // 逻辑现在在Laser_Skill组件中处理
        Data.progress += deltaTime;
    }

    public override void StopExecuteSkill(RuntimeSkill Data)
    {
        // 销毁对象并从字典中移除
        if (Data.skillInstanceDict.ContainsKey("Laser"))
        {
            Destroy(Data.skillInstanceDict["Laser"].gameObject);
            Data.skillInstanceDict.Remove("Laser");
        }
    }
}