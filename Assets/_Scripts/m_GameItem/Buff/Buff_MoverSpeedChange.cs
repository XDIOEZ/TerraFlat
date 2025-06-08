using UnityEngine;

[CreateAssetMenu(fileName = "�½�Buff��Ϊ_�޸��ƶ��ٶ�", menuName = "Buff/MoverSpeedChange")]
public class Buff_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;
    public override void Apply(BuffRunTime data)
    {
        ISpeed moverSpeed = data.buff_Receiver as ISpeed;
        if (moverSpeed == null)
        {
            // Buff������û���ٶȽӿڣ�ȡ��apply
            return;
        }

        moverSpeed.Speed *= SpeedChangeValue;
    }
}