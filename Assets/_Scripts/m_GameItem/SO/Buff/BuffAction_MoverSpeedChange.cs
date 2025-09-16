using UnityEngine;

[CreateAssetMenu(fileName = "�½�Buff��Ϊ_�޸��ƶ��ٶ�", menuName = "Buff/MoverSpeedChange")]
public class BuffAction_MoverSpeedChange : BuffAction
{
    public float SpeedChangeValue;

    public override void Apply(BuffRunTime data)
    {
        data.buff_Receiver.itemMods.GetMod_ByID(ModText.Mover, out Mover mod);
        if (mod == null)
        {
            // Buff������û���ٶȽӿڣ�ȡ��apply
            return;
        }
        mod.Speed.MultiplicativeModifier *= SpeedChangeValue;
    }
}