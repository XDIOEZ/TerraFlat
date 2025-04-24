using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IFuel
{
    //提供最大燃料量
    float MaxBurnTime { get;}

    //能提供最高温度
    float MaxTemptrue { get; }
}
 