using UnityEngine;

[CreateAssetMenu(fileName = "新建Buff行为_修改移动速度", menuName = "Buff/MoverSpeedChange")]
public class Buff_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;
    public override void Apply(BuffRunTime data)
    {
        ISpeed moverSpeed = data.buff_Receiver as ISpeed;
        if (moverSpeed == null)
        {
            // Buff接受者没有速度接口，取消apply
            return;
        }

        moverSpeed.Speed *= SpeedChangeValue;
    }
}