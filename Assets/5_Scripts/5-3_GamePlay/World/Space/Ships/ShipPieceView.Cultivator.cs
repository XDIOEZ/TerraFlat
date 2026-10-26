using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class ShipPieceView
    {
        #region 船上培育仓的独立植株图层
        private readonly CultivatorPlotPresentation cultivatorPlots = new();
        private readonly SpriteRenderer[] cultivatorSprites = new SpriteRenderer[16];
        private MachineEntity cultivatorEntity;
        private CultivatorLogic cultivatorLogic;

        private void BindCultivatorVisual(MachineEntity node)
        {
            MachineEntity source = node?.Definition.LogicId == "cultivator" ? node : null;
            if (!ReferenceEquals(cultivatorEntity, source))
            {
                UnsubscribeCultivatorVisual();
                cultivatorEntity = source;
                SubscribeCultivatorVisual();
            }
            RefreshCultivatorSprites();
        }
        private void HandleCultivatorChanged(MachineEntity node)
        {
            if (ReferenceEquals(cultivatorEntity, node)) RefreshCultivatorSprites();
        }
        // 子图层继承机身的移动和旋转，空槽立即停用已有植物 Renderer。
        private void RefreshCultivatorSprites()
        {
            var logic = cultivatorEntity?.Logic as CultivatorLogic;
            if (!ReferenceEquals(cultivatorLogic, logic))
            {
                if (cultivatorLogic != null) cultivatorLogic.Changed -= RefreshCultivatorSprites;
                cultivatorLogic = logic;
                if (isActiveAndEnabled && logic != null) logic.Changed += RefreshCultivatorSprites;
            }
            cultivatorPlots.Refresh(cultivatorEntity);
            for (int i = 0; i < cultivatorSprites.Length; i++)
            {
                SpriteRenderer plant = cultivatorSprites[i];
                if (visual == null || cultivatorEntity == null || i >= cultivatorPlots.PlotCount ||
                    !cultivatorPlots.TryGetPlot(i, visual.sprite, out Sprite sprite, out Vector3 offset, out Vector3 scale))
                {
                    if (plant != null) plant.enabled = false;
                    continue;
                }
                if (plant == null)
                {
                    var child = new GameObject("CultivatorCrop_" + i);
                    child.transform.SetParent(visual.transform, false);
                    plant = cultivatorSprites[i] = child.AddComponent<SpriteRenderer>();
                    plant.spriteSortPoint = SpriteSortPoint.Pivot;
                }
                plant.sprite = sprite;
                plant.sharedMaterial = visual.sharedMaterial;
                plant.sortingLayerID = visual.sortingLayerID;
                plant.sortingOrder = visual.sortingOrder + 1;
                plant.transform.localPosition = offset;
                plant.transform.localScale = scale;
                plant.enabled = true;
            }
        }
        private void ReleaseCultivatorVisual()
        {
            UnsubscribeCultivatorVisual();
            cultivatorEntity = null;
        }
        private void SubscribeCultivatorVisual()
        {
            if (cultivatorEntity == null || !isActiveAndEnabled) return;
            MachineWorld.NodeStateChanged -= HandleCultivatorChanged;
            MachineWorld.NodeStateChanged += HandleCultivatorChanged;
            if (cultivatorLogic != null)
            {
                cultivatorLogic.Changed -= RefreshCultivatorSprites;
                cultivatorLogic.Changed += RefreshCultivatorSprites;
            }
        }
        private void UnsubscribeCultivatorVisual()
        {
            MachineWorld.NodeStateChanged -= HandleCultivatorChanged;
            if (cultivatorLogic != null) cultivatorLogic.Changed -= RefreshCultivatorSprites;
            cultivatorLogic = null;
        }
        private void OnEnable()
        {
            SubscribeCultivatorVisual();
            if (cultivatorEntity != null) RefreshCultivatorSprites();
            SubscribeBatteryChargeVisual();
            if (batteryEntity != null) RefreshBatteryChargeVisual(true);
            SubscribeElectricLightVisual();
            if (lightEntity != null) RefreshElectricLightVisual();
            SubscribeArticulatedMachineVisual();
            RefreshArticulatedMachineVisual();
        }
        private void OnDisable()
        {
            UnsubscribeCultivatorVisual();
            UnsubscribeBatteryChargeVisual();
            UnsubscribeElectricLightVisual();
            DisableElectricLightVisual();
            SuspendArticulatedMachineVisual();
        }
        #endregion
    }
}
