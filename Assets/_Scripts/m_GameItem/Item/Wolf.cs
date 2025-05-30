using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

    public class Wolf : Item, IHunger, ISpeed, ISight, IHealth, IStamina,ISave_Load,IItemValues,ITeam
{
    public Data_Creature Data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Creature; }
    #region 饥饿

    public Nutrition Foods { get => Data.NutritionData; set => Data.NutritionData = value; }
    public float EatingSpeed { get => Data.EatingSpeed; set => throw new System.NotImplementedException(); }
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
        get => Data.speed_Max;
        set => Data.speed_Max = value;
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
        set => Data.stamina = Mathf.Clamp(value, 0, Data.stamina_Max);
    }

    public float MaxStamina
    {
        get => Data.stamina_Max;
        set => Data.stamina_Max = value;
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

    #region 动态数据

    public ItemValues ItemValues { get => Data.ItemDataValue; set => Data.ItemDataValue = value; }

    #endregion

    #region 团队
    public string TeamID { get => Data.TeamID; set => Data.TeamID = value; }
    public Dictionary<string, RelationType> Relations { get => Data.Relations; set => Data.Relations = value; }
    #endregion

    public void Start()
    {
    }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void FixedUpdate()
    {
        //每秒恢复1点精力
        Hungry_Update();
        //更新数值管理器
        ItemValues.FixedUpdate();
    }

    public void Eat(IFood food)
    {
        Foods.Food += EatingSpeed;
        food.BeEat(EatingSpeed);
    }

    public void Hungry_Update()
    {
        //每秒减少1点食物能量*生产速度
        Data.NutritionData.Food -= Time.fixedDeltaTime * Data.productionSpeed;
    }

    public void Death()
    {
        OnStopWork_Event.Invoke();
        Animator animator = GetComponentInChildren<Animator>();
        animator.SetTrigger("Death");


        StartCoroutine(WaitAndDestroy(animator));
    }

    private IEnumerator WaitAndDestroy(Animator animator)
    {
        // 获取当前动画状态
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

        // 等待当前动画状态结束（仅适用于已进入播放状态）
        yield return new WaitForSeconds(stateInfo.length-0.1f);

        Destroy(gameObject);
    }


    #region 保存与加载
    public void Save()
    {
        GetComponentInChildren<ITriggerAttack>().DestroyWeapon();
    }

    public void Load()
    {
        Item _Weapon;
        if (Data._inventoryData.ContainsKey("武器"))
        {
            _Weapon = RunTimeItemManager.Instance.InstantiateItem("WolfDefaultWeapon");
            _Weapon.Item_Data = Data._inventoryData["武器"].itemSlots[0]._ItemData;
        }
        else
        {
            _Weapon = RunTimeItemManager.Instance.InstantiateItem("WolfDefaultWeapon");
        }
         
        GetComponentInChildren<ITriggerAttack>().GetItemWeapon(_Weapon);
        //启动数值管理器
        ItemValues.Start_Work();

        Hp.OnValueChanged += (float f) =>
        {
            ItemValues.GetValue("血量").CurrentValue = f;
        };

        ItemValues.GetValue("血量").OnCurrentValueChanged
            += (float f) => 
            {
             Hp.Value = f;
            };

        if (TeamID == "")
        {
            //随机分配一个int范围内的ID
            TeamID = "WolfTeam";
            Relations.Add(TeamID, RelationType.Ally);
        }

    }
    #endregion
}
