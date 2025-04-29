using System.Collections;
using UltEvents;
using UnityEngine;

    public class Wolf : Item, IHunger, ISpeed, ISight, IHealth, IStamina,ISave_Load
{
    public AnimalData Data;
    public override ItemData Item_Data { get { return Data; } set { Data = value as AnimalData; } }
    #region 饥饿

    public Hunger_Water Foods { get => Data.hunger; set => Data.hunger = value; }
    public float EatingSpeed { get => Data.attackSpeed; set => throw new System.NotImplementedException(); }
    public UltEvent OnNutrientChanged { get; set; } = new UltEvent();

    #endregion
    #region 速度
    // 完善ISpeed接口实现，直接映射AnimalData中的速度属性
    public float Speed
    {
        get => Data.speed;
        set => Data.speed = value;
    }

    public float DefaultSpeed
    {
        get => Data.defaultSpeed;
        set => Data.defaultSpeed = value;
    }

    public float RunSpeed
    {
        get => Data.runSpeed;
        set => Data.runSpeed = value;
    }
    #endregion

    #region 感知

    public float sightRange { get => Data.sightRange; set => Data.sightRange = value; }
    #endregion


    #region 生命
    public Hp Hp
    {
        get => Data.hp;
        set
        {
            if (Data.hp != value)
            {
                Data.hp = value;
                OnHpChanged?.Invoke();
            }
        }
    }

    public Defense Defense
    {
        get => Data.defense;
        set
        {
            if (Data.defense != value)
            {
                Data.defense = value;
                OnDefenseChanged?.Invoke();
            }
        }
    }

    public UltEvent OnHpChanged { get; set; } = new UltEvent();

    public UltEvent OnDefenseChanged { get; set; } = new UltEvent();


    #endregion

    #region 精力

    public float Stamina
    {
        get => Data.stamina;
        set => Data.stamina = Mathf.Clamp(value, 0, Data.staminaMax);
    }

    public float MaxStamina
    {
        get => Data.staminaMax;
        set => Data.staminaMax = value;
    }

    public float StaminaRecoverySpeed
    {
        get => Data.staminaRecoverySpeed;
        set => Data.staminaRecoverySpeed = value;
    }
    public UltEvent OnStaminaChanged { get; set; } = new UltEvent();
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    #endregion

    public void Start()
    {
        Load();
      //  Debug.Log("Wolf Start");
    }

    public void OnEnable()
    {
      
    }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void FixedUpdate()
    {
        Hungry_Update();

    }

    public void Eat(float Be_Eat)
    {
        Foods.Food += Be_Eat;
    }

    public void Hungry_Update()
    {
        //每秒减少1点食物能量*生产速度
        Data.hunger.Food -= Time.fixedDeltaTime * Data.productionSpeed;
    }

    public void Death()
    {
        Destroy(gameObject);
    }

    #region 保存与加载
    public void Save()
    {
        GetComponent<ITriggerAttack>().DestroyWeapon();
    }

    public void Load()
    {
        ItemData _WeaponData;
        if (Data._inventoryData.ContainsKey("武器"))
        {
             _WeaponData = Data._inventoryData["武器"].itemSlots[0]._ItemData;
        }
        else
        {
            _WeaponData = GameRes.Instance.GetPrefab("WolfDefaultWeapon").GetComponent<Item>().Item_Data;
        }

            GetComponentInChildren<ITriggerAttack>().CreateWeapon(_WeaponData);
    }
    #endregion
}
