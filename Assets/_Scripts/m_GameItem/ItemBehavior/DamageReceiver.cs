/*using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class DamageReceiver : MonoBehaviour
{
    public IReceiveDamage entity_;

    public float hp;

    public Dictionary<string, float> DefenseDict = new Dictionary<string, float>();
    public List<Damager_CheckerPoint> Damager_CheckerPoints = new List<Damager_CheckerPoint>();

    public UltEvent<float> OnChangeHP;
    public UltEvent<float> OnChangeDefense;
    public UltEvent OnDeath;

    //挂接实体血量
    public Hp _Hp
    {
        get
        {
            return entity_.Hp;
        }

        set
        {
            entity_.Hp = value;
        }
    }
    //挂接实体防御
    public Defense _Defense
    {
        get
        {
            return entity_.DefenseValue;
        }

        set
        {
            entity_.DefenseValue = value;
        }
    }


    private void Start()
    {
        // 获取子对象填充列表 Damager_CheckerPoints
        Damager_CheckerPoints.Clear();
        Damager_CheckerPoints.AddRange(GetComponentsInChildren<Damager_CheckerPoint>());
        entity_ = GetComponentInParent<IReceiveDamage>();
    }

    public void SetDamageValue(Damage damage)
    {

        Debug.Log(" 受到 damage: " + damage + " 剩余 : " + _Hp);

        OnChangeHP.Invoke(_Hp.value);

        if (_Hp.value < 0)
        {
            Debug.Log(name + "血量耗尽");
            Die();
        }
    }

    public void SetDefense(Defense defense)
    {

    }

    public void Die()
    {
        OnDeath.Invoke();
        
    }

}
*/