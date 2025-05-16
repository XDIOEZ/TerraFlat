using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UltEvents;
using UnityEngine;

public class AppleTree : Item,IHealth,ISave_Load,ILoot,IItemValues
{
    #region 基础数据

    public Tree_Data _data;
    public override ItemData Item_Data
    {
        get
        {
            return _data;
        }
        set
        {
            _data = (Tree_Data)value;
        }
    }
    #endregion

    #region 健康数据

    public Hp Hp { get => _data.hp; set => _data.hp = value; }

    public Defense Defense { get => _data.defense; set => _data.defense = value; }

    public UltEvent OnHpChanged { get; set; }
    public UltEvent OnDefenseChanged { get; set; }

    public UltEvent onSave { get; set; }

    public UltEvent onLoad { get; set; }
    public List_Loot Loots
    {
        get
        {
            return _data.loot;
        }
        set
        {
            _data.loot = value;
        }
    }



    #endregion
    #region 物品数值数据
    public ItemValues ItemValues
    {
        get
        {
            return _data.ItemDataValue;
        }
        set
        {
            _data.ItemDataValue = value;
        }
    }
    #endregion

    public void Start()
    {
        OnHpChanged = new UltEvent();
        OnDefenseChanged = new UltEvent();
    }

    public void FixedUpdate()
    {
        ItemValues.FixedUpdate();
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

    public void Production(float value)
    {
        if(value > 100)
        {
            GetComponentInChildren<ItemMaker>().DropItemByLoot(Loots.GetLoot("Loots_Production"), 2f);
           ItemValues.Get_ItemValue("生产进度").CurrentValue = -1;
        }
    }

    public void Death()
    {
          Debug.Log("树被摧毁");
            GetComponentInChildren<ItemMaker>().DropItemByLootName("Loots_Death", 1.5f);
            Destroy(gameObject);
    }

    void HpChanged(float value)
    {
        if(value <= 0)
        {
            Death();
        }
    }

    #region ISave_Load接口实现
    [Button("保存")]
    public void Save()
    {
        onSave?.Invoke();
        _data.loot = GetComponentInChildren<ItemMaker>().loots;
        _data.ItemDataValue.ClearAllEvents();
    }
    [Button("加载")]
    public void Load()
    {
        onLoad?.Invoke();
        GetComponentInChildren<ItemMaker>().loots = _data.loot;
       
        ItemValues.Get_ItemValue("生产进度").OnCurrentValueChanged += Production;
       
        Init();
        ItemValues.Start_Work();
    }

    public void Init()
    {
        ItemValues.Get_ItemValue("生产进度").CurrentValue = Random.Range(0, 100);
        ItemValues.Add_ChangeSpeed("生产进度", "呼吸作用", 1, -1);
       // print("初始化");
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
