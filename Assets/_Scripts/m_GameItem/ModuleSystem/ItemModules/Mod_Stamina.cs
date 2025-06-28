using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Stamina : Module, IUI_Slider
{
    public StaminaData staminaData;
    private List<IStaminaEvent> staminaEvents = new List<IStaminaEvent>();

    public new void Start()
    {
        base.Start();
        // Get all IStaminaEvent components in children
        var events = item.GetComponentsInChildren<IStaminaEvent>();
        staminaEvents.AddRange(events);

        // Subscribe to all events
        foreach (var staminaEvent in staminaEvents)
        {
            staminaEvent.StaminaChange += StaminaChanged;
        }
    }

    private new void OnDestroy()
    {
        base.OnDestroy();
        // Unsubscribe from all events when destroyed to prevent memory leaks
        foreach (var staminaEvent in staminaEvents)
        {
            if (staminaEvent != null)
            {
                staminaEvent.StaminaChange -= StaminaChanged;
            }
        }
    }

    public void StaminaChanged(float value)
    {
        staminaData.Current_Stamina -= value;
    }

    public override ModuleData Data
    {
        get => staminaData;
        set => staminaData = (StaminaData)value;
    }

    // 当前体力值
    public float CurrentValue
    {
        get => staminaData.Current_Stamina;
    }

    // 最大体力值
    public float MaxValue
    {
        get => staminaData.Max_Stamina.Value;
    }
}
[System.Serializable]
[MemoryPackable]
public partial class StaminaData : ModuleData
{
    public float Current_Stamina;
    public GameValue_float Max_Stamina;
}

