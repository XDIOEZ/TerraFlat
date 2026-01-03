using UnityEngine;

[System.Serializable]
public class BuffAction_MoverSpeedChange : BuffAction
{
    [Header("移动速度改变倍率（>1加快，<1减慢）(乘算倍率)")]
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
}