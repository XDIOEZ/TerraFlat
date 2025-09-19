using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_MoveSpeed : Module, IItemValueModifier
{
    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; } set { SaveData = (Ex_ModData_MemoryPackable)value; } }

    public GameValue_float moveSpeed = new GameValue_float(1.2f); // 默认加速倍率，例如1.2倍

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.MoveSpeed; // 确保 ModText 有这个字段
        }
    }

    // 原Load逻辑迁移到Equip，装备时应用速度加成
    public void Equip(Item EquipmentOwner, ItemData Equipment)
    {
        // 读取保存的数据
        SaveData.ReadData(ref moveSpeed);

        // 应用移动速度加成
        var movement = GetMover();
        if (movement != null && moveSpeed.Value != 0)
        {
            movement.Data.Speed.MultiplicativeModifier *= moveSpeed.Value;
        }
    }

    // 原Save逻辑迁移到Unequip，卸下时还原速度并保存数据
    public void Unequip(Item EquipmentOwner, ItemData Equipment)
    {
        // 还原移动速度加成
        var movement = GetMover();
        if (movement != null && moveSpeed.Value != 0)
        {
            movement.Data.Speed.MultiplicativeModifier /= moveSpeed.Value;
        }

        // 保存当前数据
        SaveData.WriteData(moveSpeed);
        // Tip: 此模块是数值控制模块，不需要将其保存到 itemdata.moddata 中
    }

    // 保留基类方法但清空实现，因为逻辑已迁移到接口方法
    public override void Load()
    {
        // 逻辑已迁移到Equip方法，装备时触发
    }

    public override void Save()
    {
        // 逻辑已迁移到Unequip方法，卸下时触发
    }

    // 提取重复逻辑：获取 Mover 模块
    private Mover GetMover()
    {
        if (item?.itemMods != null && item.itemMods.ContainsKey_ID(ModText.Mover))
        {
            return item.itemMods.GetMod_ByID(ModText.Mover) as Mover;
        }
        return null;
    }
}

// 数值修改接口
public interface IItemValueModifier
{
    public void Equip(Item EquipmentOwner, ItemData Equipment);
    public void Unequip(Item EquipmentOwner, ItemData Equipment);
}