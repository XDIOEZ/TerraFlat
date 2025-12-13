using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class ModText
{
    #region A
    public static string Attacker = "攻击模块";
    public static string AnimatorReceiver = "动画接收模块";
    public static string AI = "AI";
    #endregion

    #region B
    public static string Bag = "背包模块";
    public static string Building = "建筑模块";
    public static string BuffManager = "BuffManager";
    #endregion

    #region C
    public static string Camera = "相机模块";
    public static string Composite = "组合模块";
    public static string Controller = "Controller模块";
    public static string ColdWeapon = "冷兵器攻击模块";
    
    public static string ChunkLoader = "区块加载模块";
    #endregion

    #region D
    public static string Defense = "防御模块";
    public static string Drop = "掉落模块";
    public static string DeathLoot = "死亡掉落模块";
    #endregion

    #region E
    public static string Equipment = "装备系统";
    #endregion

    #region F
    public static string FocusPoint = "FaceMouse模块";
    public static string Food = "食物模块";
    public static string Fuel = "燃料模块";
    public static string Furnace = "熔炉模块";
    #endregion
    #region G
    public static string Grow = "生长模块";
    #endregion


    #region H
    public static string Hand = "UI_手部插槽";
    public static string Hotbar = "UI_快捷栏";
    public static string Hp = "生命值系统模块";
    #endregion

    #region I
    public static string Picker = "物品拾取模块";
    public static string Interact = "Module_Interaction";//交互模块
    public static string ItemDorper = "物品放置模块";
    #endregion

    #region M
    public static string MoveSpeed = "移动模块";
    public static string Mover = "移动模块";
    public static string Mover_AI = "移动模块_AI";

    #endregion

    #region R
    public static string Run = "奔跑模块";
    #endregion

    #region S
    public static string Smelting = "熔炼模块";
    public static string Stamina = "耐力模块";
    public static string Scene = "场景模块";

    public static string SkillManager_Item = "技能管理器_物品";
    #endregion

    #region T
    public static string TrunBody = "TrunBody";
    public static string TileEffectReceiver = "TileReciver";
    #endregion
    #region W
    public static string WorkBench = "工作台模块";
    #endregion
}
public static class AnimationText
{
    public static string Idle = "Idle";
    public static string Move = "Move";
    public static string Run = "Run";
    public static string Attack = "Attack";
}

public enum DamageTag
{
    物理,
    魔法
}