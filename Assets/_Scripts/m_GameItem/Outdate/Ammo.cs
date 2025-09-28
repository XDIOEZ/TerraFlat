using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ammo : Item
{
    public IDamageSender damageSender;
    public Rigidbody2D rb2d;
    public Data_Ammo ammoData;
    [Header("���߼���ų�")]
    public LayerMask layerMask;

    public new void  Start()
    {
        base.Start();
        DamageSender = GetComponentInChildren<IDamageSender>();
                rb2d = GetComponent<Rigidbody2D>();
        previousPosition = transform.position;
    }
    public Vector2 currentPosition;//��ǰ֡λ��
    public Vector2 previousPosition;//��һ֡λ��

    public void FixedUpdate()
    {
        //�ӵ���ǰ֡λ��
         currentPosition = transform.position;
        //�ӵ���ǰ֡����
        Vector2 direction = currentPosition - previousPosition;
        //�ӵ�һ֡�ƶ�����
        float distance = direction.magnitude;

        if (distance > 0)
        {
            Debug.DrawRay(previousPosition, direction*2, Color.red);

            //����Ammoͼ�������赲����
            RaycastHit2D _hit = Physics2D.Raycast(previousPosition, direction , distance*2, layerMask);

          

          
            

            if (_hit.collider != null)
            {
                // �ڴ˴�����������ײ�����߼�
               // damageSender.OnTriggerEnter2D(_hit.collider);
                Debug.Log("Ammo hit " + _hit.collider.name);
            }
            //�ٶ�С��0.5ʱ����
            if (rb2d.velocity.magnitude < 0.5f)
            {
                Destroy(gameObject);
            }
            previousPosition = currentPosition;
        }

    }

    public IDamageSender DamageSender
    {
        get
        {
            if (damageSender == null)
            {
                damageSender = GetComponentInChildren<IDamageSender>();
            }
            return damageSender;
        }
        set
        {
            damageSender = value;
        }
    }

    public override ItemData itemData
    {
        get => ammoData;

        set => ammoData = (Data_Ammo)value;
    }
    public Damage WeaponDamage
    {
        get
        {
            return ammoData.damage;
        }

        set
        {
            ammoData.damage = value;
        }
    }

    public float MinDamageInterval 
    {
        get
        {
            return ammoData.MinDamageInterval;
        }

        set
        {
            ammoData.MinDamageInterval = value;
        }
    }

    public Data_ColdWeapon Data { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float MaxAttackDistance { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float AttackSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float ReturnSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float SpinSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float EnergyCostSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    public override void Act()
    {
    //    DamageSender.IsDamageModeEnabled = true;
    }
}
/*
    public override Item_Data GetData()
    {
        Debug.LogWarning("Ammo.GetData() is not implemented");
        throw new System.NotImplementedException();
    }

    public override void SetData(Item_Data data)
    {
        Debug.LogWarning("Ammo.GetData() is not implemented");
        throw new System.NotImplementedException();
    }*/
