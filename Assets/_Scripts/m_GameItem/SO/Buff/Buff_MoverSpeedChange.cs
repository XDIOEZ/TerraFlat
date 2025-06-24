using UnityEngine;

[CreateAssetMenu(fileName = "�½�Buff��Ϊ_�޸��ƶ��ٶ�", menuName = "Buff/MoverSpeedChange")]
public class Buff_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;

    public override void Apply(BuffRunTime data)
    {
        if (data.buff_Receiver is not ISpeed speeder)
        {
            // Buff������û���ٶȽӿڣ�ȡ��apply
            return;
        }
        speeder.Speed.MultiplicativeModifier *= SpeedChangeValue;
    }
}