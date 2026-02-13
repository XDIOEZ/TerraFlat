using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

public class Mod_SkillManager_Item : Mod_SkillManager
{

    #region 生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.SkillManager_Item;
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        base.ModUpdate(deltaTime);
    }
    public override void Load()
    {
        base.Load();
    }
    public override void Save()
    {
        base.Save();
    }

    public override void Act()
    {
        base.Act();
    }

    #endregion

    #region 技能控制方法

    /// <summary>
    /// 强行停止所有正在执行的技能
    /// </summary>
    public void StopAllSkills()
    {
        // 从后往前遍历，避免在迭代时删除元素导致的问题
        for (int i = UpdateSkillList.Count - 1; i >= 0; i--)
        {
            RuntimeSkill skill = UpdateSkillList[i];
            // 停止技能
            skill.Stop();
            // 从列表中移除
            UpdateSkillList.RemoveAt(i);
        }

        Debug.Log("已强行停止所有技能");
    }

    /// <summary>
    /// 强行停止指定索引的技能
    /// </summary>
    /// <param name="index">技能在UpdateSkillList中的索引</param>
    public void StopSkillByIndex(int index)
    {
        if (index >= 0 && index < UpdateSkillList.Count)
        {
            RuntimeSkill skill = UpdateSkillList[index];
            // 停止技能
            skill.Stop();
            // 从列表中移除
            UpdateSkillList.RemoveAt(index);

            Debug.Log($"已强行停止索引为 {index} 的技能");
        }
        else
        {
            Debug.LogWarning($"无效的技能索引: {index}");
        }
    }

    /// <summary>
    /// 强行停止指定名称的技能
    /// </summary>
    /// <param name="skillName">技能名称</param>
    public void StopSkillByName(string skillName)
    {
        // 从后往前遍历，避免在迭代时删除元素导致的问题
        for (int i = UpdateSkillList.Count - 1; i >= 0; i--)
        {
            RuntimeSkill skill = UpdateSkillList[i];
            // 检查技能名称是否匹配
            if (skill.skillData != null && skill.skillData.name == skillName)
            {
                // 停止技能
                skill.Stop();
                // 从列表中移除
                UpdateSkillList.RemoveAt(i);

                Debug.Log($"已强行停止名称为 {skillName} 的技能");
                return; // 找到并停止第一个匹配的技能后返回
            }
        }

        Debug.LogWarning($"未找到名称为 {skillName} 的技能");
    }

    #endregion
}