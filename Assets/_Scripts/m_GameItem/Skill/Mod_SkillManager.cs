using AYellowpaper.SerializedCollections;
using Org.BouncyCastle.Ocsp;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.XR;

public class Mod_SkillManager : Module
{
    #region ��������

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    public float Data;
    #endregion
    #region ģ�����

    public int CurrentSelectSkilIndex;
    [Tooltip("���������б�(���ڴ浵���ӵ�еķ���)")]
    public List<string> SkillNameList = new List<string>();
    [Tooltip("���������б�(����,�������)")]
    public List<BaseSkill> skillDataList = new List<BaseSkill>();
    [Tooltip("�����б�(������ʾ���ܶ���,��ִ�м�����Ϊ)")]
    public List<RuntimeSkill> UpdateSkillList = new List<RuntimeSkill>();
    [Tooltip("�۽���λ")]
    public Mod_FocusPoint focusPoint;
    [Tooltip("������")]
    public PlayerController controller;
    [Tooltip("ʩ�����")]
    public SerializedDictionary<string, Vector2> castingPointOffset = new();


    #endregion

    #region ��������

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Grow;
        }
    }

public override void Load()
{
    focusPoint = item.itemMods.GetMod_ByID<Mod_FocusPoint>(ModText.FocusPoint);

    controller = item.itemMods.GetMod_ByID<PlayerController>(ModText.Controller);
    if (controller != null)
        controller.RightClick += Act;
    
    // ͨ��SkillNameList��GameRes��ȡ���� �滻skillDataList�еļ���
    // ȷ��skillDataList�Ĵ�С��SkillNameListһ��
    while (skillDataList.Count < SkillNameList.Count)
    {
        skillDataList.Add(null);
    }
    
    // �滻��Ӧλ�õļ���
    for (int i = 0; i < SkillNameList.Count; i++)
    {
        BaseSkill skill = GameRes.Instance.GetSkill(SkillNameList[i]);
        if (skill != null)
        {
            skillDataList[i] = skill;
        }
        else
        {
            Debug.LogError($"�޷��ҵ�����: {SkillNameList[i]}");
            skillDataList[i] = null;
        }
    }
    
    // ���SkillNameList����ˣ��Ƴ�����ļ���
    while (skillDataList.Count > SkillNameList.Count)
    {
        skillDataList.RemoveAt(skillDataList.Count - 1);
    }
    
    ModSaveData.ReadData(ref Data);
}
    
    public override void ModUpdate(float deltaTime)
    {
        // �Ӻ���ǰ�����������ڵ���ʱɾ��Ԫ�ص��µ�����
        for (int i = UpdateSkillList.Count - 1; i >= 0; i--)
        {
            RuntimeSkill skill = UpdateSkillList[i];
            skill.Stay(deltaTime);
            
            // �����������ɣ��Ƴ���
            if (skill.IsFinished())
            {
                skill.Stop();
                UpdateSkillList.RemoveAt(i);
            }
        }
    }
    
    public override void Save()
    {
        ModSaveData.WriteData(Data);
    }
    
    public override void Act()
    {
        if (CurrentSelectSkilIndex >= 0 && CurrentSelectSkilIndex < skillDataList.Count)
        {
            BaseSkill selectedSkill = skillDataList[CurrentSelectSkilIndex];
            // ��������ʱ����ʵ��
            RuntimeSkill runtimeSkill = new RuntimeSkill();
            runtimeSkill.skillManager = this;
            runtimeSkill.skillData = selectedSkill;
            runtimeSkill.duration = selectedSkill.duration; // ����BaseSkill��Duration����
            runtimeSkill.progress = selectedSkill.initialPrograss; // ����BaseSkill��Duration����
            runtimeSkill.skillSender = item;
            runtimeSkill.targetPoint = focusPoint.Data.DefaultSkill_Point;
            UpdateSkillList.Add(runtimeSkill);
            runtimeSkill.Start();
        }
        else
        {
            Debug.LogWarning($"��Ч�ļ�������: {CurrentSelectSkilIndex}");
        }
    }
    
    #endregion
    
    #region ���ܿ��Ʒ���
    
    /// <summary>
    /// ǿ��ֹͣ��������ִ�еļ���
    /// </summary>
    public void StopAllSkills()
    {
        // �Ӻ���ǰ�����������ڵ���ʱɾ��Ԫ�ص��µ�����
        for (int i = UpdateSkillList.Count - 1; i >= 0; i--)
        {
            RuntimeSkill skill = UpdateSkillList[i];
            // ֹͣ����
            skill.Stop();
            // ���б����Ƴ�
            UpdateSkillList.RemoveAt(i);
        }
        
        Debug.Log("��ǿ��ֹͣ���м���");
    }
    
    /// <summary>
    /// ǿ��ָֹͣ�������ļ���
    /// </summary>
    /// <param name="index">������UpdateSkillList�е�����</param>
    public void StopSkillByIndex(int index)
    {
        if (index >= 0 && index < UpdateSkillList.Count)
        {
            RuntimeSkill skill = UpdateSkillList[index];
            // ֹͣ����
            skill.Stop();
            // ���б����Ƴ�
            UpdateSkillList.RemoveAt(index);
            
            Debug.Log($"��ǿ��ֹͣ����Ϊ {index} �ļ���");
        }
        else
        {
            Debug.LogWarning($"��Ч�ļ�������: {index}");
        }
    }
    
    /// <summary>
    /// ǿ��ָֹͣ�����Ƶļ���
    /// </summary>
    /// <param name="skillName">��������</param>
    public void StopSkillByName(string skillName)
    {
        // �Ӻ���ǰ�����������ڵ���ʱɾ��Ԫ�ص��µ�����
        for (int i = UpdateSkillList.Count - 1; i >= 0; i--)
        {
            RuntimeSkill skill = UpdateSkillList[i];
            // ��鼼�������Ƿ�ƥ��
            if (skill.skillData != null && skill.skillData.name == skillName)
            {
                // ֹͣ����
                skill.Stop();
                // ���б����Ƴ�
                UpdateSkillList.RemoveAt(i);
                
                Debug.Log($"��ǿ��ֹͣ����Ϊ {skillName} �ļ���");
                return; // �ҵ���ֹͣ��һ��ƥ��ļ��ܺ󷵻�
            }
        }
        
        Debug.LogWarning($"δ�ҵ�����Ϊ {skillName} �ļ���");
    }
    
    /// <summary>
    /// ǿ��ֹͣ��ǰѡ��ļ���
    /// </summary>
    public void StopCurrentSelectedSkill()
    {
        if (CurrentSelectSkilIndex >= 0 && CurrentSelectSkilIndex < skillDataList.Count)
        {
            BaseSkill selectedSkill = skillDataList[CurrentSelectSkilIndex];
            if (selectedSkill != null)
            {
                // ���Ҳ�ֹͣ��Ӧ������ʱ����
                for (int i = UpdateSkillList.Count - 1; i >= 0; i--)
                {
                    RuntimeSkill runtimeSkill = UpdateSkillList[i];
                    if (runtimeSkill.skillData == selectedSkill)
                    {
                        // ֹͣ����
                        runtimeSkill.Stop();
                        // ���б����Ƴ�
                        UpdateSkillList.RemoveAt(i);
                        
                        Debug.Log($"��ǿ��ֹͣ��ǰѡ��ļ���: {selectedSkill.name}");
                        return;
                    }
                }
                
                Debug.LogWarning($"��ǰѡ��ļ��� {selectedSkill.name} δ��������");
            }
        }
        else
        {
            Debug.LogWarning($"��Ч�ļ�������: {CurrentSelectSkilIndex}");
        }
    }
    
    #endregion
}