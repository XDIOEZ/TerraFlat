using UnityEngine;

[System.Serializable]
public class BuffAction_MoverSpeedChange : BuffAction
{
    [Header("移动速度改变倍率（>1 加快，<1 减慢）")]
    public float SpeedChangeValue = 1f;

    public override void Apply(BuffRunTime data)
    {
        Item receiver = data?.buff_Receiver;
        if (receiver == null)
            return;

        Mover mod = receiver.itemMods.GetMod_ByID(ModText.Mover) as Mover;
        if (mod?.Speed == null)
            return;

        mod.Speed.MultiplicativeModifier *= SpeedChangeValue;
    }
}
