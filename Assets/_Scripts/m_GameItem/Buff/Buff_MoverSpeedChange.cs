using UnityEngine;

[CreateAssetMenu(fileName = "新建Buff行为_修改移动速度", menuName = "Buff/MoverSpeedChange")]
public class Buff_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;

    public override void Apply(BuffRunTime data)
    {
        ISpeed Speeder = (ISpeed)data.buff_Receiver;

        if (Speeder == null)
        {
            // Buff接受者没有速度接口，取消apply
            return;
        }
        Speeder.Speed.MultiplicativeModifier *= SpeedChangeValue;
    }
}