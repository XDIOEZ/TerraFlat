using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "新建Buff行为_根据条件修改移动速度", menuName = "Buff/根据条件修改移动速度")]
public class BuffAction_SpeedChange_Water : BuffAction
{
    public override void Apply(BuffRunTime data)
    {/*
        ISpeed moverSpeed = data.buff_Receiver as ISpeed;
        IBlockTile blockTile = data.buff_Receiver as Item_Tile_Water;

        if (blockTile != null)
        {
           float waterDepth = blockTile.Data_Tile.itemValues.GetValue("水深").CurrentValue;

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
