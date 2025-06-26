using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UltEvents;
using UnityEngine;

public class AppleTree : Item,IHealth,ISave_Load,ILoot
{
    #region ��������

    // ���������ֶ�
    public Data_Creature _data;

    // ��Ʒ���ݷ���
    public override ItemData Item_Data { get => _data; set => _data = (Data_Creature)value; }

    #endregion

    #region ��������

    // ����ֵ����
    public Hp Hp { get => _data.hp; set => _data.hp = value; }

    // ��������
    public Defense Defense { get => _data.defense; set => _data.defense = value; }

    // ����ֵ�仯�¼�
    public UltEvent OnHpChanged { get; set; }

    // �����仯�¼�
    public UltEvent OnDefenseChanged { get; set; }

    // �浵�¼�
    public UltEvent onSave { get; set; }

    // �����¼�
    public UltEvent onLoad { get; set; }

    // �����б�����
    public Loot_List Loots { get => _data.loot; set => _data.loot = value; }

    #endregion

    public UltEvent OnDeath { get; set; }


    public void Start()
    {
        OnHpChanged = new UltEvent();
        OnDefenseChanged = new UltEvent();
        Mods["����ģ��"].OnAction += Grow;

        //GetComponentInChildren<TileEffectReceiver>().OnTileEnterEvent += ChangeGrow;
        GetComponentInChildren<TileEffectReceiver>().OnTileEnterEvent += OnTileEnter;
    }

    public void FixedUpdate()
    {
       
    }
    void OnTileEnter(TileData data)
    {
        Debug.Log("OnTileEnter");

        if (data == null)
        {
            Debug.LogError("TileData Ϊ�գ��޷�����");
            Mods["����ģ��"].Data.isRunning = false;
            return;
        }

        if (data is not TileData_Grass tileData)
        {
            Debug.LogWarning($"TileData ���ʹ��󣬵�ǰ������ {data.GetType().Name}������������ TileData_Grass");
            Mods["����ģ��"].Data.isRunning = false;
            return;
        }

        if (tileData.FertileValue.Value > 0)
        {
            Debug.Log("��ǰ�����ʺ���������������ģ��");
            Mods["����ģ��"].Data.isRunning = true;
        }
        else
        {
            Debug.LogWarning($"��ǰ���ӵķ��ֶ�ֵΪ {tileData.FertileValue.Value}�����ʺ�����");
            Mods["����ģ��"].Data.isRunning = false;
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
            Mods["����ģ��"].Data.isRunning = true;
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
        // �������� Damage �����Ƿ�Ϊ��
        if (damage == null)
        {
            Debug.LogWarning("Damage ����Ϊ��");
            return 0;
        }

        float finalDamage = 0;

        // ���������˺������б����ж��ٸ�����������
        int hitCount = damage.Check_DamageType(Hp.Weaknesses);

        if (hitCount > 0)
        {
            finalDamage = damage.Return_EndDamage();
            Debug.Log("�������㱻���У��ܵ�" + finalDamage + "��ȫ���˺�");
        }
        else
        {
            finalDamage = damage.Return_EndDamage(Defense);
            Debug.Log("���ɹ������������ܵ�" + finalDamage + "���˺�");
        }

        // �۳� HP ֵ
        Hp.Value -= finalDamage;

        // ���� HP �仯�¼�
        OnHpChanged.Invoke();

        if (Hp.Value <= 0)
        {
            Death();
        }

        // ����ʵ���˺�ֵ
        return finalDamage;
    }


    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void Death()
    {
        Debug.Log("�����ݻ�");
        ItemMaker maker = new ItemMaker();
        maker.DropItemByLoot(Loots.GetLoot("Loots_Production"), 2f, transform);
        Destroy(gameObject);
    }

    #region ISave_Load�ӿ�ʵ��
    [Button("����")]
    public void Save()
    {
        onSave?.Invoke();
    }
    [Button("����")]
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
    [Header("������Ʒ")]
    public string itemName;
    public int amount;
    [MemoryPackConstructor]
    public DropItem(string itemName,int amount)
    {
        this.itemName = itemName;
        this.amount = amount;
    }
}
