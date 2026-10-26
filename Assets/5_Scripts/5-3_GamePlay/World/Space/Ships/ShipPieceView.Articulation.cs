using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class ShipPieceView
    {
        #region 船上关节机器共享批绘
        private MachineEntity articulatedEntity;
        private RuntimeItemDefinition articulatedItem;
        private MachineArticulatedVisual articulatedRig;
        private ChunkDepthMeshRenderer articulatedRenderer;

        private void BindArticulatedMachineVisual(MachineEntity node)
        {
            RuntimeItemDefinition item = null;
            if (node != null) GameRes.ExistingInstance?.TryGetItemDefinition(node.Definition.Id, out item);
            MachineArticulatedVisual rig = MachineArticulatedVisual.Resolve(item);
            if (rig == null || visual == null)
            { ReleaseArticulatedMachineVisual(); return; }
            if (!ReferenceEquals(node, articulatedEntity)) ReleaseArticulatedMachineVisual();
            articulatedEntity = node; articulatedItem = item; articulatedRig = rig;
            SubscribeArticulatedMachineVisual();
            RefreshArticulatedMachineVisual();
        }

        private void RefreshArticulatedMachineVisual()
        {
            if (!isActiveAndEnabled || visual == null || articulatedEntity == null || articulatedRig == null) return;
            using var scope = MachineWorld.UseNodeScope(articulatedEntity);
            if (!MachineWorld.Contains(articulatedEntity)) { ReleaseArticulatedMachineVisual(); return; }
            visual.enabled = false;
            articulatedRenderer ??= new ChunkDepthMeshRenderer(visual.transform, visual.sortingLayerID,
                visual.sortingOrder, useParentSortingAnchor: true);
            MachineArticulatedMotionClock clock = MachineArticulatedVisual.SynchronizeClock(articulatedEntity, articulatedRig);
            Bounds bounds = SharedSpriteMeshCache.GetGeometry(articulatedItem.Sprite).Bounds;
            float bodySize = articulatedEntity.Definition.Fluid.ResolveBodySizeCells(articulatedEntity.Definition.Layer);
            float fit = bodySize / Mathf.Max(bounds.size.x, bounds.size.y);
            Matrix4x4 root = visual.transform.localToWorldMatrix;
            for (int i = 0; i < articulatedRig.Parts.Length; i++)
            {
                MachineArticulatedVisual.PartPose pose = articulatedRig.GetPartPose(articulatedItem, i, fit, clock);
                // 船体的平移、朝向和升降由父Transform统一带动，零件仍沿自身关节运动。
                articulatedRenderer.Set(ChunkDepthMeshRenderer.MachineDomain, targetGuid, i,
                    pose.Sprite, visual.sharedMaterial,
                    root * Matrix4x4.TRS(pose.Offset, pose.Rotation, pose.Scale), visual.transform.position,
                    pose.Order, visual.color, animation: pose.Animation,
                    highlighted: IsInteractionHighlighted, conveyor: pose.Parameters);
            }
        }

        private void HandleArticulatedMachineChanged(MachineEntity node)
        { if (ReferenceEquals(node, articulatedEntity)) RefreshArticulatedMachineVisual(); }
        private void HandleArticulatedMachineCellChanged(string scopeKey, Vector2Int cell)
        {
            if (articulatedEntity != null && articulatedEntity.ScopeKey == scopeKey && articulatedEntity.Cell == cell)
                RefreshArticulatedMachineVisual();
        }
        private void SubscribeArticulatedMachineVisual()
        {
            if (articulatedEntity == null || !isActiveAndEnabled) return;
            MachineWorld.NodeStateChanged -= HandleArticulatedMachineChanged;
            MachineWorld.VisualSpeedChanged -= HandleArticulatedMachineChanged;
            MachineWorld.NodeCellChanged -= HandleArticulatedMachineCellChanged;
            MachineWorld.NodeStateChanged += HandleArticulatedMachineChanged;
            MachineWorld.VisualSpeedChanged += HandleArticulatedMachineChanged;
            MachineWorld.NodeCellChanged += HandleArticulatedMachineCellChanged;
        }
        private void SuspendArticulatedMachineVisual()
        {
            MachineWorld.NodeStateChanged -= HandleArticulatedMachineChanged;
            MachineWorld.VisualSpeedChanged -= HandleArticulatedMachineChanged;
            MachineWorld.NodeCellChanged -= HandleArticulatedMachineCellChanged;
            articulatedRenderer?.Dispose(); articulatedRenderer = null;
        }
        private void ReleaseArticulatedMachineVisual()
        {
            bool hadRig = articulatedRig != null;
            SuspendArticulatedMachineVisual();
            articulatedEntity = null; articulatedItem = null; articulatedRig = null;
            if (hadRig && visual != null) visual.enabled = true;
        }
        #endregion
    }
}
