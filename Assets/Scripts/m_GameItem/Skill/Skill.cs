using UnityEngine;
using System.Collections;

public class Skill : MonoBehaviour
{
    [Header("运行时数据")]
    public RuntimeSkill runtimeSkill;

    public virtual void Load()
    {

    }

    public virtual void SkillUpdate(float deltaTime)
    {

    }
    public virtual void Save()
    {

    }
/*
    public override void Load()
    {

    }

    public override void SkillUpdate(float deltaTime)
    {

    }
    public override void Save()
    {

    }
*/
}