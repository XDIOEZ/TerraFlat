using UnityEngine;

[CreateAssetMenu(fileName = "新建Buff行为_修改移动速度", menuName = "Buff/MoverSpeedChange")]
public class Buff_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;
    public override void Apply(BuffRunTime data)
    {
        ISpeed moverSpeed = data.buff_Receiver as ISpeed;

        moverSpeed.Speed *= SpeedChangeValue;
    }
}