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

    /// <summary>机械物品定义中的图层坐标，新增 MOD 机械可沿用同一参数契约。</summary>
    private sealed class MechanicalVisualConfig
    {
        public float InputShaftOffsetX = -.45f;
        public string AxisPortLayout;
        public float AxisPortOffset = .45f;
        public float AxisPortOffsetY;
        public bool AxisPortDrawOnTop;
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
    private readonly Dictionary<Vector3Int, int> submittedWireMasks = new(); // 相邻事件只重提连接发生变化的电线。
    internal HashSet<Vector3Int> MechanicalShadowKeys => submittedMechanicalCells; // 供阴影注册表按区块卸载。
    private static Material mechanicalFallbackMaterial;

    /// <summary>新资源会话重新解析 MOD 多图层参数。</summary>
    private static void ResetMechanicalVisualCache()
    {
        mechanicalVisualConfigs.Clear();
        mechanicalFallbackMaterial = null;
    }

    /// <summary>基础地形建立后接入机械数据变化和表现重建通知。</summary>
    private void BindMechanicalPresentation()
    {
        MachineWorld.CellChanged += HandleMechanicalCellChanged;
        BatchPresentationRebuilt += RefreshMechanicalPresentation;
    }

    /// <summary>区块卸载时解除事件，回收行网格部件、交互和阴影注册。</summary>
    private void UnbindMechanicalPresentation()
    {
        MachineWorld.CellChanged -= HandleMechanicalCellChanged;
        BatchPresentationRebuilt -= RefreshMechanicalPresentation;
        mechanicalVisualNodes.Clear();
        ClearMechanicalDepthVisuals();
        MechanicalShadowRegistry.RemoveOwner(this);
        submittedMechanicalCells.Clear();
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
        var origin = boundChunk.Address.ChunkOrigin;
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(
            new Vector2(origin.X, origin.Y), new Vector2(cell.x, cell.y));
        int x = Mathf.RoundToInt(displacement.x), y = Mathf.RoundToInt(displacement.y);
        if ((uint)x >= (uint)boundChunk.Terrain.Width || (uint)y >= (uint)boundChunk.Terrain.Height) return;
        for (int occupancy = 0; occupancy <= 3; occupancy++)
        {
            MachineEntity node = MachineWorld.GetAt(cell, occupancy);
            if (node != null) SubmitMechanicalNode(node, x, y);
            else
            {
                MechanicalShadowRegistry.Remove(this, new Vector3Int(x, y, occupancy));
                submittedMechanicalCells.Remove(new Vector3Int(x, y, occupancy));
                submittedWireMasks.Remove(new Vector3Int(x, y, occupancy));
                RemoveMechanicalDepthVisual(x, y, occupancy);
            }
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

    /// <summary>按机械类型组合 Sprite，视觉状态来自物品定义而不是运行时 Item。</summary>
    private void SubmitMechanicalNode(MachineEntity node, int x, int y)
    {
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
        Quaternion rotation = Quaternion.Euler(0f, 0f, node.RotationQuarterTurns * 90f);
        Vector3 facilityBodyOffset = node.Definition.Ports == "none"
            ? (def.Visual?.RendererLocalPosition ?? Vector3.zero)
            : Vector3.zero;
        string kind = node.Definition.Kind;
        Vector3Int key = new(x, y, node.Definition.Layer);
        submittedMechanicalCells.Add(key);
        if (node.Definition.ShouldCastVisualShadows())
            MechanicalShadowRegistry.BeginNode(this, key, gameObject.scene, def.Sprite,
                origin + rotation * facilityBodyOffset, rotation, def.Visual?.Shadows);
        else MechanicalShadowRegistry.Remove(this, key);
        PrepareMechanicalDepthVisual(node, x, y, origin);
        MechanicalDepthVisual depthVisual = mechanicalDepthVisuals[key];
        try
        {

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
                Fit(core, 116f / 128f, 28f / 128f), 2, 1f);
            Part(node, x, y, 3, State(def, "shaftEndRings"), material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            return;
        }
        if (kind == "gear")
        {
            Part(node, x, y, 0, State(def, "inputShaft"), material, origin, rotation,
                new Vector3(config.InputShaftOffsetX, 0f), Vector3.one, 0, 0f);
            float sign = ((node.Cell.x + node.Cell.y) & 1) == 0 ? 1f : -1f;
            Part(node, x, y, 1, def.Sprite, material, origin, rotation, Vector3.zero,
                Vector3.one, 1, sign, sign < 0f ? Mathf.PI / 8f : 0f);
            return;
        }
        if (kind == "bellows")
        {
            Part(node, x, y, 0, State(def, "inputShaft"), material, origin, rotation,
                config.InputShaftLocalPosition, Vector3.one, 0, 0f);
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
                new Vector3(-44f / 128f, 0f), scale, 2, 1f);
            Part(node, x, y, 1, core, material, origin, rotation,
                new Vector3(44f / 128f, 0f), scale, 2, 1f);
            Part(node, x, y, 2, def.Sprite, material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            Part(node, x, y, 3, State(def, "shaftEndRings"), material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            return;
        }

        if (kind == "gearbox")
        {
            bool frontPorts = config.AxisPortDrawOnTop;
            if (!frontPorts) Ports(node, x, y, 0, def, material, origin, rotation, config);
            Part(node, x, y, frontPorts ? 0 : 1, def.Sprite, material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            Sprite gear = State(def, "gearboxGear");
            Part(node, x, y, frontPorts ? 1 : 2, gear, material, origin, rotation,
                config.GearboxLargeGearLocalPosition, Vector3.one * config.GearboxLargeGearScale, 1, 1f,
                track: 1);
            Part(node, x, y, frontPorts ? 2 : 3, gear, material, origin, rotation,
                config.GearboxSmallGearLocalPosition, Vector3.one * config.GearboxSmallGearScale,
                1, 1f, Mathf.PI / 8f, track: 2);
            if (frontPorts) Ports(node, x, y, 3, def, material, origin, rotation, config);
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

        if (def.TryGetVisualStateSprite(ReciprocatingSpriteState, out Sprite reciprocating))
        {
            Part(node, x, y, 0, def.Sprite, material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            int movingPart = config.AxisPortDrawOnTop ? 1 : 3;
            Part(node, x, y, movingPart, reciprocating, material, origin, rotation,
                config.ReciprocatingLocalPosition, Vector3.one, 3, 1f,
                stroke: config.ReciprocatingStroke);
            Ports(node, x, y, config.AxisPortDrawOnTop ? 3 : 1,
                def, material, origin, rotation, config);
            return;
        }

        if (def.TryGetVisualStateSprite("rotor", out Sprite rotor))
        {
            if (config.RotorBehindBody)
            {
                Part(node, x, y, 0, rotor, material, origin, rotation,
                    config.RotorLocalPosition, Vector3.one, 1, -1f);
                Part(node, x, y, 1, def.Sprite, material, origin, rotation,
                    Vector3.zero, Vector3.one, 0, 0f);
                return;
            }
            Part(node, x, y, 0, def.Sprite, material, origin, rotation,
                Vector3.zero, Vector3.one, 0, 0f);
            int rotorPart = config.AxisPortDrawOnTop ? 1 : 3;
            Part(node, x, y, rotorPart, rotor, material, origin, rotation,
                config.RotorLocalPosition, Vector3.one, 1, -1f);
            if (def.TryGetVisualStateSprite("axisPorts", out Sprite port))
                Part(node, x, y, config.AxisPortDrawOnTop ? 3 : 1, port, material, origin, rotation,
                    new Vector3(0f, config.AxisPortOffsetY), Vector3.one, 0, 0f);
            return;
        }
        Sprite body = def.Sprite;
        if (node.Definition.LogicId == "vessel" && Mod_WaterVessel.TryResolvePresentationSprite(node.Snapshot, out Sprite vessel)) body = vessel;
        // 设施本体沿用预览的图片局部偏移，让 Sprite Pivot 落在同一建造锚点上。
        Part(node, x, y, 0, body, material, origin, rotation, facilityBodyOffset, Vector3.one, 0, 0f);
        depthVisual.UpdateFacility(node);
        Ports(node, x, y, 3, def, material, origin, rotation, config);
        }
        finally { depthVisual.EndUpdate(); }
    }

    /// <summary>声明了端口贴图的设备按配置叠加轴环或镜像接头。</summary>
    private void Ports(MachineEntity node, int x, int y, int part, RuntimeItemDefinition def,
        Material material, Vector3 origin, Quaternion rotation, MechanicalVisualConfig config)
    {
        if (!def.TryGetVisualStateSprite("axisPorts", out Sprite port)) return;
        if (config.AxisPortLayout == "centeredShaftRings")
            Part(node, x, y, part, port, material, origin, rotation,
                new Vector3(0f, config.AxisPortOffsetY), Vector3.one, 0, 0f);
        else if (config.AxisPortLayout == "mirroredSingle")
        {
            Part(node, x, y, 1, port, material, origin, rotation,
                new Vector3(-config.AxisPortOffset, config.AxisPortOffsetY), Vector3.one, 0, 0f);
            Part(node, x, y, 2, port, material, origin, rotation,
                new Vector3(config.AxisPortOffset, config.AxisPortOffsetY), Vector3.one, 0, 0f);
        }
        else throw new InvalidOperationException("机械端口图层布局无效：" + node.Definition.Id);
    }

    /// <summary>写入 GPU 动画速度和相位，零转速时停在当前角度。</summary>
    private void Part(MachineEntity node, int x, int y, int part, Sprite sprite, Material material,
        Vector3 origin, Quaternion rotation, Vector3 offset, Vector3 scale,
        int mode, float multiplier, float phase = 0f, int track = 0, float stroke = 0f)
    {
        float radians = node.VisualRpm * Mathf.PI * 2f / 60f;
        Vector4 animation = new(mode, radians * multiplier,
            (node.VisualPhase - node.VisualTime * radians) * multiplier + phase, stroke);
        if (track == 1)
            animation = new Vector4(mode, node.GearboxLargeSpeed,
                node.GearboxLargePhase - node.GearboxLargeTime * node.GearboxLargeSpeed + phase, 0f);
        else if (track == 2)
            animation = new Vector4(mode, node.GearboxSmallSpeed,
                node.GearboxSmallPhase - node.GearboxSmallTime * node.GearboxSmallSpeed + phase, 0f);
        MechanicalShadowRegistry.SetPart(this, new Vector3Int(x, y, node.Definition.Layer),
            part, sprite, origin, rotation, offset, scale, mode, animation);
        mechanicalDepthVisuals[new Vector3Int(x, y, node.Definition.Layer)]
            .SetPart(part, sprite, material, rotation, offset, scale, mode, animation);
    }

    /// <summary>每台机械只保留交互与必要灯光；图像通过共享行网格绘制。</summary>
    private void PrepareMechanicalDepthVisual(MachineEntity node, int x, int y, Vector3 origin)
    {
        Vector3Int key = new(x, y, node.Definition.Layer);
        if (!mechanicalDepthVisuals.TryGetValue(key, out MechanicalDepthVisual visual) || visual == null)
        {
            int id = x + y * boundChunk.Terrain.Width + node.Definition.Layer * boundChunk.Terrain.CellCount;
            int visualLayer = node.Definition.Electrical?.IsWire == true ? -1 : node.Definition.Layer;
            visual = MechanicalDepthVisual.Create(this, origin, id, visualLayer);
            mechanicalDepthVisuals[key] = visual;
        }
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
