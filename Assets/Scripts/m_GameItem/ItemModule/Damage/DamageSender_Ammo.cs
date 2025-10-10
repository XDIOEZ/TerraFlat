/*using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DamageSender_Ammo :  MonoBehaviour,IDamageSender
{
    public IColdWeapon _Weapon;
    public bool isDamageModeEnabled = false; // 默认开启伤害模式
    public Collider2D _collider;
    public float damageInterval = 1f; // 造成伤害的间隔时间，单位为秒
    public float lastDamageTime = 0f; // 上次造成伤害的时间
    public int damageCount = 0; // 造成的伤害次数

    // 提供公共属性来访问和修改 isDamageModeEnabled
    public void Awake()
    {
        InitializeComponents();
    }


    [ShowNonSerializedField]
    public virtual bool IsDamageModeEnabled
    {
        get
        {
            return isDamageModeEnabled;
        }
        set
        {
            Collider.enabled = value;
            isDamageModeEnabled = value;
        }
    }

    public virtual Collider2D Collider
    {
        get
        {
            return _collider;
        }
        set
        {
            if (_collider == null)
            {
                _collider = GetComponent<Collider2D>();
            }
            _collider = value;
        }
    }
    public virtual Damage DamageValue
    {
        get { return _Weapon.WeaponDamage; }
        set { _Weapon.WeaponDamage = value; }
    }

    public virtual void InitializeComponents()
    {
        _Weapon = GetComponentInParent<IColdWeapon>();
        Collider = GetComponent<Collider2D>(); // 确保碰撞器初始化 
    }
    public  void OnTriggerEnter2D(Collider2D collider)
    {
        // Debug.Log("OnTriggerEnter2D");
        // 获取目标的伤害检查点组件
        Damager_CheckerPoint damagePoint = collider.GetComponentInChildren<Damager_CheckerPoint>();

        if (damagePoint == null)
        {

            Debug.Log("没有找到有效的检查点"+collider.name);
            return; // 如果没有找到有效的检查点，则无法施加伤害
        }
        // 首先检查伤害模式是否启用以及是否满足施加伤害的条件
        if (!IsDamageModeEnabled || Time.time - lastDamageTime < damageInterval|| damageCount >=1)
        {
            Debug.Log("伤害模式未启用或伤害间隔时间未到，无法施加伤害");
            return;
        }

        // 设置目标的伤害值
        damagePoint.SetDamageValue_Point(DamageValue);

        Destroy(gameObject.transform.parent.gameObject);// 销毁子弹

        damageCount += 1; // 记录造成的伤害次数

        lastDamageTime = Time.time;  // 记录当前时间作为最后一次伤害的时间

        return; // 成功施加伤害
    }

    public void StartTrySendDamage()
    {
        throw new System.NotImplementedException();
    }

    public void StayTrySendDamage()
    {
        throw new System.NotImplementedException();
    }

    public void EndTrySendDamage()
    {
        throw new System.NotImplementedException();
    }
}
*/