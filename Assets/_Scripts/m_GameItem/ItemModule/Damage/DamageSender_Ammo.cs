/*using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DamageSender_Ammo :  MonoBehaviour,IDamageSender
{
    public IColdWeapon _Weapon;
    public bool isDamageModeEnabled = false; // Ĭ�Ͽ����˺�ģʽ
    public Collider2D _collider;
    public float damageInterval = 1f; // ����˺��ļ��ʱ�䣬��λΪ��
    public float lastDamageTime = 0f; // �ϴ�����˺���ʱ��
    public int damageCount = 0; // ��ɵ��˺�����

    // �ṩ�������������ʺ��޸� isDamageModeEnabled
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
        Collider = GetComponent<Collider2D>(); // ȷ����ײ����ʼ�� 
    }
    public  void OnTriggerEnter2D(Collider2D collider)
    {
        // Debug.Log("OnTriggerEnter2D");
        // ��ȡĿ����˺��������
        Damager_CheckerPoint damagePoint = collider.GetComponentInChildren<Damager_CheckerPoint>();

        if (damagePoint == null)
        {

            Debug.Log("û���ҵ���Ч�ļ���"+collider.name);
            return; // ���û���ҵ���Ч�ļ��㣬���޷�ʩ���˺�
        }
        // ���ȼ���˺�ģʽ�Ƿ������Լ��Ƿ�����ʩ���˺�������
        if (!IsDamageModeEnabled || Time.time - lastDamageTime < damageInterval|| damageCount >=1)
        {
            Debug.Log("�˺�ģʽδ���û��˺����ʱ��δ�����޷�ʩ���˺�");
            return;
        }

        // ����Ŀ����˺�ֵ
        damagePoint.SetDamageValue_Point(DamageValue);

        Destroy(gameObject.transform.parent.gameObject);// �����ӵ�

        damageCount += 1; // ��¼��ɵ��˺�����

        lastDamageTime = Time.time;  // ��¼��ǰʱ����Ϊ���һ���˺���ʱ��

        return; // �ɹ�ʩ���˺�
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