using UltEvents;
using UnityEngine;

public interface IStamina
{
    public float Stamina { get; set; }

    public float MaxStamina { get; set; }

    [Tooltip("每秒恢复的精力值")]
    public float StaminaRecoverySpeed { get; set; }

    public UltEvent OnStaminaChanged { get; }
}