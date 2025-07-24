using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using UltEvents; // 别忘了加上这个using！

/*<<<<<<< Updated upstream
public class IronSword : Item, IColdWeapon,IDamager
=======*/
public class IronSword : Item, IColdWeapon,IAttackState
//>>>>>>> Stashed changes
{
    #region 属性
    #region 物体数据
    public Data_ColdWeapon _data;
    public override ItemData Item_Data { get => _data; set => _data = (Data_ColdWeapon)value; }
    #endregion

    #region 武器数据

    public Damage WeaponDamage { get => _data._damage; set => _data._damage = value; }
    public float MinDamageInterval { get => _data._minDamageInterval; set => _data._minDamageInterval = value; }
    public float MaxAttackDistance { get => _data._maxAttackDistance; set => _data._maxAttackDistance = value; }
    public float AttackSpeed { get => _data._attackSpeed; set => _data._attackSpeed = value; }
    public float ReturnSpeed { get => _data._returnSpeed; set => _data._returnSpeed = value; }
    public float SpinSpeed { get => _data._spinSpeed; set => _data._spinSpeed = value; }
    public float EnergyCostSpeed { get => _data._energyConsumptionSpeed; set => _data._energyConsumptionSpeed = value; }
    public float LastDamageTime { get => _data._lastDamageTime; set => _data._lastDamageTime = value; }
    //武器伤害输出窗口
    public float MaxDamageCount { get=>_data._maxAttackCount; set => _data._maxAttackCount = value; }

    #endregion

    #endregion

    #region 攻击行为监听
    public UltEvent OnAttackStart { get; set; } = new UltEvent();
    public UltEvent OnAttackUpdate { get; set; } = new UltEvent();
    public UltEvent OnAttackEnd { get; set; } = new UltEvent();
    #endregion

    #region 方法

    public override void Act()
    {
        print("IronSword Act");
    }
    #endregion
}
