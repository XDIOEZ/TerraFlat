using UnityEngine;

[CreateAssetMenu(fileName = "�½�Buff��Ϊ_�޸��ƶ��ٶ�", menuName = "Buff/MoverSpeedChange")]
public class Buff_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;

    public override void Apply(BuffRunTime data)
    {
        ISpeed Speeder = (ISpeed)data.buff_Receiver;

        if (Speeder == null)
        {
            // Buff������û���ٶȽӿڣ�ȡ��apply
            return;
        }
        Speeder.Speed.MultiplicativeModifier *= SpeedChangeValue;
    }
}