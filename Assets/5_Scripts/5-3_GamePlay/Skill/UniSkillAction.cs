using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "SkillAction_InstantiatePrefab", menuName = "Skill/SkillAction_InstantiatePrefab", order = 0)]
public class UniSkillAction : BaseSkillAction
{
    [Tooltip("预制体")]
    public GameObject SkillPrefab;

    public override void StartExecuteSkill(RuntimeSkill Data)
    {
        // 实例化镭射线预制体并传递RuntimeSkill数据
        GameObject laserObject = Instantiate(SkillPrefab);
    }

    public override void StayExecuteSkill(RuntimeSkill Data, float deltaTime)
    {
        // 逻辑现在在Laser_Skill组件中处理
        Data.progress += deltaTime;
    }

    public override void StopExecuteSkill(RuntimeSkill Data)
    {
    }
}