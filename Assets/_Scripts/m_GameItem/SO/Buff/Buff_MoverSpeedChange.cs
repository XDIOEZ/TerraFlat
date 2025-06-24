using UnityEngine;

[CreateAssetMenu(fileName = "新建Buff行为_修改移动速度", menuName = "Buff/MoverSpeedChange")]
public class Buff_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;

    public override void Apply(BuffRunTime data)
    {
        if (data.buff_Receiver is not ISpeed speeder)
        {
            // Buff接受者没有速度接口，取消apply
            return;
        }
        speeder.Speed.MultiplicativeModifier *= SpeedChangeValue;
    }
}