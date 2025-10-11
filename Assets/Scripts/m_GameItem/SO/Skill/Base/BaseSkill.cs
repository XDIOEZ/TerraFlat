using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;

[CreateAssetMenu(fileName = "BaseSkill", menuName = "Skill/BaseSkill")]
public class BaseSkill : ScriptableObject
{
    [Header("基础信息")]
    [Tooltip("技能名称")]
    public string skillName = "新技能";
    [Tooltip("技能描述")]
    public string description = "技能描述";
    [Tooltip("技能图标")]
    public Sprite icon;
    [Tooltip("技能持续时间")]
    public float duration = 2f;
    [Tooltip("技能初始持续时间")]
    public float initialPrograss = 0f;
    [Tooltip("技能速度(各种意义上的吧)")]
    public float speed = 1;
    [Tooltip("通用string参数")]
    public string stringParam;

    [Tooltip("技能行为")]
    [InlineEditor]
    public List<Skill> Actions;

}