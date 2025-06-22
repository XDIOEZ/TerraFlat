using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UltEvents;
using UnityEngine;

public class AppleTree : Item,IHealth,ISave_Load,ILoot
{
    #region 基础数据

    // 基础数据字段
    public Data_Creature _data;

    // 物品数据访问
    public override ItemData Item_Data { get => _data; set => _data = (Data_Creature)value; }

    #endregion

    #region 健康数据

    // 生命值属性
    public Hp Hp { get => _data.hp; set => _data.hp = value; }

    // 防御属性
    public Defense Defense { get => _data.defense; set => _data.defense = value; }

    // 生命值变化事件
    public UltEvent OnHpChanged { get; set; }

    // 防御变化事件
    public UltEvent OnDefenseChanged { get; set; }

    // 存档事件
    public UltEvent onSave { get; set; }

    // 读档事件
    public UltEvent onLoad { get; set; }

    // 掉落列表属性
    public List_Loot Loots { get => _data.loot; set => _data.loot = value; }

    #endregion

    public UltEvent OnDeath { get; set; }


    public void Start()
    {
        OnHpChanged = new UltEvent();
        OnDefenseChanged = new UltEvent();
    }

    public void FixedUpdate()
    {
       
    }


    public float GetDamage(Damage damage)
    {
        // 检查输入的 Damage 对象是否为空
        if (damage == null)
        {
            Debug.LogWarning("Damage 对象为空");
            return 0;
        }

        float finalDamage = 0;

        // 检测输入的伤害类型列表中有多少个击中了弱点
        int hitCount = damage.Check_DamageType(Hp.Weaknesses);

        if (hitCount > 0)
        {
            finalDamage = damage.Return_EndDamage();
            Debug.Log("树的弱点被击中，受到" + finalDamage + "点全额伤害");
        }
        else
        {
            finalDamage = damage.Return_EndDamage(Defense);
            Debug.Log("树成功防御攻击，受到" + finalDamage + "点伤害");
        }

        // 扣除 HP 值
        Hp.Value -= finalDamage;

        // 触发 HP 变化事件
        OnHpChanged.Invoke();

        if (Hp.Value <= 0)
        {
            Death();
        }

        // 返回实际伤害值
        return finalDamage;
    }


    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void Death()
    {
        Debug.Log("树被摧毁");
        ItemMaker maker = new ItemMaker();
        maker.DropItemByLoot(Loots.GetLoot("Loots_Production"), 2f, transform);
        Destroy(gameObject);
    }

    #region ISave_Load接口实现
    [Button("保存")]
    public void Save()
    {
        onSave?.Invoke();
    }
    [Button("加载")]
    public void Load()
    {
        onLoad?.Invoke();
    }
    #endregion
}


[MemoryPackable]
[System.Serializable]
public partial class DropItem
{
    [Header("掉落物品")]
    public string itemName;
    public int amount;
    [MemoryPackConstructor]
    public DropItem(string itemName,int amount)
    {
        this.itemName = itemName;
        this.amount = amount;
    }
}
