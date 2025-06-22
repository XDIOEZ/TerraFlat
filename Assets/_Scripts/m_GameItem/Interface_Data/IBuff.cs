using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IBuff
{
    public Dictionary<string, BuffRunTime> BuffRunTimeData_Dic { get; set; }
}
