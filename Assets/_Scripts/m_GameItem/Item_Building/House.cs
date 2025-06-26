using UnityEngine;
using UltEvents;
using System.Collections.Generic;

public class House : Item, ISave_Load, IHealth, IBuilding,IRoom
{
    #region �ֶ������л��ֶ�
    [Header("��������")]
    [SerializeField]
    private Data_Building data = new Data_Building();

    [Header("���")]
    public List<BoxCollider2D> colliders;

    [Header("��������ģ��")]
    [SerializeField]
    private WorldSaveSO buildingSO;
    public WorldSaveSO BuildingSO { get => buildingSO; set => buildingSO = value; }
    #endregion

    #region �ӿ�ʵ��
    // ISave_Load
    public UltEvent onSave { get; set; } = new UltEvent();
    public UltEvent onLoad { get; set; } = new UltEvent();

    // IHealth
    public Hp Hp
    {
        get => data.hp;
        set
        {
            data.hp = value;
            OnHpChanged?.Invoke();
        }
    }

    public Defense Defense
    {
        get => data.defense;
        set
        {
            data.defense = value;
            OnDefenseChanged?.Invoke();
        }
    }

    public UltEvent OnDefenseChanged { get; set; } = new UltEvent();
    public UltEvent OnHpChanged { get; set; } = new UltEvent();
    public UltEvent OnDeath { get; set; }

    // IBuilding
    public bool IsInstalled
    {
        get => data.isBuilding;
        set => data.isBuilding = value;
    }

    public bool BePlayerTaken { get; set; } = false;

    // Item
    public override ItemData Item_Data
    {
        get => data;
        set => data = value as Data_Building ?? new Data_Building();
    }

    public string RoomName
    {
        get
        {
            return data.sceneName;
        }
        set
        {
            data.sceneName = value;
        }
    }


    // ��������ȡ�������ݵķ���
    public Data_Building GetBuildingData() => data;
    #endregion

    #region Unity��������
    private void Start()
    {
        BePlayerTaken = BelongItem != null;
    }
    #endregion

    #region ���Ĺ���
    public void Save() => onSave?.Invoke();
    public void Load()
    {
        OnHpChanged?.Invoke();
        OnDefenseChanged?.Invoke();
        onLoad?.Invoke();
    }

    public void Death()
    {
        OnDeath.Invoke();
       // Destroy(gameObject);
    }
    #endregion
}

public interface IRoom
{
    WorldSaveSO BuildingSO { get; }
    string RoomName { get; set; }
}