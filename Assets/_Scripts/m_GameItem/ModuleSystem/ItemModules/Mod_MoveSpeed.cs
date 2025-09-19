using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_MoveSpeed : Module, IItemValueModifier
{
    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; } set { SaveData = (Ex_ModData_MemoryPackable)value; } }

    public GameValue_float moveSpeed = new GameValue_float(1.2f); // Ĭ�ϼ��ٱ��ʣ�����1.2��

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.MoveSpeed; // ȷ�� ModText ������ֶ�
        }
    }

    // ԭLoad�߼�Ǩ�Ƶ�Equip��װ��ʱӦ���ٶȼӳ�
    public void Equip(Item EquipmentOwner, ItemData Equipment)
    {
        // ��ȡ���������
        SaveData.ReadData(ref moveSpeed);

        // Ӧ���ƶ��ٶȼӳ�
        var movement = GetMover();
        if (movement != null && moveSpeed.Value != 0)
        {
            movement.Data.Speed.MultiplicativeModifier *= moveSpeed.Value;
        }
    }

    // ԭSave�߼�Ǩ�Ƶ�Unequip��ж��ʱ��ԭ�ٶȲ���������
    public void Unequip(Item EquipmentOwner, ItemData Equipment)
    {
        // ��ԭ�ƶ��ٶȼӳ�
        var movement = GetMover();
        if (movement != null && moveSpeed.Value != 0)
        {
            movement.Data.Speed.MultiplicativeModifier /= moveSpeed.Value;
        }

        // ���浱ǰ����
        SaveData.WriteData(moveSpeed);
        // Tip: ��ģ������ֵ����ģ�飬����Ҫ���䱣�浽 itemdata.moddata ��
    }

    // �������෽�������ʵ�֣���Ϊ�߼���Ǩ�Ƶ��ӿڷ���
    public override void Load()
    {
        // �߼���Ǩ�Ƶ�Equip������װ��ʱ����
    }

    public override void Save()
    {
        // �߼���Ǩ�Ƶ�Unequip������ж��ʱ����
    }

    // ��ȡ�ظ��߼�����ȡ Mover ģ��
    private Mover GetMover()
    {
        if (item?.itemMods != null && item.itemMods.ContainsKey_ID(ModText.Mover))
        {
            return item.itemMods.GetMod_ByID(ModText.Mover) as Mover;
        }
        return null;
    }
}

// ��ֵ�޸Ľӿ�
public interface IItemValueModifier
{
    public void Equip(Item EquipmentOwner, ItemData Equipment);
    public void Unequip(Item EquipmentOwner, ItemData Equipment);
}