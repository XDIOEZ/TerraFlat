using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AK47 : Gun
{
    public override ItemData Item_Data
    {
        get
        {
            return gunData;
        }

        set
        {
            gunData = (GunData)value;
        }
    }
    #region ��������
    public new void Start()
    {
        base.Start();
    }
    new void OnEnable()
    {
        base.OnEnable();
    }

    new void OnDisable()
    {
        base.OnDisable();
    }

    #endregion

    #region ��Ϊ�ӿ�
    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public override void StartAttack()
    {
        base.StartAttack();
    }

    public override void StayAttack()
    {
        base.StayAttack();
    }

    public override void StopAttack()
    {
        base.StopAttack();
    }
    #endregion
}
