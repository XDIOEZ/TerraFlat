using UnityEngine;

[System.Serializable]
[CreateAssetMenu(fileName = "New Buff_Stamina_Change", menuName = "Buff/Buff_Stamina_Change")]
public class BuffAction_StaminaChange : BuffAction
{
    [Header("�����ָ��ٶ�")]
    [Tooltip("�ı䱶��")]
    public float SpeedRate;
    public override void Apply(BuffRunTime data)
    {
        data.buff_Receiver.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina, out var mod);
        if (mod == null)
        {
            // Buff������û���ٶȽӿڣ�ȡ��apply
            return;
        }
        mod.Data.CurrentStamina += SpeedRate;
    }
}