using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>机械节点的分层表现；贴地件由 BRG 批绘，高大建筑用排序代理与玩家比较 Y 轴。</summary>
public sealed partial class ChunkTilemapRenderer
{
    #region 机械表现
    private const string ClutchEngagedSpriteState = "clutchEngaged"; // 离合器接合状态的主贴图键。
    private const string ClutchDisengagedSpriteState = "clutchDisengaged"; // 离合器断开状态的主贴图键。
    private const string ReciprocatingSpriteState = "reciprocating"; // 往复运动的独立图层键。
    private const string BellowsLeatherSpriteState = "bellowsLeather"; // 风箱皮革折页的独立图层键。
    private const int BellowsCompressionMode = 4; // 按机械相位沿局部纵轴压缩皮革折页。
    private const float MechanicalPartSortStep = 1f / 4096f; // 同一 Y 锚点内只用于稳定机械子图层，不跨相邻格倒序。

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
    private readonly List<MechanicalWorld.MechanicalRenderCell> mechanicalVisualNodes = new();
    private readonly Dictionary<Vector3Int, MechanicalDynamicVisual> dynamicMechanicalVisuals = new(); // 高大机械的轻量排序代理。
    private readonly HashSet<Vector3Int> submittedMechanicalCells = new(); // 资源重建时清除旧 BRG 子层。
    internal HashSet<Vector3Int> MechanicalShadowKeys => submittedMechanicalCells; // 供阴影注册表按区块卸载。
    private static Material mechanicalBatchMaterial;

    /// <summary>新资源会话重新解析 MOD 多图层参数。</summary>
    private static void ResetMechanicalVisualCache()
    {
        mechanicalVisualConfigs.Clear();
        mechanicalBatchMaterial = null;
    }

    /// <summary>基础 BRG Owner 建立后接入机械数据变化和重建通知。</summary>
    private void BindMechanicalPresentation()
    {
        MechanicalWorld.CellChanged += HandleMechanicalCellChanged;
        BatchPresentationRebuilt += RefreshMechanicalPresentation;
    }

    /// <summary>区块卸载时解除事件，BRG Owner 统一清除全部机械实例。</summary>
    private void UnbindMechanicalPresentation()
    {
        MechanicalWorld.CellChanged -= HandleMechanicalCellChanged;
        BatchPresentationRebuilt -= RefreshMechanicalPresentation;
        mechanicalVisualNodes.Clear();
        ClearDynamicMechanicalVisuals();
        MechanicalShadowRegistry.RemoveOwner(this);
        submittedMechanicalCells.Clear();
    }

    /// <summary>从权威机械数据重建当前区块所有图层。</summary>
    private void RefreshMechanicalPresentation()
    {
        if (boundChunk?.Terrain == null || !batchPresentationComplete) return;
        ClearDynamicMechanicalVisuals();
        foreach (Vector3Int key in submittedMechanicalCells)
        {
            MechanicalShadowRegistry.Remove(this, key);
            for (int part = 0; part < 4; part++)
                ClearLayerVisual(Layer(key.z, part), key.x, key.y);
        }
        submittedMechanicalCells.Clear();
        var origin = boundChunk.Address.ChunkOrigin;
        MechanicalWorld.CollectInBounds(new BoundsInt(origin.X, origin.Y, 0,
            boundChunk.Terrain.Width, boundChunk.Terrain.Height, 1), mechanicalVisualNodes);
        foreach (MechanicalWorld.MechanicalRenderCell entry in mechanicalVisualNodes)
            SubmitMechanicalNode(entry.Node, entry.DisplayCell.x - origin.X,
                entry.DisplayCell.y - origin.Y);
    }

    /// <summary>局部放置、拆除或转速变化只刷新对应格的八个实例槽。</summary>
    private void HandleMechanicalCellChanged(Vector2Int cell)
    {
        if (boundChunk?.Terrain == null || !batchPresentationComplete) return;
        var origin = boundChunk.Address.ChunkOrigin;
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(
            new Vector2(origin.X, origin.Y), new Vector2(cell.x, cell.y));
        int x = Mathf.RoundToInt(displacement.x), y = Mathf.RoundToInt(displacement.y);
        if ((uint)x >= (uint)boundChunk.Terrain.Width || (uint)y >= (uint)boundChunk.Terrain.Height) return;
        for (int occupancy = 0; occupancy < 2; occupancy++)
        {
            for (int part = 0; part < 4; part++)
                ClearLayerVisual(Layer(occupancy, part), x, y);
            MechanicalNode node = MechanicalWorld.GetAt(cell, occupancy);
            if (node != null) SubmitMechanicalNode(node, x, y);
            else
            {
                MechanicalShadowRegistry.Remove(this, new Vector3Int(x, y, occupancy));
                submittedMechanicalCells.Remove(new Vector3Int(x, y, occupancy));
                RemoveDynamicMechanicalVisual(x, y, occupancy);
            }
        }
    }

    /// <summary>两个机械占地层分别提供主体、两个运动层和前景层。</summary>
    private static ChunkBatchRendererGroupService.VisualLayer Layer(int occupancy, int part)
        => (ChunkBatchRendererGroupService.VisualLayer)
            ((int)ChunkBatchRendererGroupService.VisualLayer.MechanicalLowerBase + occupancy * 4 + part);

    /// <summary>按机械类型组合 Sprite，视觉状态来自物品定义而不是运行时 Item。</summary>
    private void SubmitMechanicalNode(MechanicalNode node, int x, int y)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(node.Definition.Id, out RuntimeItemDefinition def) ||
            def.Sprite == null) throw new InvalidOperationException("机械本体 Sprite 缺失：" + node.Definition.Id);
        mechanicalBatchMaterial ??= Resources.Load<Material>("Config/WorldModel/BRG/ChunkBRG-Sprite-Lit");
        if (mechanicalBatchMaterial == null) throw new InvalidOperationException("机械 BRG 材质缺失。");
        Material material = def.Material ?? mechanicalBatchMaterial;
        MechanicalVisualConfig config = Config(def);
        // 循环世界的显示区块可能使用非规范坐标；实例位置必须跟当前区块格一致。
        var chunkOrigin = boundChunk.Address.ChunkOrigin;
        Vector3 origin = new(chunkOrigin.X + x + .5f, chunkOrigin.Y + y + .5f,
            node.Snapshot.transform.position.z);
        Quaternion rotation = Quaternion.Euler(0f, 0f, node.RotationQuarterTurns * 90f);
        string kind = node.Definition.Kind;
        Vector3Int key = new(x, y, node.Definition.Layer);
        submittedMechanicalCells.Add(key);
        if (node.Definition.ShouldCastVisualShadows())
            MechanicalShadowRegistry.BeginNode(this, key, gameObject.scene, def.Sprite,
                origin, rotation, def.Visual?.Shadows);
        else MechanicalShadowRegistry.Remove(this, key);
        PrepareDynamicMechanicalVisual(node, x, y, origin);

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
            if (node.Definition.RenderSorting != "dynamicY")
                throw new InvalidOperationException("往复机械图层需要动态 Y 排序：" + node.Definition.Id);
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
        Part(node, x, y, 0, def.Sprite, material, origin, rotation,
            Vector3.zero, Vector3.one, 0, 0f);
        Ports(node, x, y, 3, def, material, origin, rotation, config);
    }

    /// <summary>声明了端口贴图的设备按配置叠加轴环或镜像接头。</summary>
    private void Ports(MechanicalNode node, int x, int y, int part, RuntimeItemDefinition def,
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
    private void Part(MechanicalNode node, int x, int y, int part, Sprite sprite, Material material,
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
        Vector4 region = Vector4.zero;
        if (mode == 2)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (Vector2 uv in sprite.uv) { min = Mathf.Min(min, uv.x); max = Mathf.Max(max, uv.x); }
            region = new Vector4(min, max, .5f / sprite.texture.width, 0f);
        }
        if (node.Definition.RenderSorting == "dynamicY")
        {
            dynamicMechanicalVisuals[new Vector3Int(x, y, node.Definition.Layer)]
                .SetPart(part, sprite, material, rotation, offset, scale, mode, animation);
            return;
        }
        SetLayerVisual(Layer(node.Definition.Layer, part), x, y, sprite, material,
            Matrix4x4.TRS(origin + rotation * offset, rotation, scale),
            Color.white, animation, region, MechanicalSortPosition(node, origin, part));
    }

    /// <summary>整台机械共用建造锚点参与 Y 排序，内部占地层与子图层仅施加极小前景偏移。</summary>
    private static Vector3 MechanicalSortPosition(MechanicalNode node, Vector3 origin, int part)
    {
        int visualDepth = node.Definition.Layer * 4 + part;
        return new Vector3(origin.x, origin.y - visualDepth * MechanicalPartSortStep, origin.z);
    }

    /// <summary>高大机械创建动态 Y 排序代理；贴地传动件仅保留 BRG 绘制。</summary>
    private void PrepareDynamicMechanicalVisual(MechanicalNode node, int x, int y, Vector3 origin)
    {
        Vector3Int key = new(x, y, node.Definition.Layer);
        if (node.Definition.RenderSorting != "dynamicY")
        {
            RemoveDynamicMechanicalVisual(x, y, node.Definition.Layer);
            return;
        }
        if (!dynamicMechanicalVisuals.TryGetValue(key, out MechanicalDynamicVisual visual) || visual == null)
        {
            visual = MechanicalDynamicVisual.Create(transform, origin);
            dynamicMechanicalVisuals[key] = visual;
        }
        visual.BeginUpdate(origin);
    }

    /// <summary>拆除或类型切换后释放当前格的动态视觉。</summary>
    private void RemoveDynamicMechanicalVisual(int x, int y, int occupancy)
    {
        Vector3Int key = new(x, y, occupancy);
        if (!dynamicMechanicalVisuals.TryGetValue(key, out MechanicalDynamicVisual visual)) return;
        dynamicMechanicalVisuals.Remove(key);
        if (visual != null) visual.Dispose();
    }

    /// <summary>区块卸载与资源重建时统一回收全部动态视觉。</summary>
    private void ClearDynamicMechanicalVisuals()
    {
        foreach (MechanicalDynamicVisual visual in dynamicMechanicalVisuals.Values)
            if (visual != null) visual.Dispose();
        dynamicMechanicalVisuals.Clear();
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
            throw new InvalidOperationException("机械图层参数缺失：" + def.Id);
        config = JsonConvert.DeserializeObject<MechanicalVisualConfig>(json) ??
            throw new InvalidOperationException("机械图层参数无效：" + def.Id);
        mechanicalVisualConfigs.Add(def.Id, config);
        return config;
    }
    #endregion
}
