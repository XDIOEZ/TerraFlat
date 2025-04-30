using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UltEvents;
using UnityEngine;

public class AppleTree : Item,IHealth,ISave_Load,ILoot,IItemValues
{
    #region ��������

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

    #region ��������

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
        Load();
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
        Hp.value -= finalDamage;

        // ���� HP �仯�¼�
        OnHpChanged.Invoke();

        if (Hp.value <= 0)
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

    public void Production(float value)
    {
        if(value > 100)
        {
            GetComponentInChildren<ItemMaker>().DropItemByLoot(Loots.GetLoot("Loots_Production"), 2f);
            GetComponentInChildren<ItemValuesManager>().Value_DICT["��������"].CurrentValue = -1;
        }
    }

    public void Death()
    {
          Debug.Log("�����ݻ�");
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

    #region ISave_Load�ӿ�ʵ��
    [Button("����")]
    public void Save()
    {
        onSave?.Invoke();
        _data.loot = GetComponentInChildren<ItemMaker>().loots;
    }
    [Button("����")]
    public void Load()
    {
        onLoad?.Invoke();
        GetComponentInChildren<ItemMaker>().loots = _data.loot;
        GetComponentInChildren<ItemValuesManager>().SetItemValues_AndStart(this);
        GetComponentInChildren<ItemValuesManager>().Value_DICT["��������"].OnValueChanged += Production;
        if (GetComponentInChildren<ItemValuesManager>().Value_DICT["��������"].CurrentValue == 0)
        {
            GetComponentInChildren<ItemValuesManager>().Value_DICT["��������"].CurrentValue = Random.Range(0,100);
        }
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
