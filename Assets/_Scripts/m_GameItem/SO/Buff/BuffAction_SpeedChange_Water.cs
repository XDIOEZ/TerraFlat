using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "�½�Buff��Ϊ_���������޸��ƶ��ٶ�", menuName = "Buff/���������޸��ƶ��ٶ�")]
public class BuffAction_SpeedChange_Water : BuffAction
{
    public override void Apply(BuffRunTime data)
    {/*
        ISpeed moverSpeed = data.buff_Receiver as ISpeed;
        IBlockTile blockTile = data.buff_Receiver as Item_Tile_Water;

        if (blockTile != null)
        {
           float waterDepth = blockTile.Data_Tile.itemValues.GetValue("ˮ��").CurrentValue;

            if (waterDepth > 0&&waterDepth<1)
            {
                moverSpeed.Speed *= 0.5f;
            }
            if (waterDepth > 1 && waterDepth < 2)
            {
                moverSpeed.Speed *= 0.3f;
            }
            if (waterDepth > 2)
            {
                moverSpeed.Speed *= 0f;
            }
        }*/
    }
}
