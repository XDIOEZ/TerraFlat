using AYellowpaper.SerializedCollections;
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
        // 从后往前遍历，避免在迭代时删除元素导致的问题
        for (int i = UpdateSkillList.Count - 1; i >= 0; i--)
        {
            RuntimeSkill skill = UpdateSkillList[i];
            skill.Stay(deltaTime);

            // 如果技能已完成，移除它
            if (skill.IsFinished())
            {
                skill.Stop();
                UpdateSkillList.RemoveAt(i);
            }
        }
    }
    public override void Load()
    {
        if (item == null)
        {
            Debug.Log("[Mod_SkillManager]item为空,如果此石头不是在玩家手上的话这是正常现象");
            return;
        }
        if (item.Owner != null)
        {
            focusPoint = item.Owner.itemMods.GetMod_ByID<Mod_FocusPoint>(ModText.FocusPoint);
            if (focusPoint == null)
            {
                //        Debug.LogError("FocusPoint 为空，请检查技能配置！");
            }
            controller = item.Owner.itemMods.GetMod_ByID<GameController>(ModText.Controller);
            if (controller != null)
                controller.RightClick += Act;

            // 通过SkillNameList从GameRes获取技能 替换skillDataList中的技能
            // 确保skillDataList的大小与SkillNameList一致
            while (skillDataList.Count < SkillNameList.Count)
            {
                skillDataList.Add(null);
            }

            // 替换对应位置的技能
            for (int i = 0; i < SkillNameList.Count; i++)
            {
                BaseSkill skill = GameRes.Instance.GetSkill(SkillNameList[i]);
                if (skill != null)
                {
                    skillDataList[i] = skill;
                }
                else
                {
                    Debug.LogError($"无法找到技能: {SkillNameList[i]}");
                    skillDataList[i] = null;
                }
            }

            // 如果SkillNameList变短了，移除多余的技能
            while (skillDataList.Count > SkillNameList.Count)
            {
                skillDataList.RemoveAt(skillDataList.Count - 1);
            }

            ModSaveData.ReadData(ref Data);

            // 提前清理子对象，避免重复创建
            ClearCastingPoints();

            // 根据SkillNameList中技能的数量生成施法点位，默认位置为(0,0)
            for (int i = 0; i < SkillNameList.Count; i++)
            {
                string skillName = SkillNameList[i];

                // 如果SerializedcastingPointOffset中没有对应的偏移量，则使用默认值(0,0)
                Vector2 localPositionOffset = Vector2.zero;
                if (SerializedcastingPointOffset != null && SerializedcastingPointOffset.ContainsKey(skillName))
                {
                    localPositionOffset = SerializedcastingPointOffset[skillName];

                }
                else
                {
                    SerializedcastingPointOffset[skillName] = localPositionOffset;
                    //                Debug.LogWarning($"未找到技能 {skillName} 的偏移量，使用默认值(0,0)");
                }

                // 创建新的 GameObject 作为施法点位
                GameObject castingPointObject = new GameObject(skillName + "_CastingPoint");

                // 设置为当前 GameObject 的子对象
                castingPointObject.transform.SetParent(transform, false);

                // 设置本地坐标（相对于父对象的位置）
                castingPointObject.transform.localPosition = new Vector3(localPositionOffset.x, localPositionOffset.y, 0);

                // 存储到 castingPoint 字典中
                castingPoint[skillName] = castingPointObject.transform;
            }

            // 检查施法点配置
            CheckCastingPointConfiguration();


            transform.localPosition = Vector3.zero;
            //添加点位到旋转体控制组件 子对象施法点会随着一起旋转
            if (item.Owner.itemMods != null)
            {
                Mod_TurnBack turnBodyMod = item.Owner.itemMods.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);
                if (turnBodyMod != null)
                {
                    turnBodyMod.AddControlledTransform(transform);
                }
                else
                {
                    //                Debug.LogWarning("没有找到旋转体控制组件");
                }
            }
            else
            {
                Debug.LogWarning("itemMods为空，无法查找旋转体控制组件");
            }
        }



    }

    public override void Save()
    {
        if (item == null)
        {
            Debug.LogError("Mod_SkillManager_Item: item is null!");
            return;
        }
        StopAllSkills();
        castingPoint.Clear();
        ModSaveData.WriteData(Data);
        item.itemData.ModuleDataDic[_Data.Name] = _Data;
    }

    public override void Act()
    {
        if (CurrentSelectSkilIndex >= 0 && CurrentSelectSkilIndex < skillDataList.Count)
        {
            BaseSkill selectedSkill = skillDataList[CurrentSelectSkilIndex];
            // 创建运行时技能实例
            RuntimeSkill runtimeSkill = new RuntimeSkill();
            runtimeSkill.skillManager = this;
            runtimeSkill.skillData = selectedSkill;
            runtimeSkill.duration = selectedSkill.duration; // 假设BaseSkill有Duration属性
            runtimeSkill.progress = selectedSkill.initialPrograss; // 假设BaseSkill有Duration属性
            runtimeSkill.skillSender = item;

            if (focusPoint != null)
            {
                runtimeSkill.targetPoint = focusPoint.Data.DefaultSkill_Point;
            }
            else
            {
                //为空默认指向自己
                runtimeSkill.targetPoint = item.transform.position;
            }

            UpdateSkillList.Add(runtimeSkill);
            runtimeSkill.Start();
        }
        else
        {
            Debug.LogWarning($"无效的技能索引: {CurrentSelectSkilIndex}");
        }
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