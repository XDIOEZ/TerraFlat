using UnityEngine;

[CreateAssetMenu(fileName = "新建Buff行为_修改移动速度", menuName = "Buff/MoverSpeedChange")]
public class BuffAction_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;
    public Mover SpeedMod;

    public override void Apply(BuffRunTime data)
    {
        if (SpeedMod == null)
        {
            data.buff_Receiver.itemMods.GetMod_ByID(ModText.Mover, out SpeedMod);

            if (SpeedMod == null)
            {
                Debug.LogError("BuffAction_MoverSpeedChange: SpeedMod is null.");
                return;
            }
        }

        

        SpeedMod.Speed.MultiplicativeModifier *= SpeedChangeValue;
    }
}