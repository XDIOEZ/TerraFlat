using UnityEngine;

[CreateAssetMenu(fileName = "�½�Buff��Ϊ_�޸��ƶ��ٶ�", menuName = "Buff/MoverSpeedChange")]
public class Buff_MoverSpeedChange : BuffBehavior
{
    public float SpeedChangeValue;
    public override void Apply(BuffRunTime data)
    {
        ISpeed moverSpeed = data.buff_Receiver as ISpeed;

        moverSpeed.Speed *= SpeedChangeValue;
    }
}