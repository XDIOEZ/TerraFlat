using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class ShipPieceView
    {
        #region 船上电池三格指示灯
        private MachineEntity batteryEntity;
        private readonly SpriteRenderer[] batteryCells = new SpriteRenderer[BatteryChargePresentation.CellCount];
        private int batteryLitCount = -1;

        private void BindBatteryChargeVisual(MachineEntity node)
        {
            MachineEntity source = node?.Definition.Electrical?.IsBattery == true &&
                node.Definition.BatteryIndicator != null ? node : null;
            if (!ReferenceEquals(batteryEntity, source))
            {
                UnsubscribeBatteryChargeVisual();
                batteryEntity = source;
                SubscribeBatteryChargeVisual();
            }
            RefreshBatteryChargeVisual(true);
        }

        private void HandleBatteryChargeChanged(MachineEntity node)
        {
            if (ReferenceEquals(batteryEntity, node)) RefreshBatteryChargeVisual(false);
        }

        // 子色块继承船体和电池的旋转缩放，未达门槛时露出原图暗格。
        private void RefreshBatteryChargeVisual(bool force)
        {
            int count = BatteryChargePresentation.GetLitCount(batteryEntity);
            if (!force && count == batteryLitCount) return;
            batteryLitCount = count;
            for (int i = 0; i < batteryCells.Length; i++)
            {
                SpriteRenderer cell = batteryCells[i];
                if (visual == null || i >= count || !BatteryChargePresentation.TryGetCell(batteryEntity, visual.sprite, i,
                    out Sprite sprite, out Vector3 offset, out Vector3 scale))
                {
                    if (cell != null) cell.enabled = false;
                    continue;
                }
                if (cell == null)
                {
                    var child = new GameObject("BatteryChargeCell_" + i);
                    child.layer = visual.gameObject.layer;
                    child.transform.SetParent(visual.transform, false);
                    cell = batteryCells[i] = child.AddComponent<SpriteRenderer>();
                    cell.spriteSortPoint = SpriteSortPoint.Pivot;
                }
                cell.sprite = sprite;
                cell.color = batteryEntity.Definition.BatteryIndicator.Color;
                cell.sharedMaterial = visual.sharedMaterial;
                cell.sortingLayerID = visual.sortingLayerID;
                cell.sortingOrder = visual.sortingOrder + 1;
                cell.transform.localPosition = new Vector3(visual.flipX ? -offset.x : offset.x,
                    visual.flipY ? -offset.y : offset.y, offset.z);
                cell.transform.localScale = scale;
                cell.enabled = true;
            }
        }

        private void SubscribeBatteryChargeVisual()
        {
            if (batteryEntity == null || !isActiveAndEnabled) return;
            MachineWorld.NodeStateChanged -= HandleBatteryChargeChanged;
            MachineWorld.NodeStateChanged += HandleBatteryChargeChanged;
        }
        private void UnsubscribeBatteryChargeVisual() => MachineWorld.NodeStateChanged -= HandleBatteryChargeChanged;
        private void ReleaseBatteryChargeVisual()
        {
            UnsubscribeBatteryChargeVisual();
            batteryEntity = null;
        }
        #endregion
    }
}
