using UnityEngine;

[CreateAssetMenu(fileName = "新建Buff行为_修改移动速度", menuName = "Buff/MoverSpeedChange")]
public class BuffAction_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;

    public override void Apply(BuffRunTime data)
    {
        data.buff_Receiver.itemMods.GetMod_ByID(ModText.Mover, out Mover mod);
        if (mod == null)
        {
            // Buff接受者没有速度接口，取消apply
            return;
        }
        mod.Speed.MultiplicativeModifier *= SpeedChangeValue;
    }
}