using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;
using MemoryPack;
[System.Serializable]
[MemoryPackable]
public partial class SkillManagerSaveData
{
    public List<string> SkillNames = new List<string>();
    public int CurrentSelectIndex = 0;
}
public class Mod_SkillManager : Module
{


    #region 基础参数
    public new virtual Item item => base.item;
    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    public SkillManagerSaveData Data = new SkillManagerSaveData();
    #endregion
    #region 模组参数

    public int CurrentSelectSkilIndex;
    [Tooltip("技能名称列表(用于存档玩家拥有的法术)")]
    public List<string> SkillNameList = new List<string>();
    [Tooltip("技能数据列表(缓存,方便调用)")]
    public List<BaseSkill> skillDataList = new List<BaseSkill>();
    [Tooltip("技能列表(用于显示技能动画,和执行技能行为)")]
    public List<RuntimeSkill> UpdateSkillList = new List<RuntimeSkill>();
    [Tooltip("聚焦点位")]
    public Mod_FocusPoint focusPoint;
    [Tooltip("控制器")]
    public GameController controller;
    [Tooltip("施法点列表(手动挂接,顺序A/B/C)")]
    public List<Transform> castingPoints = new List<Transform>(3);

    #endregion

    #region 常量

    private const int CastingPointCount = 3;
    private static readonly string[] CastingPointLabels = { "A", "B", "C" };

    #endregion

    #region 生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.SkillManager_Item;
        }
    }
    public override void Load()
    {
        item.itemData.ModuleDataDic[_Data.Name] = _Data;
        ModSaveData.ReadData(ref Data);
        if (Data == null)
        {
            Debug.LogWarning("SkillManager存档为空,已重置为默认");
            Data = new SkillManagerSaveData();
        }
        if (item == null)
        {
            Debug.Log("[Mod_SkillManager]item为空,如果此石头不是在玩家手上的话这是正常现象");
            return;
        }

        if (item.Owner != null && item.Owner.itemMods != null)
        {
            focusPoint = item.Owner.itemMods.GetMod_ByID<Mod_FocusPoint>(ModText.FocusPoint);
            controller = item.Owner.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        }

        if (focusPoint == null && item.itemMods != null)
        {
            focusPoint = item.itemMods.GetMod_ByID<Mod_FocusPoint>(ModText.FocusPoint);
        }

        if (controller == null && item.itemMods != null)
        {
            controller = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        }

        if (controller != null)
        {
            controller.RightClick += Act;
        }

        SkillNameList = new List<string>(Data.SkillNames);
        CurrentSelectSkilIndex = Data.CurrentSelectIndex;
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



        // 检查施法点配置
        CheckCastingPointConfiguration();


        transform.localPosition = Vector3.zero;
        //添加点位到旋转体控制组件 子对象施法点会随着一起旋转
        if (item.itemMods != null)
        {
            Mod_TurnBack turnBodyMod = item.itemMods.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);
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

    /// <summary>
    /// 检查施法点配置是否完整，并自动添加缺失的配置
    /// </summary>
    [Button("检查施法点配置")]
    public void CheckCastingPointConfiguration()
    {
        if (castingPoints.Count < CastingPointCount)
        {
            Debug.LogWarning($"施法点数量不足,需要{CastingPointCount}个(A/B/C),当前{castingPoints.Count}个");
            while (castingPoints.Count < CastingPointCount)
            {
                castingPoints.Add(null);
            }
        }

        for (int i = 0; i < CastingPointCount; i++)
        {
            if (castingPoints[i] == null)
            {
                Debug.LogWarning($"施法点未挂接: {CastingPointLabels[i]}");
            }
        }
    }

    public Transform GetCastingPoint(int index)
    {
        if (index < 0 || index >= castingPoints.Count)
        {
            Debug.LogWarning($"施法点索引无效: {index}");
            return null;
        }

        Transform point = castingPoints[index];
        if (point == null)
        {
            string label = index >= 0 && index < CastingPointLabels.Length ? CastingPointLabels[index] : index.ToString();
            Debug.LogWarning($"施法点未挂接: {label}");
        }

        return point;
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

    public override void Save()
    {
        if (item == null)
        {
            Debug.LogError("Mod_SkillManager_Item: item is null!");
            return;
        }
        StopAllSkills();
        Data.SkillNames.Clear();
        Data.SkillNames.AddRange(SkillNameList);
        Data.CurrentSelectIndex = CurrentSelectSkilIndex;
        ModSaveData.WriteData(Data);
        item.itemData.ModuleDataDic[_Data.Name] = _Data;
    }

    public override void Act()
    {
        if (CurrentSelectSkilIndex >= 0 && CurrentSelectSkilIndex < skillDataList.Count)
        {
            BaseSkill selectedSkill = skillDataList[CurrentSelectSkilIndex];
            if (selectedSkill == null)
            {
                Debug.LogWarning("当前选择的技能为空");
                return;
            }

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

    /// <summary>
    /// 强行停止当前选择的技能
    /// </summary>
    public void StopCurrentSelectedSkill()
    {
        if (CurrentSelectSkilIndex >= 0 && CurrentSelectSkilIndex < skillDataList.Count)
        {
            BaseSkill selectedSkill = skillDataList[CurrentSelectSkilIndex];
            if (selectedSkill != null)
            {
                // 查找并停止对应的运行时技能
                for (int i = UpdateSkillList.Count - 1; i >= 0; i--)
                {
                    RuntimeSkill runtimeSkill = UpdateSkillList[i];
                    if (runtimeSkill.skillData == selectedSkill)
                    {
                        // 停止技能
                        runtimeSkill.Stop();
                        // 从列表中移除
                        UpdateSkillList.RemoveAt(i);

                        Debug.Log($"已强行停止当前选择的技能: {selectedSkill.name}");
                        return;
                    }
                }

                Debug.LogWarning($"当前选择的技能 {selectedSkill.name} 未在运行中");
            }
        }
        else
        {
            Debug.LogWarning($"无效的技能索引: {CurrentSelectSkilIndex}");
        }
    }

    #endregion
}