using UnityEngine;

public sealed partial class MechanicalDepthVisual
{
    #region 培育仓植株数据图层
    private const int CultivatorPartStart = 16;
    private readonly CultivatorPlotPresentation cultivatorPlots = new();
    private MachineEntity cultivatorEntity;

    private void UpdateCultivatorSprites(MachineEntity entity)
    {
        MachineEntity source = entity.Definition.LogicId == "cultivator" ? entity : null;
        if (!ReferenceEquals(cultivatorEntity, source))
        {
            UnsubscribeCultivator();
            cultivatorEntity = source;
            SubscribeCultivator();
        }
        RefreshCultivatorSprites();
    }

    // 成长与库存通知只更新四个内容图层，不重建机身或额外推进作物。
    private void HandleCultivatorChanged(MachineEntity entity)
    {
        if (ReferenceEquals(cultivatorEntity, entity)) RefreshCultivatorSprites();
    }
    private void SubscribeCultivator()
    {
        if (cultivatorEntity == null || !isActiveAndEnabled) return;
        MachineWorld.NodeStateChanged -= HandleCultivatorChanged;
        MachineWorld.NodeStateChanged += HandleCultivatorChanged;
    }
    private void UnsubscribeCultivator() => MachineWorld.NodeStateChanged -= HandleCultivatorChanged;

    private void RefreshCultivatorSprites()
    {
        cultivatorPlots.Refresh(cultivatorEntity);
        PartVisual body = parts[0];
        for (int i = 0; i < 16; i++)
        {
            int index = CultivatorPartStart + i;
            if (body != null && cultivatorEntity != null && i < cultivatorPlots.PlotCount &&
                cultivatorPlots.TryGetPlot(i, body.Sprite, out Sprite sprite, out Vector3 offset, out Vector3 scale))
            {
                SetFacilitySprite(index, sprite, body.Material,
                    body.Offset + body.Rotation * Vector3.Scale(offset, body.Scale),
                    16 + i, Color.white, Vector3.Scale(scale, body.Scale), body.Rotation);
            }
            else if (index < facilityParts.Count && facilityParts[index] is PartVisual part && part.Visible)
            {
                part.Visible = false;
                owner?.RemoveMachineDepthPart(entityId, part.Id);
            }
        }
    }
    #endregion
}
