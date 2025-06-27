using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Gun : Weapon
{
    #region �ֶ� | Fields 
    public PrefabLauncher launcher;
    public GunData gunData; // ʹ��GunData��װ���� 
    #endregion

    #region �������� | Life Cycle 
    public new void Start()
    {
        launcher ??= GetComponent<PrefabLauncher>();

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

    #region �����߼� | Attack Logic 
    public override void StartAttack()
    {
        if (gunData.WeaponFireMode == GunData.FireMode.Single)
        {
            launcher.LaunchPrefab(transform.right, gunData.speed, transform.rotation);
        }
    }

    public override void StayAttack()
    {
        if (gunData.WeaponFireMode == GunData.FireMode.Automatic)
        {
            gunData.timeSinceLastFire += Time.deltaTime;
            if (gunData.timeSinceLastFire >= 1f / gunData.fireRate)
            {
                launcher.LaunchPrefab(transform.right, gunData.speed, transform.rotation);
                gunData.timeSinceLastFire = 0f;
            }
        }
    }

    public override void StopAttack()
    {
        // ʵ��ֹͣ�������߼���������Ҫ��
    }
    #endregion

    #region ���ݽӿ� | Data Management 
/*    public override Item_Data GetData()
    {
        Debug.Log("��ȡgunData��....");
        return gunData;
    }*/
    #endregion 
}
