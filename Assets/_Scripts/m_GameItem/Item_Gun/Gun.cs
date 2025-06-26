using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Gun : Weapon
{
    #region 字段 | Fields 
    public PrefabLauncher launcher;
    public GunData gunData; // 使用GunData封装参数 
    #endregion

    #region 生命周期 | Life Cycle 
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

    #region 攻击逻辑 | Attack Logic 
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
        // 实现停止攻击的逻辑（如有需要）
    }
    #endregion

    #region 数据接口 | Data Management 
/*    public override Item_Data GetData()
    {
        Debug.Log("获取gunData中....");
        return gunData;
    }*/
    #endregion 
}
