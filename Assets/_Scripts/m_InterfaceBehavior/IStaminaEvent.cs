using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public interface IStaminaEvent
{
    UltEvent<float> StaminaChange { get; set; }

}
