using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Skill_Summon : Skill
{
    public string SummonItemName;
    public override void Load()
    {
        runtimeSkill.targetPoint = transform.position;
        SummonItemName = runtimeSkill.skillData.stringParam;
        ItemMgr.Instance.InstantiateItem(SummonItemName, runtimeSkill.targetPoint).Load();
    }

    public override void SkillUpdate(float deltaTime)
    {

    }
    public override void Save()
    {

    }
}
