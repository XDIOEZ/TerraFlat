using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>机械节点的分层表现；主体与运动部件共用脚点行网格，阴影继续独立批绘。</summary>
public sealed partial class ChunkTilemapRenderer
{
    #region 机械表现
    private const string ClutchEngagedSpriteState = "clutchEngaged"; // 离合器接合状态的主贴图键。
    private const string ClutchDisengagedSpriteState = "clutchDisengaged"; // 离合器断开状态的主贴图键。
    private const string ReciprocatingSpriteState = "reciprocating"; // 往复运动的独立图层键。
    private const string BellowsLeatherSpriteState = "bellowsLeather"; // 风箱皮革折页的独立图层键。
    private const int BellowsCompressionMode = 4; // 按机械相位沿局部纵轴压缩皮革折页。
    private static readonly Vector2Int[] wireNeighborDirections =
        { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private static readonly string[] wireSpriteStates =
        { "wire0", "wire1", "wire2", "wire3", "wire4", "wire5", "wire6", "wire7",
          "wire8", "wire9", "wire10", "wire11", "wire12", "wire13", "wire14", "wire15" };
    private static readonly string[] pipeSpriteStates =
        { "pipe0", "pipe1", "pipe2", "pipe3", "pipe4", "pipe5", "pipe6", "pipe7",
          "pipe8", "pipe9", "pipe10", "pipe11", "pipe12", "pipe13", "pipe14", "pipe15" };

    /// <summary>机械物品定义中的图层坐标，新增 MOD 机械可沿用同一参数契约。</summary>
    private sealed class MechanicalVisualConfig
    {
        public float InputShaftOffsetX = -.45f;
        public Vector3 ElectricalPortLocalPosition; // 电线口沿用机身遮挡独立接头的分层方式。
        public Vector3 InputShaftLocalPosition = new(-.55f, 0f, 0f);
        public Vector3 RotorLocalPosition;
        public bool RotorBehindBody; // 锯片等嵌入式转子由机身前沿遮住下部。
        public Vector3 ReciprocatingLocalPosition; // 往复件静止时相对主体锚点的位置。
        public float ReciprocatingStroke; // 往复件沿局部向下方向的世界行程。
        public float BellowsCompression = .32f; // 风囊收缩时相对展开高度的最大比例。
        public Vector3 GearboxLargeGearLocalPosition = new(-12f / 128f, 0f, 0f);
        public Vector3 GearboxSmallGearLocalPosition = new(24f / 128f, 0f, 0f);
        public float GearboxLargeGearScale = .875f;
        public float GearboxSmallGearScale = .4375f;
    }

    private static readonly Dictionary<string, MechanicalVisualConfig> mechanicalVisualConfigs = new(StringComparer.Ordinal);
    private readonly List<MachineWorld.MachineRenderCell> mechanicalVisualNodes = new();
    private readonly Dictionary<Vector3Int, MechanicalDepthVisual> mechanicalDepthVisuals = new(); // 机器的交互和灯光桥，图像统一合入行网格。
    private readonly HashSet<Vector3Int> submittedMechanicalCells = new(); // 资源重建时清理已提交的机械和阴影身份。
    private int submittedMechanicalHighestLayer = -1; // 保留旧最高层，最后一个高层节点拆除时仍能清理表现。
    private readonly Dictionary<Vector3Int, int> submittedWireMasks = new(); // 相邻事件只重提连接发生变化的电线。
    private readonly Dictionary<Vector3Int, int> submittedPipeMasks = new(); // 管口增删和旋转只刷新连接外形变化的邻管。
    private readonly Dictionary<Vector3Int, int> submittedTankMasks = new(); // 储罐边框只在四邻连接变化时重新提交。
    internal HashSet<Vector3Int> MechanicalShadowKeys => submittedMechanicalCells; // 供阴影注册表按区块卸载。
    private static Material mechanicalFallbackMaterial;
    private static Sprite mechanicalAxisPortSprite;
    private static Sprite fluidIndicatorSprite;

    /// <summary>新资源会话重新解析 MOD 多图层参数。</summary>
    private static void ResetMechanicalVisualCache()
    {
        mechanicalVisualConfigs.Clear();
        offeredMechanicalVisuals.Clear();
        mechanicalFallbackMaterial = null;
        mechanicalAxisPortSprite = null;
        if (fluidIndicatorSprite != null) UnityEngine.Object.Destroy(fluidIndicatorSprite);
        fluidIndicatorSprite = null;
    }

    /// <summary>基础地形建立后接入机械数据变化和表现重建通知。</summary>
    private void BindMechanicalPresentation()
    {
        MachineWorld.SurfacePresentationCellChanged += HandleMechanicalCellChanged;
        MachineWorld.SurfacePresentationCellsChanged += HandleMechanicalCellsChanged;
        BatchPresentationRebuilt += RefreshMechanicalPresentation;
    }

    /// <summary>区块卸载时解除事件，回收行网格部件、交互和阴影注册。</summary>
    private void UnbindMechanicalPresentation()
    {
        MachineWorld.SurfacePresentationCellChanged -= HandleMechanicalCellChanged;
        MachineWorld.SurfacePresentationCellsChanged -= HandleMechanicalCellsChanged;
        BatchPresentationRebuilt -= RefreshMechanicalPresentation;
        mechanicalVisualNodes.Clear();
        ClearMechanicalDepthVisuals();
        MechanicalShadowRegistry.RemoveOwner(this);
        submittedMechanicalCells.Clear();
        submittedMechanicalHighestLayer = -1;
    }

    /// <summary>从权威机械数据重建当前区块所有图层。</summary>
    private void RefreshMechanicalPresentation()
    {
        if (boundChunk?.Terrain == null || !batchPresentationComplete) return;
        ClearMechanicalDepthVisuals();
        foreach (Vector3Int key in submittedMechanicalCells)
        {
            MechanicalShadowRegistry.Remove(this, key);
        }
        submittedMechanicalCells.Clear();
        submittedMechanicalHighestLayer = -1;
        var origin = boundChunk.Address.ChunkOrigin;
        MachineWorld.CollectInBounds(new BoundsInt(origin.X, origin.Y, 0,
            boundChunk.Terrain.Width, boundChunk.Terrain.Height, 1), mechanicalVisualNodes);
        foreach (MachineWorld.MachineRenderCell entry in mechanicalVisualNodes)
            SubmitMechanicalNode(entry.Node, entry.DisplayCell.x - origin.X,
                entry.DisplayCell.y - origin.Y);
    }

    /// <summary>局部放置、拆除或转速变化只刷新对应格的表现部件。</summary>
    private void HandleMechanicalCellChanged(Vector2Int cell)
    {
        if (boundChunk?.Terrain == null || !batchPresentationComplete) return;
        RefreshElectricalWireNeighbours(cell);
        RefreshFluidPipeNeighbours(cell);
        RefreshFluidTankNeighbours(cell);
        RefreshMechanicalCell(cell);
    }

    private void RefreshMechanicalCell(Vector2Int cell)
    {
        var origin = boundChunk.Address.ChunkOrigin;
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(
            new Vector2(origin.X, origin.Y), new Vector2(cell.x, cell.y));
        int x = Mathf.RoundToInt(displacement.x), y = Mathf.RoundToInt(displacement.y);
        if ((uint)x >= (uint)boundChunk.Terrain.Width || (uint)y >= (uint)boundChunk.Terrain.Height) return;
        int highestLayer = Math.Max(MachineWorld.HighestOccupiedLayer, submittedMechanicalHighestLayer);
        for (int occupancy = 0; occupancy <= highestLayer; occupancy++)
        {
            MachineEntity node = MachineWorld.GetAt(cell, occupancy);
            if (node != null) SubmitMechanicalNode(node, x, y);
            else
            {
                MechanicalShadowRegistry.Remove(this, new Vector3Int(x, y, occupancy));
                submittedMechanicalCells.Remove(new Vector3Int(x, y, occupancy));
                submittedWireMasks.Remove(new Vector3Int(x, y, occupancy));
                submittedPipeMasks.Remove(new Vector3Int(x, y, occupancy));
                submittedTankMasks.Remove(new Vector3Int(x, y, occupancy));
                RemoveMechanicalDepthVisual(x, y, occupancy);
            }
        }
    }

    /// <summary>液体与平台变化只重提水下状态真正改变的机器格，流量变化不重建部件。</summary>
    private void RefreshMechanicalSubmergedSurfaceCells(FlatWorld.WorldModel.ChunkTerrainChanged changed)
    {
        if (!batchPresentationComplete || mechanicalDepthVisuals.Count == 0 ||
            !(changed.Kind == FlatWorld.WorldModel.TerrainChangeKind.Liquid ||
              changed.AffectsEnvironmentLayer(FlatWorld.WorldModel.TerrainSupportLayer.TileIdLayer))) return;
        foreach (int index in changed.GetCellIndices(boundChunk.Terrain.Width))
            RefreshMechanicalSubmergedSurfaceCell(index);
    }

    private void RefreshMechanicalSubmergedSurfaceCell(int index)
    {
        if (!batchPresentationComplete || mechanicalDepthVisuals.Count == 0) return;
        int width = boundChunk.Terrain.Width;
        var origin = boundChunk.Address.ChunkOrigin;
        int x = index % width, y = index / width;
        for (int layer = 0; layer <= submittedMechanicalHighestLayer; layer++)
            if (mechanicalDepthVisuals.TryGetValue(new Vector3Int(x, y, layer), out var visual) &&
                visual.RequiresSubmergedSurfaceRefresh)
            {
                RefreshMechanicalCell(WorldTopologyRuntime.NormalizeCell(new Vector2Int(origin.X + x, origin.Y + y)));
                break;
            }
    }

    /// <summary>跨区块与环绕边界的相邻罐体沿正式同材共享组更新外壳。</summary>
    private void RefreshFluidTankNeighbours(Vector2Int changedCell)
    {
        var origin = boundChunk.Address.ChunkOrigin;
        foreach (Vector2Int direction in wireNeighborDirections)
        {
            Vector2Int cell = changedCell + direction;
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(
                new Vector2(origin.X, origin.Y), new Vector2(cell.x, cell.y));
            int x = Mathf.RoundToInt(delta.x), y = Mathf.RoundToInt(delta.y);
            if ((uint)x >= (uint)boundChunk.Terrain.Width || (uint)y >= (uint)boundChunk.Terrain.Height) continue;
            MachineEntity node = MachineWorld.GetAt(cell, 0);
            if (!MachineWorld.IsFluidTankBlock(node)) continue;
            var key = new Vector3Int(x, y, node.Definition.Layer);
            int mask = MachineWorld.GetFluidTankConnectionMask(node);
            if (!submittedTankMasks.TryGetValue(key, out int previous) || previous != mask)
                SubmitMechanicalNode(node, x, y);
        }
    }

    /// <summary>正交邻格也接收变化，跨区块及循环世界边界使用同一权威电线索引。</summary>
    private void RefreshElectricalWireNeighbours(Vector2Int changedCell)
    {
        var origin = boundChunk.Address.ChunkOrigin;
        foreach (Vector2Int direction in wireNeighborDirections)
        {
            Vector2Int cell = changedCell + direction;
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(
                new Vector2(origin.X, origin.Y), new Vector2(cell.x, cell.y));
            int x = Mathf.RoundToInt(delta.x), y = Mathf.RoundToInt(delta.y);
            if ((uint)x >= (uint)boundChunk.Terrain.Width || (uint)y >= (uint)boundChunk.Terrain.Height) continue;
            MachineEntity wire = MachineWorld.GetElectricalWireAtCurrentWorld(cell);
            if (wire == null) continue;
            var key = new Vector3Int(x, y, wire.Definition.Layer);
            int mask = MachineWorld.GetElectricalWireConnectionMask(cell);
            if (!submittedWireMasks.TryGetValue(key, out int previous) || previous != mask)
                SubmitMechanicalNode(wire, x, y);
        }
    }

    /// <summary>相邻设备管口变化沿正式管网刷新，循环边界与跨区块共用同一查询。</summary>
    private void RefreshFluidPipeNeighbours(Vector2Int changedCell)
    {
        var origin = boundChunk.Address.ChunkOrigin;
        foreach (Vector2Int direction in wireNeighborDirections)
        {
            Vector2Int cell = changedCell + direction;
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(
                new Vector2(origin.X, origin.Y), new Vector2(cell.x, cell.y));
            int x = Mathf.RoundToInt(delta.x), y = Mathf.RoundToInt(delta.y);
            if ((uint)x >= (uint)boundChunk.Terrain.Width || (uint)y >= (uint)boundChunk.Terrain.Height) continue;
            MachineEntity node = MachineWorld.GetFluidPipeAt(cell);
            if (node?.Definition.IsFluidPipeSegment != true) continue;
            var key = new Vector3Int(x, y, node.Definition.Layer);
            int mask = MachineWorld.GetFluidPipeConnectionMask(node);
            if (!submittedPipeMasks.TryGetValue(key, out int previous) || previous != mask)
                SubmitMechanicalNode(node, x, y);
        }
    }

    /// <summary>按机械类型组合 Sprite，视觉状态来自物品定义而不是运行时 Item。</summary>
    private void SubmitMechanicalNode(MachineEntity node, int x, int y)
    {
        if (node.Definition.Electrical?.Hidden == true && !EngineeringVision.LocalCanSee)
        {
            var hiddenKey = new Vector3Int(x, y, node.Definition.Layer);
            RemoveMechanicalDepthVisual(x, y, node.Definition.Layer);
            MechanicalShadowRegistry.Remove(this, hiddenKey);
            submittedMechanicalCells.Remove(hiddenKey);
            submittedWireMasks.Remove(hiddenKey);
            return;
        }
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(node.Definition.Id, out RuntimeItemDefinition def) ||
            def.Sprite == null) throw new InvalidOperationException("机械本体 Sprite 缺失：" + node.Definition.Id);
        mechanicalFallbackMaterial ??= Resources.Load<Material>("Config/WorldModel/BRG/ChunkBRG-Sprite-Lit");
        if (mechanicalFallbackMaterial == null) throw new InvalidOperationException("机械共享材质缺失。");
        Material material = def.Material ?? mechanicalFallbackMaterial;
        MechanicalVisualConfig config = Config(def);
        // 循环世界的显示区块可能使用非规范坐标；实例位置必须跟当前区块格一致。
        var chunkOrigin = boundChunk.Address.ChunkOrigin;
        Vector3 origin = new(chunkOrigin.X + x + .5f, chunkOrigin.Y + y + .5f,
            node.Snapshot.transform.position.z);
        origin += DepthPresentationOffset;
        bool mirrorConverterX = node.Definition.IsConverter && (node.RotationQuarterTurns & 3) == 2;
        Quaternion rotation = Quaternion.Euler(0f, 0f,
            mirrorConverterX ? 0f : (node.RotationQuarterTurns & 3) * 90f);
        Vector3 facilityBodyOffset = node.Definition.Ports == "none"
            ? (def.Visual?.RendererLocalPosition ?? Vector3.zero)
            : Vector3.zero;
        Vector3 facilityBodyScale = node.Definition.Ports == "none"
            ? (def.Visual?.RendererLocalScale ?? Vector3.one)
            : Vector3.one;
        string kind = node.Definition.Kind;
        Vector3Int key = new(x, y, node.Definition.Layer);
        submittedMechanicalCells.Add(key);
        submittedMechanicalHighestLayer = Math.Max(submittedMechanicalHighestLayer, node.Definition.Layer);
        if (node.Definition.ShouldCastVisualShadows())
            MechanicalShadowRegistry.BeginNode(this, key, gameObject.scene, def.Sprite,
                origin + rotation * facilityBodyOffset, rotation, facilityBodyScale, def.Visual?.Shadows);
        else MechanicalShadowRegistry.Remove(this, key);
        PrepareMechanicalDepthVisual(node, x, y, origin);
        MechanicalDepthVisual depthVisual = mechanicalDepthVisuals[key];
        try
        {

        if (node.Definition.Fluid != null)
        {
            SubmitFluidNode(node, x, y, def, material, origin, rotation);
            return;
        }

        if (node.Definition.Transport != null)
        {
            SubmitConveyor(node, x, y, def, material, origin);
            return;
        }
        if (node.Definition.Electrical?.IsWire == true)
        {
            int mask = MachineWorld.GetElectricalWireConnectionMask(node.Cell);
            Sprite wire = def.TryGetVisualStateSprite(wireSpriteStates[mask], out Sprite connected)
                ? connected : def.Sprite;
            // 接线形状由世界方向决定，忽略召唤器旋转并保持一格宽度。
            Part(node, x, y, 0, wire, material, origin, Quaternion.identity,
                facilityBodyOffset, Vector3.one, 0, 0f);
            submittedWireMasks[key] = mask;
            return;
        }

        if (kind == "shaft")
        {
            Sprite core = State(def, "shaftRollingCore");
            Part(node, x, y, 0, core, material, origin, rotation, Vector3.zero,
                Fit(core, 116f / 128f, 28f / 128f), 2, 1f, drawBelowMechanical: true);
            Part(node, x, y, 3, State(def, "shaftEndRings"), material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f, drawBelowMechanical: true);
            return;
        }
        if (kind == "gear")
        {
            Part(node, x, y, 0, State(def, "inputShaft"), material, origin, rotation,
                new Vector3(config.InputShaftOffsetX, 0f), Vector3.one, 0, 0f, drawBelowMechanical: true);
            float toothPhase = ((node.Cell.x + node.Cell.y) & 1) == 0 ? 0f : Mathf.PI / 8f;
            Part(node, x, y, 1, def.Sprite, material, origin, rotation, Vector3.zero,
                Vector3.one, 1, 1f, toothPhase);
            return;
        }
        if (kind == "bellows")
        {
            Part(node, x, y, 0, State(def, "inputShaft"), material, origin, rotation,
                config.InputShaftLocalPosition, Vector3.one, 0, 0f, drawBelowMechanical: true);
            Part(node, x, y, 1, def.Sprite, material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            Part(node, x, y, 2, State(def, BellowsLeatherSpriteState), material, origin, rotation,
                Vector3.zero, Vector3.one, BellowsCompressionMode, 1f,
                stroke: Mathf.Clamp01(config.BellowsCompression));
            return;
        }

        if (kind == "bridge")
        {
            Sprite core = State(def, "shaftRollingCore");
            Vector3 scale = Fit(core, 24f / 128f, 28f / 128f);
            Part(node, x, y, 0, core, material, origin, rotation,
                new Vector3(-44f / 128f, 0f), scale, 2, 1f, drawBelowMechanical: true);
            Part(node, x, y, 1, core, material, origin, rotation,
                new Vector3(44f / 128f, 0f), scale, 2, 1f, drawBelowMechanical: true);
            Part(node, x, y, 2, def.Sprite, material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f, drawBelowMechanical: true);
            Part(node, x, y, 3, State(def, "shaftEndRings"), material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f, drawBelowMechanical: true);
            return;
        }

        if (kind == "gearbox")
        {
            int part = Ports(node, x, y, 0, material, origin, rotation);
            Part(node, x, y, part++, def.Sprite, material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            Sprite gear = State(def, "gearboxGear");
            Part(node, x, y, part++, gear, material, origin, rotation,
                config.GearboxLargeGearLocalPosition, Vector3.one * config.GearboxLargeGearScale, 1, 1f,
                track: 1);
            Part(node, x, y, part++, gear, material, origin, rotation,
                config.GearboxSmallGearLocalPosition, Vector3.one * config.GearboxSmallGearScale,
                1, 1f, Mathf.PI / 8f, track: 2);
            return;
        }

        if (kind == "clutch")
        {
            string state = node.Engaged ? ClutchEngagedSpriteState : ClutchDisengagedSpriteState;
            // 拓扑状态变更会刷新当前格，直接重选对应的本体贴图。
            Part(node, x, y, 0, State(def, state), material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            return;
        }

        if (def.TryGetVisualStateSprite("electricalPort", out _))
        {
            // 接头保持原始 PPU，伸入机身的部分由上层外壳遮住。
            int part = Ports(node, x, y, 0, material, origin, rotation);
            if (def.TryGetVisualStateSprite("electricalPort", out Sprite cablePort))
                Part(node, x, y, part++, cablePort, material, origin, rotation,
                    config.ElectricalPortLocalPosition, Vector3.one, 0, 0f);
            Part(node, x, y, part++, def.Sprite, material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            depthVisual.UpdateFacility(node);
            return;
        }

        if (def.TryGetVisualStateSprite(ReciprocatingSpriteState, out Sprite reciprocating))
        {
            int part = Ports(node, x, y, 0, material, origin, rotation);
            Part(node, x, y, part++, def.Sprite, material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            Part(node, x, y, part++, reciprocating, material, origin, rotation,
                config.ReciprocatingLocalPosition, Vector3.one, 3, 1f,
                stroke: config.ReciprocatingStroke);
            return;
        }

        if (def.TryGetVisualStateSprite("rotor", out Sprite rotor))
        {
            int part = Ports(node, x, y, 0, material, origin, rotation);
            if (config.RotorBehindBody)
            {
                Part(node, x, y, part++, rotor, material, origin, rotation,
                    config.RotorLocalPosition, Vector3.one, 1, 1f);
                Part(node, x, y, part++, def.Sprite, material, origin, rotation,
                    Vector3.zero, Vector3.one, 0, 0f);
            }
            else
            {
                Part(node, x, y, part++, def.Sprite, material, origin, rotation,
                    Vector3.zero, Vector3.one, 0, 0f);
                Part(node, x, y, part++, rotor, material, origin, rotation,
                    config.RotorLocalPosition, Vector3.one, 1, 1f);
            }
            return;
        }
        Sprite body = def.Sprite;
        if (node.Definition.Lighting != null) body = MachineLightPresentation.ResolveBodySprite(node, def);
        if (node.Definition.LogicId == "vessel" && Mod_WaterVessel.TryResolvePresentationSprite(node.Snapshot, out Sprite vessel)) body = vessel;
        // 落地设施沿用预览的偏移和缩放，阴影与火焰共同读取这份主体变换。
        int facilityPart = Ports(node, x, y, 0, material, origin, rotation);
        Part(node, x, y, facilityPart++, body, material, origin, rotation, facilityBodyOffset, facilityBodyScale, 0, 0f);
        depthVisual.UpdateFacility(node);
        }
        finally { depthVisual.EndUpdate(); }
    }

    /// <summary>流体覆盖层沿原行网格显示两相余量与实际方向，读数不反写权威库存。</summary>
    private void SubmitFluidNode(MachineEntity node, int x, int y, RuntimeItemDefinition def, Material material, Vector3 origin, Quaternion rotation)
    {
        Sprite body = def.Sprite;
        MachineArticulatedVisual articulation = MachineArticulatedVisual.Resolve(def);
        if (articulation != null)
        {
            SubmitArticulatedMachineParts(node, x, y, def, material, origin, rotation, articulation);
            Ports(node, x, y, 6, material, origin, rotation);
            mechanicalDepthVisuals[new Vector3Int(x, y, node.Definition.Layer)].UpdateFacility(node);
            return;
        }
        bool tankBlock = MachineWorld.IsFluidTankBlock(node);
        int tankMask = tankBlock ? MachineWorld.GetFluidTankConnectionMask(node) : 0;
        FluidMachineState state = MachineWorld.GetFluidState(node);
        FluidInventory inventory = MachineWorld.GetFluidInventory(node);
        bool pipe = node.Definition.IsFluidPipeSegment;
        if (pipe)
        {
            int mask = MachineWorld.GetFluidPipeConnectionMask(node);
            Part(node, x, y, 0, State(def, pipeSpriteStates[0]), material, origin,
                Quaternion.identity, Vector3.zero, Vector3.one, 0, 0, partOrder: 12);
            Sprite arm = FluidPipeConnectionVisual.GetArm(State(def, pipeSpriteStates[1]));
            for (int pipeDirection = 0; pipeDirection < 4; pipeDirection++)
            {
                if ((mask & (1 << pipeDirection)) == 0) continue;
                FluidPipeConnectionVisual.GetArmPose(arm, pipeDirection, out Quaternion turn, out Vector3 offset, out Vector3 scale);
                Part(node, x, y, 8 + pipeDirection, arm, material, origin, turn,
                    Quaternion.Inverse(turn) * offset, scale, 0, 0, partOrder: 0);
            }
            // 可选设备图层覆盖在通用管道中枢上，管臂仍按拓扑自动连接。
            if (def.TryGetVisualStateSprite("pipeOverlay", out Sprite pipeOverlay))
                Part(node, x, y, 12, pipeOverlay, material, origin, Quaternion.identity,
                    Vector3.zero, Vector3.one, 0, 0, partOrder: 13);
            submittedPipeMasks[new Vector3Int(x, y, node.Definition.Layer)] = mask;
        }
        else if (tankBlock)
        {
            submittedTankMasks[new Vector3Int(x, y, node.Definition.Layer)] = tankMask;
            // 罐体始终占满一格，太阳投影仍只提交一次完整外壳。
            MechanicalShadowRegistry.SetPart(this, new Vector3Int(x, y, node.Definition.Layer),
                0, body, origin, Quaternion.identity, Vector3.zero, Fit(body, 1f, 1f), 0, default);
            for (int slice = 0; slice < 9; slice++)
            {
                FluidTankBlockVisual.GetPart(body, slice, tankMask, out Sprite sprite, out Vector3 offset, out Vector3 scale);
                int part = slice == 4 ? 0 : slice < 4 ? slice + 8 : slice + 7;
                Part(node, x, y, part, sprite, material, origin, Quaternion.identity, offset, scale, 0, 0, partOrder: 0);
            }
        }
        else Part(node, x, y, 0, body, material, origin, rotation, Vector3.zero,
            Fit(body, node.Definition.Fluid.ResolveBodySizeCells(node.Definition.Layer),
                node.Definition.Fluid.ResolveBodySizeCells(node.Definition.Layer)), 0, 0);
        MechanicalDepthVisual visual = mechanicalDepthVisuals[new Vector3Int(x, y, node.Definition.Layer)];
        Ports(node, x, y, 6, material, origin, rotation);
        visual.UpdateFacility(node);
        if (fluidIndicatorSprite == null)
            fluidIndicatorSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(.5f, .5f), Texture2D.whiteTexture.width);
        Color gas = new(.4f, .8f, 1, .95f), liquid = new(.9f, .68f, .25f, .95f);
        double gasCapacity = MachineWorld.GetFluidGasCapacityStandardLiters(node);
        float gasAmount = Mathf.Clamp01((float)(FluidUnits.MolToStandardLiters(inventory.GasMoles) / (decimal)gasCapacity));
        float liquidAmount = Mathf.Clamp01((float)(inventory.GetLiquidLiters() / MachineWorld.GetFluidVolumeLiters(node)));
        if (gasAmount > 0)
        { Part(node, x, y, 1, fluidIndicatorSprite, material, origin, Quaternion.identity, new Vector3(0, -.26f), new Vector3(.48f * gasAmount, .06f, 1), 0, 0); visual.SetPartTint(1, gas); }
        if (liquidAmount > 0)
        { Part(node, x, y, 2, fluidIndicatorSprite, material, origin, Quaternion.identity, new Vector3(0, -.34f), new Vector3(.48f * liquidAmount, .06f, 1), 0, 0); visual.SetPartTint(2, liquid); }
        if (state.Ruptured) return;
        int direction = state.LastFlowDirection;
        if (direction < 0 && node.Definition.Fluid.Kind != "pipe" && node.Definition.Fluid.Kind != "tank" && node.Definition.Fluid.Kind != "valve")
            foreach (FluidMachinePortDefinition port in node.Definition.Fluid.Ports)
                if (port.Mode == "output") { direction = (port.Direction + node.RotationQuarterTurns) & 3; break; }
        if (direction < 0) return;
        Quaternion arrow = Quaternion.Euler(0, 0, direction * 90);
        Color flow = state.BlockedPorts.Count > 0 ? new Color(1, .25f, .2f) : state.LastFlowPhase == "liquid" ? liquid : gas;
        Part(node, x, y, 3, fluidIndicatorSprite, material, origin, arrow, new Vector3(.22f, 0), new Vector3(.23f, .035f, 1), 0, 0);
        Part(node, x, y, 4, fluidIndicatorSprite, material, origin, arrow * Quaternion.Euler(0, 0, 45), Quaternion.Euler(0, 0, -45) * new Vector3(.29f, .03f), new Vector3(.1f, .035f, 1), 0, 0);
        Part(node, x, y, 5, fluidIndicatorSprite, material, origin, arrow * Quaternion.Euler(0, 0, -45), Quaternion.Euler(0, 0, 45) * new Vector3(.29f, -.03f), new Vector3(.1f, .035f, 1), 0, 0);
        visual.SetPartTint(3, flow); visual.SetPartTint(4, flow); visual.SetPartTint(5, flow);
    }

    /// <summary>声明了端口贴图的设备按配置叠加轴环或镜像接头。</summary>
    private int Ports(MachineEntity node, int x, int y, int part,
        Material material, Vector3 origin, Quaternion rotation)
    {
        MechanicalAxisPortVisualDefinition visual = node.Definition.AxisPortVisual;
        if (visual == null) return part;
        Sprite port = StandardAxisPortSprite();
        for (int i = 0; i < visual.Count; i++)
        {
            Vector3 scale = visual.IsLeftPort(i) ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            Part(node, x, y, part + i, port, material, origin, rotation,
                visual.GetLocalPosition(i), scale, 0, 0f, drawBelowMechanical: true);
        }
        return part + visual.Count;
    }

    private static Sprite StandardAxisPortSprite()
    {
        if (mechanicalAxisPortSprite != null) return mechanicalAxisPortSprite;
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null ||
            !resources.TryGetItemDefinition(Mod_MechanicalNode.StandardAxisPortItemId, out RuntimeItemDefinition definition) ||
            !definition.TryGetVisualStateSprite(Mod_MechanicalNode.StandardAxisPortState, out mechanicalAxisPortSprite) ||
            mechanicalAxisPortSprite == null)
            throw new InvalidOperationException("机械标准连接杆端口贴图缺失。");
        return mechanicalAxisPortSprite;
    }

    /// <summary>写入 GPU 动画速度和相位，零转速时停在当前角度。</summary>
    private void Part(MachineEntity node, int x, int y, int part, Sprite sprite, Material material,
        Vector3 origin, Quaternion rotation, Vector3 offset, Vector3 scale,
        int mode, float multiplier, float phase = 0f, int track = 0, float stroke = 0f,
        bool drawBelowMechanical = false, Vector4 conveyorSurface = default, int partOrder = -1)
    {
        drawBelowMechanical |= node.Definition.IsConnectionLine;
        // 电机反向只做水平镜像，避免 180 度旋转把支脚和顶部结构倒置。
        if (node.Definition.IsConverter && (node.RotationQuarterTurns & 3) == 2)
        {
            offset.x = -offset.x;
            scale.x = -scale.x;
        }
        float radians = node.VisualRpm * Mathf.PI * 2f / 60f;
        Vector4 animation = new(mode, radians * multiplier,
            (node.VisualPhase - node.VisualTime * radians) * multiplier + phase, stroke);
        if (track == 1)
            animation = new Vector4(mode, node.GearboxLargeSpeed,
                node.GearboxLargePhase - node.GearboxLargeTime * node.GearboxLargeSpeed + phase, 0f);
        else if (track == 2)
            animation = new Vector4(mode, node.GearboxSmallSpeed,
                node.GearboxSmallPhase - node.GearboxSmallTime * node.GearboxSmallSpeed + phase, 0f);
        if (track == 3)
            animation = new Vector4(mode, node.ConveyorVisualSpeed,
                node.ConveyorVisualPhase - node.ConveyorVisualTime * node.ConveyorVisualSpeed, stroke);
        MechanicalShadowRegistry.SetPart(this, new Vector3Int(x, y, node.Definition.Layer),
            part, sprite, origin, rotation, offset, scale, mode, animation);
        mechanicalDepthVisuals[new Vector3Int(x, y, node.Definition.Layer)]
            .SetPart(part, sprite, material, rotation, offset, scale, mode, animation, drawBelowMechanical, conveyorSurface, partOrder);
    }

    /// <summary>每台机械只保留交互与必要灯光；图像通过共享行网格绘制。</summary>
    private void PrepareMechanicalDepthVisual(MachineEntity node, int x, int y, Vector3 origin)
    {
        Vector3Int key = new(x, y, node.Definition.Layer);
        if (!mechanicalDepthVisuals.TryGetValue(key, out MechanicalDepthVisual visual) || visual == null)
        {
            int id = x + y * boundChunk.Terrain.Width + node.Definition.Layer * boundChunk.Terrain.CellCount;
            int visualLayer = node.Definition.VisualLayer;
            if (!TryTakePreparedMechanicalVisual(node, this, origin, id, visualLayer, out visual))
                visual = MechanicalDepthVisual.Create(this, origin, id, visualLayer);
            mechanicalDepthVisuals[key] = visual;
        }
        visual.BindSelectionEntity(node);
        visual.BeginUpdate(origin);
        visual.BindInteraction(MachineWorld.GetOrCreateInteractionTarget(node));
    }

    /// <summary>拆除或类型切换后释放当前格的动态视觉。</summary>
    private void RemoveMechanicalDepthVisual(int x, int y, int occupancy)
    {
        Vector3Int key = new(x, y, occupancy);
        if (!mechanicalDepthVisuals.TryGetValue(key, out MechanicalDepthVisual visual)) return;
        mechanicalDepthVisuals.Remove(key);
        if (visual != null) visual.Dispose();
    }

    /// <summary>区块卸载与资源重建时统一回收全部动态视觉。</summary>
    private void ClearMechanicalDepthVisuals()
    {
        submittedWireMasks.Clear();
        submittedPipeMasks.Clear();
        submittedTankMasks.Clear();
        foreach (MechanicalDepthVisual visual in mechanicalDepthVisuals.Values)
            if (visual != null) visual.Dispose();
        mechanicalDepthVisuals.Clear();
    }

    /// <summary>滚动木芯按旧 Tiled Sprite 的固定世界尺寸缩放。</summary>
    private static Vector3 Fit(Sprite sprite, float width, float height)
        => new(width / sprite.bounds.size.x, height / sprite.bounds.size.y, 1f);

    /// <summary>缺少正式图层时指出资源 ID，避免无声隐藏机械贴图。</summary>
    private static Sprite State(RuntimeItemDefinition def, string state)
    {
        if (!def.TryGetVisualStateSprite(state, out Sprite sprite) || sprite == null)
            throw new InvalidOperationException("机械 Sprite 状态缺失：" + def.Id + "/" + state);
        return sprite;
    }

    /// <summary>同一机械定义的图层参数只解析一次。</summary>
    private static MechanicalVisualConfig Config(RuntimeItemDefinition def)
    {
        if (mechanicalVisualConfigs.TryGetValue(def.Id, out MechanicalVisualConfig config)) return config;
        if (!def.TryGetModuleParameters(Mod_MechanicalNode.ModuleId, out string json))
        {
            config = new MechanicalVisualConfig();
            mechanicalVisualConfigs.Add(def.Id, config);
            return config;
        }
        config = JsonConvert.DeserializeObject<MechanicalVisualConfig>(json) ??
            throw new InvalidOperationException("机械图层参数无效：" + def.Id);
        mechanicalVisualConfigs.Add(def.Id, config);
        return config;
    }
    #endregion
}
