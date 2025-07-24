using UnityEngine;

[System.Serializable]
[CreateAssetMenu(fileName = "New Buff_Stamina_Change", menuName = "Buff/Buff_Stamina_Change")]
public class Buff_Stamina_Change : BuffAction
{
    [Tooltip("¸Ä±ä±¶ÂÊ")]
    public float staminaChangeRate;
    public override void Apply(BuffRunTime data)
    {
       var stamina    = data.buff_Receiver.Mods[ModText.Mover] as Mover;
        stamina.staminaConsumeSpeed.MultiplicativeModifier *= staminaChangeRate;
    }
}