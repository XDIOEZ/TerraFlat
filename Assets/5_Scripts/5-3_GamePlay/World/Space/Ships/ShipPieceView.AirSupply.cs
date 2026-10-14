using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class ShipPieceView
    {
        #region 舱内供气器指示灯
        private static readonly int IndicatorRectId = Shader.PropertyToID("_DeviceIndicatorRect");
        private static readonly int IndicatorTintId = Shader.PropertyToID("_DeviceIndicatorTint");
        private static readonly Color AirSupplyOffTint = new(.16f, .16f, .16f, 1f);
        private static readonly Color AirSupplyFaultTint = new(1f, .24f, .24f, 1f);
        private MaterialPropertyBlock airSupplyProperties;
        private Color airSupplyAppliedTint;
        private Vector4 airSupplyAppliedRect;
        private bool airSupplyIndicatorApplied, airSupplyIndicatorFault;

        // 故障只预留表现接口，断电始终熄灯，具体触发条件由后续故障规则提供。
        public void SetAirSupplyIndicatorFault(bool fault)
        {
            airSupplyIndicatorFault = fault;
            SynchronizeAirSupplyIndicator();
        }

        private void SynchronizeAirSupplyIndicator()
        {
            if (visual == null || visual.sprite == null) return;
            bool isAirSupply = boundPiece?.ItemId == "ShipAirSupply";
            if (!isAirSupply && !airSupplyIndicatorApplied) return;
            Color tint = Color.clear;
            Vector4 rect = Vector4.zero;
            if (isAirSupply)
            {
                bool powered;
                using (MachineWorld.UseScope("ship:" + shipId))
                    powered = MachineWorld.GetById(TargetGuid)?.ElectricalSuppliedWatts > 0f;
                tint = !powered ? AirSupplyOffTint : airSupplyIndicatorFault ? AirSupplyFaultTint : Color.clear;

                // 只覆盖原图左上起算的十二乘三像素灯玻璃，保留周围金属边框。
                Sprite sprite = visual.sprite;
                float minX = (26f - sprite.pivot.x) / sprite.pixelsPerUnit;
                float maxX = (38f - sprite.pivot.x) / sprite.pixelsPerUnit;
                float minY = (sprite.rect.height - 14f - sprite.pivot.y) / sprite.pixelsPerUnit;
                float maxY = (sprite.rect.height - 11f - sprite.pivot.y) / sprite.pixelsPerUnit;
                rect = new Vector4(visual.flipX ? -maxX : minX, visual.flipY ? -maxY : minY,
                    visual.flipX ? -minX : maxX, visual.flipY ? -minY : maxY);
            }
            if (airSupplyIndicatorApplied && airSupplyAppliedTint == tint && airSupplyAppliedRect == rect) return;
            airSupplyProperties ??= new MaterialPropertyBlock();
            visual.GetPropertyBlock(airSupplyProperties);
            airSupplyProperties.SetVector(IndicatorRectId, rect);
            airSupplyProperties.SetColor(IndicatorTintId, tint);
            visual.SetPropertyBlock(airSupplyProperties);
            airSupplyAppliedTint = tint;
            airSupplyAppliedRect = rect;
            airSupplyIndicatorApplied = isAirSupply;
        }
        #endregion
    }
}
