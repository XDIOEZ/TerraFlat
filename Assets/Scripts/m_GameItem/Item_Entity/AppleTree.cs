using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UltEvents;
using UnityEngine;

public class AppleTree : Item, IHealth, ISave_Load, ILoot, IPlant
{
    #region 基础数据

    // 基础数据字段
    public Data_Creature _data;

    // 物品数据访问
    public override ItemData itemData { get => _data; set => _data = (Data_Creature)value; }

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
    public Loot_List Loots { get => _data.loot; set => _data.loot = value; }

    #endregion

    public UltEvent OnDeath { get; set; }


    public new void Start()
    {
        base.Start();
        OnHpChanged = new UltEvent();
        OnDefenseChanged = new UltEvent();
        //GetComponentInChildren<TileEffectReceiver>().OnTileEnterEvent += ChangeGrow;
       // GetComponentInChildren<TileEffectReceiver>().OnTileEnterEvent += OnTileEnter;
      //  Mods["生长模块"].OnAction += Grow;
    }

    public void FixedUpdate()
    {
       
    }
    void OnTileEnter(TileData data)
    {
        Debug.Log("OnTileEnter");

        if (data == null)
        {
            Debug.LogError("TileData 为空，无法处理！");
            Mods["生长模块"]._Data.isRunning = false;
            return;
        }

        if (data is not TileData_Grass tileData)
        {
            Debug.LogWarning($"TileData 类型错误，当前类型是 {data.GetType().Name}，期望类型是 TileData_Grass");
            Mods["生长模块"]._Data.isRunning = false;
            return;
        }

        if (tileData.FertileValue.Value > 0)
        {
            Debug.Log("当前格子适合生长，启动生长模块");
            Mods["生长模块"]._Data.isRunning = true;
        }
        else
        {
            Debug.LogWarning($"当前格子的肥沃度值为 {tileData.FertileValue.Value}，不适合生长");
            Mods["生长模块"]._Data.isRunning = false;
        }

    }

    public void Grow(float NodeIndex)
    {
        if(NodeIndex == 1)
        {
            transform.localScale = Vector3.one * 0.25f;
        }
        else if(NodeIndex == 2)
        {
            transform.localScale = Vector3.one * 0.5f;
        }
        else if(NodeIndex == 3)
        {
            transform.localScale = Vector3.one * 1f;
            Mods["生产模块"]._Data.isRunning = true;
        }
        else if(NodeIndex == 4)
        {
            transform.localScale = Vector3.one * 2f;
        }
    }

    void ChangeGrow(TileData tileData)
    {
       // if(tileData.)
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
}

internal interface IPlant
{
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
