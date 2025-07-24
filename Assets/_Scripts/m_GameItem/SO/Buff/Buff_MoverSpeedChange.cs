using UnityEngine;

[CreateAssetMenu(fileName = "�½�Buff��Ϊ_�޸��ƶ��ٶ�", menuName = "Buff/MoverSpeedChange")]
public class Buff_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;

    public override void Apply(BuffRunTime data)
    {
        if (!data.buff_Receiver.Mods.ContainsKey(ModText.Mover))
        {
            // Buff������û���ٶȽӿڣ�ȡ��apply
            return;
        }
        var speeder = data.buff_Receiver.Mods[ModText.Mover].GetComponent<Mover>();
        speeder.Speed.MultiplicativeModifier *= SpeedChangeValue;
    }
}