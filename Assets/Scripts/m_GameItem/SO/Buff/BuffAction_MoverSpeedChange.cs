using UnityEngine;

[CreateAssetMenu(fileName = "新建Buff行为_修改移动速度", menuName = "Buff/MoverSpeedChange")]
public class BuffAction_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;
    [SerializeField]
    Mover mod;

    public override void Apply(BuffRunTime data)
    {
        if (mod == null)
        {
            data.buff_Receiver.itemMods.GetMod_ByID(ModText.Mover, out mod);

            if (mod == null)
            {
                Debug.LogError("BuffAction_MoverSpeedChange: SpeedMod is null.");
                return;
            }
        }

        mod.Speed.MultiplicativeModifier *= SpeedChangeValue;
    }

    public override BuffAction Clone()
    {
        var newBuff = Instantiate(this);
        Mover newMod = null;
        newBuff.mod = newMod; // 防止引用污染
        return newBuff;
    }
}