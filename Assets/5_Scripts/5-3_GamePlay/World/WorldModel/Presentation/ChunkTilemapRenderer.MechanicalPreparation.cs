using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public sealed partial class ChunkTilemapRenderer
{
    #region 机器落地表现提前准备
    private sealed class PreparedMechanicalVisual
    {
        internal MechanicalLandingPreparation Owner;
        internal MachineEntity Node;
        internal MachineDefinition Definition;
        internal MechanicalDepthVisual Visual;
    }

    private static readonly Dictionary<int, PreparedMechanicalVisual> offeredMechanicalVisuals = new();

    /// <summary>只准备停用表现和静态资源，真实机器一直由原作用域继续运行。</summary>
    public sealed class MechanicalLandingPreparation : IDisposable
    {
        private readonly List<MachineEntity> machines;
        private readonly Dictionary<int, PreparedMechanicalVisual> prepared = new();
        private readonly HashSet<MachineDefinition> warmedDefinitions = new();
        private readonly GameRes resources;
        private readonly ulong resourceRevision;
        private readonly int resourceReloadVersion;
        private readonly GameObject root;
        private int cursor;
        private bool disposed, offered;

        public int TotalCount => machines.Count;
        public int PreparedCount { get; private set; }
        public int ConsumedCount { get; private set; }
        public int AvailableCount => prepared.Count;
        public int HiddenCount
        {
            get
            {
                int count = 0;
                foreach (PreparedMechanicalVisual entry in prepared.Values)
                    if (entry.Visual != null && !entry.Visual.gameObject.activeInHierarchy) count++;
                return count;
            }
        }
        public int RemainingCount => machines.Count - cursor;
        public bool IsReady => !disposed && root != null && cursor == machines.Count &&
            ReferenceEquals(resources, GameRes.ExistingInstance) && resources.ItemDefinitionRevision == resourceRevision &&
            resources.ResourceReloadVersion == resourceReloadVersion;

        public MechanicalLandingPreparation(IReadOnlyList<MachineEntity> machines)
        {
            if (machines == null) throw new ArgumentNullException(nameof(machines));
            resources = GameRes.ExistingInstance ?? throw new InvalidOperationException("机器表现资源尚未就绪。");
            resourceRevision = resources.ItemDefinitionRevision;
            resourceReloadVersion = resources.ResourceReloadVersion;
            this.machines = new List<MachineEntity>(machines.Count);
            var ids = new HashSet<int>();
            for (int i = 0; i < machines.Count; i++)
            {
                MachineEntity machine = machines[i];
                if (machine == null || machine.Definition == null || !ids.Add(machine.Id))
                    throw new InvalidOperationException("落地表现准备包含空机器或重复身份。");
                this.machines.Add(machine);
            }
            root = new GameObject("LandingMechanicalPreparation") { hideFlags = HideFlags.DontSave };
            root.SetActive(false);
        }

        public void Step(double budgetMilliseconds)
        {
            EnsureCurrent();
            if (offered) throw new InvalidOperationException("机器表现开始交接后不能继续准备。");
            long started = Stopwatch.GetTimestamp();
            do
            {
                if (cursor >= machines.Count) return;
                MachineEntity node = machines[cursor];
                if (!resources.TryGetItemDefinition(node.Definition.Id, out RuntimeItemDefinition definition))
                    throw new InvalidOperationException("落地机器定义缺失：" + node.Definition.Id);
                if (warmedDefinitions.Add(node.Definition)) PrewarmMechanicalDefinition(node, definition);
                MechanicalDepthVisual visual = MechanicalDepthVisual.CreatePrepared(root.transform, node.Definition, definition);
                prepared.Add(node.Id, new PreparedMechanicalVisual
                { Owner = this, Node = node, Definition = node.Definition, Visual = visual });
                cursor++; PreparedCount++;
            }
            while ((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency < Math.Max(0d, budgetMilliseconds));
        }

        public IDisposable BeginCommit()
        {
            EnsureCurrent();
            if (!IsReady) throw new InvalidOperationException("机器地面表现尚未全部准备完成。");
            foreach (KeyValuePair<int, PreparedMechanicalVisual> entry in prepared)
                if (!ReferenceEquals(entry.Value.Node.Definition, entry.Value.Definition) ||
                    offeredMechanicalVisuals.TryGetValue(entry.Key, out PreparedMechanicalVisual existing) &&
                    !ReferenceEquals(existing.Owner, this))
                    throw new InvalidOperationException("机器身份或定义在落地准备后发生变化：" + entry.Key);
            foreach (KeyValuePair<int, PreparedMechanicalVisual> entry in prepared)
                offeredMechanicalVisuals[entry.Key] = entry.Value;
            offered = true;
            return MachineWorld.BeginSurfaceChangeBatch();
        }

        private void EnsureCurrent()
        {
            if (disposed) throw new ObjectDisposedException(nameof(MechanicalLandingPreparation));
            if (root == null || !ReferenceEquals(resources, GameRes.ExistingInstance) ||
                resources.ItemDefinitionRevision != resourceRevision || resources.ResourceReloadVersion != resourceReloadVersion)
                throw new InvalidOperationException("落地机器表现资源或场景已变化，需要重新准备。");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (KeyValuePair<int, PreparedMechanicalVisual> entry in prepared)
            {
                if (offeredMechanicalVisuals.TryGetValue(entry.Key, out PreparedMechanicalVisual existing) &&
                    ReferenceEquals(existing, entry.Value)) offeredMechanicalVisuals.Remove(entry.Key);
                if (entry.Value.Visual != null) entry.Value.Visual.Dispose();
            }
            prepared.Clear();
            warmedDefinitions.Clear();
            if (root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
        }

        internal MechanicalDepthVisual Consume(int id)
        {
            if (!prepared.Remove(id, out PreparedMechanicalVisual entry)) return null;
            offeredMechanicalVisuals.Remove(id);
            ConsumedCount++;
            return entry.Visual;
        }
    }

    private static bool TryTakePreparedMechanicalVisual(MachineEntity node, ChunkTilemapRenderer owner,
        Vector3 origin, int id, int layer, out MechanicalDepthVisual visual)
    {
        visual = null;
        if (!offeredMechanicalVisuals.TryGetValue(node.Id, out PreparedMechanicalVisual entry) ||
            !ReferenceEquals(entry.Node, node) || !ReferenceEquals(entry.Definition, node.Definition) || entry.Visual == null)
            return false;
        visual = entry.Owner.Consume(node.Id);
        try { visual.AdoptPrepared(owner, origin, id, layer); }
        catch
        {
            visual.Dispose();
            throw;
        }
        return true;
    }

    private static void PrewarmMechanicalDefinition(MachineEntity node, RuntimeItemDefinition definition)
    {
        Config(definition);
        mechanicalFallbackMaterial ??= Resources.Load<Material>("Config/WorldModel/BRG/ChunkBRG-Sprite-Lit");
        if (mechanicalFallbackMaterial == null) throw new InvalidOperationException("机械共享材质缺失。");
        SharedSpriteMeshCache.GetGeometry(definition.Sprite);
        if (definition.Visual?.SpriteStates != null)
            foreach (string state in definition.Visual.SpriteStates.Keys)
                if (definition.TryGetVisualStateSprite(state, out Sprite sprite)) SharedSpriteMeshCache.GetGeometry(sprite);
        if (node.Definition.AxisPortVisual != null) SharedSpriteMeshCache.GetGeometry(StandardAxisPortSprite());
        if (MachineWorld.IsFluidTankBlock(node))
            for (int slice = 0; slice < 9; slice++)
            {
                FluidTankBlockVisual.GetPart(definition.Sprite, slice, 0, out Sprite sprite, out _, out _);
                SharedSpriteMeshCache.GetGeometry(sprite);
            }
        if (node.Definition.Fluid != null)
        {
            if (fluidIndicatorSprite == null)
                fluidIndicatorSprite = Sprite.Create(Texture2D.whiteTexture,
                    new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                    new Vector2(.5f, .5f), Texture2D.whiteTexture.width);
            SharedSpriteMeshCache.GetGeometry(fluidIndicatorSprite);
        }
        if (node.Definition.BatteryIndicator != null)
            for (int i = 0; i < BatteryChargePresentation.CellCount; i++)
                if (BatteryChargePresentation.TryGetCell(node, definition.Sprite, i, out Sprite sprite, out _, out _))
                    SharedSpriteMeshCache.GetGeometry(sprite);
    }
    #endregion

    #region 批量机器格和连接邻居去重刷新
    private readonly HashSet<Vector2Int> mechanicalBatchCells = new();
    private readonly HashSet<Vector2Int> mechanicalBatchNeighbours = new();

    private void HandleMechanicalCellsChanged(IReadOnlyList<Vector2Int> cells)
    {
        if (boundChunk?.Terrain == null || !batchPresentationComplete) return;
        mechanicalBatchCells.Clear(); mechanicalBatchNeighbours.Clear();
        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = WorldTopologyRuntime.NormalizeCell(cells[i]);
            mechanicalBatchCells.Add(cell);
            foreach (Vector2Int direction in wireNeighborDirections)
                mechanicalBatchNeighbours.Add(WorldTopologyRuntime.NormalizeCell(cell + direction));
        }
        mechanicalBatchNeighbours.ExceptWith(mechanicalBatchCells);
        foreach (Vector2Int cell in mechanicalBatchCells) RefreshMechanicalCell(cell);
        foreach (Vector2Int cell in mechanicalBatchNeighbours) RefreshMechanicalConnectionCell(cell);
        mechanicalBatchCells.Clear(); mechanicalBatchNeighbours.Clear();
    }

    private void RefreshMechanicalConnectionCell(Vector2Int cell)
    {
        var origin = boundChunk.Address.ChunkOrigin;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(new Vector2(origin.X, origin.Y), new Vector2(cell.x, cell.y));
        int x = Mathf.RoundToInt(delta.x), y = Mathf.RoundToInt(delta.y);
        if ((uint)x >= (uint)boundChunk.Terrain.Width || (uint)y >= (uint)boundChunk.Terrain.Height) return;
        for (int layer = 0; layer <= 5; layer++)
        {
            MachineEntity node = MachineWorld.GetAt(cell, layer);
            if (node == null) continue;
            var key = new Vector3Int(x, y, layer);
            bool changed = false;
            if (node.Definition.Electrical?.IsWire == true)
                changed = !submittedWireMasks.TryGetValue(key, out int wireMask) ||
                    wireMask != MachineWorld.GetElectricalWireConnectionMask(cell);
            else if (node.Definition.Fluid?.Kind == "pipe")
                changed = !submittedPipeMasks.TryGetValue(key, out int pipeMask) || pipeMask != MachineWorld.GetFluidPipeConnectionMask(node);
            else if (MachineWorld.IsFluidTankBlock(node))
                changed = !submittedTankMasks.TryGetValue(key, out int tankMask) || tankMask != MachineWorld.GetFluidTankConnectionMask(node);
            if (changed) SubmitMechanicalNode(node, x, y);
        }
    }
    #endregion
}
