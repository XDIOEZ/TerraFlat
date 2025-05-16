using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IFuel
{
    //提供最大燃料量
    float MaxBurnTime { get; set; }

    //能提供最高温度
    float MaxTemptrue { get; set; }


    public void SyncFuelData(int i)
    {
        var manager = m_ExcelManager.Instance;
        MaxBurnTime = manager.GetConvertedValue<float>(
        "MaxBurnTime", i, 0.0f);
        MaxTemptrue = manager.GetConvertedValue<float>(
        "MaxTemptrue", i, 0.0f);
    }
}
 