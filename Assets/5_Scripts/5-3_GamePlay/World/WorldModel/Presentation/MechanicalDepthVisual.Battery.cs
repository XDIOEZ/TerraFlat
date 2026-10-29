using UnityEngine;

public sealed partial class MechanicalDepthVisual
{
    #region 电池三格电量图层
    private const int BatteryPartStart = 32;
    private MachineEntity batteryEntity;
    private int batteryLitCount = -1;

    private void UpdateBatterySprites(MachineEntity entity)
    {
        MachineEntity source = entity.Definition.Electrical?.IsBattery == true &&
            entity.Definition.BatteryIndicator != null ? entity : null;
        if (!ReferenceEquals(batteryEntity, source))
        {
            UnsubscribeBattery();
            batteryEntity = source;
            SubscribeBattery();
        }
        RefreshBatterySprites(true);
    }

    private void HandleBatteryChanged(MachineEntity entity)
    {
        if (ReferenceEquals(batteryEntity, entity)) RefreshBatterySprites(false);
    }

    // 只在跨过电量档位或重建表现时更新三格，不反写电池储能。
    private void RefreshBatterySprites(bool force)
    {
        int count = BatteryChargePresentation.GetLitCount(batteryEntity);
        if (!force && count == batteryLitCount) return;
        PartVisual body = parts[0];
        for (int i = 0; i < BatteryChargePresentation.CellCount; i++)
        {
            int index = BatteryPartStart + i;
            if (i < count && body != null && BatteryChargePresentation.TryGetCell(batteryEntity, body.Sprite, i,
                out Sprite sprite, out Vector3 offset, out Vector3 scale))
            {
                SetFacilitySprite(index, sprite, body.Material,
                    body.Offset + body.Rotation * Vector3.Scale(offset, body.Scale),
                    3 + i, batteryEntity.Definition.BatteryIndicator.Color,
                    Vector3.Scale(scale, body.Scale), body.Rotation);
            }
            else if (index < facilityParts.Count && facilityParts[index] is PartVisual part && part.Visible)
            {
                part.Visible = false;
                owner?.RemoveMachineDepthPart(entityId, part.Id);
            }
        }
        batteryLitCount = count;
    }

    private void SubscribeBattery()
    {
        if (batteryEntity == null || !isActiveAndEnabled) return;
        MachineWorld.NodeStateChanged -= HandleBatteryChanged;
        MachineWorld.NodeStateChanged += HandleBatteryChanged;
    }
    private void UnsubscribeBattery() => MachineWorld.NodeStateChanged -= HandleBatteryChanged;
    #endregion
}
