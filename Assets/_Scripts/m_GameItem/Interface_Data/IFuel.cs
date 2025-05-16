using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IFuel
{
    //�ṩ���ȼ����
    float MaxBurnTime { get; set; }

    //���ṩ����¶�
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
 