using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable, MemoryPackable]
public sealed partial class PressureExplosionMaterial
{
    #region 释放快照
    public string FluidId;
    public decimal GasMoles, LiquidMoles;
    public double InternalEnergyJoules;
    [MemoryPackIgnore] public FluidBatch Batch => new(FluidId, GasMoles, LiquidMoles, InternalEnergyJoules);
    #endregion
}

[Serializable, MemoryPackable]
public sealed partial class PressureExplosionEvent
{
    #region 唯一事件
    public ulong Id;
    public string WorldKey, SourceId;
    public string SpatialContextJson, PresentationWorldKey;
    public Vector2 Center;
    public double Intensity;
    public double PressureEnergyJoules, ChemicalEnergyJoules, ProductHeatingJoules;
    public List<PressureExplosionMaterial> Materials = new();
    #endregion
}

[Serializable, MemoryPackable]
public sealed partial class PressureLiquidSpillState
{
    #region 尚未被地表接纳的真实液体
    public string WorldKey, FluidId;
    public Vector2 Center;
    public decimal LiquidMoles;
    public double InternalEnergyJoules;
    public int NextCellIndex;
    public string DetachedWorldKey;
    public Vector2 DetachedCenter;
    #endregion
}

public partial class MachineArchive
{
    #region 压力爆炸存档
    public ulong NextPressureExplosionId;
    public List<PressureExplosionEvent> PressureExplosions = new();
    public List<string> RecentPressureExplosionSources = new();
    public List<PressureLiquidSpillState> PendingPressureLiquidSpills = new();
    #endregion
}

/// <summary>破裂先提交唯一事件；每个事件的物料、范围伤害与连锁只结算一次。</summary>
public static class PressureExplosionQueue
{
    #region 事件入队与有限曲线
    public static event Action<PressureExplosionEvent, float> Presented;
    public static Func<PressureExplosionEvent, float, float, bool> WorldHandler;
    public static Action<PressureExplosionEvent> SnapshotHandler;
    public static Func<PressureExplosionEvent, float, bool> PresentationHandler;
    public const double HydrogenOxygenEnergyJoulesPerOxygenMol = 483600d;
    private const int EventsPerTick = 32;
    private static int budgetFrame, handledThisFrame;
    private const int SpillCellChecksPerFrame = 64, MaximumSpillCells = 4096;
    private static int spillBudgetFrame, spillCellsThisFrame;
    public static bool HasFrameBudget => budgetFrame != Time.frameCount || handledThisFrame < EventsPerTick;
    public static float Radius(double intensity) => intensity <= 0d ? 0f : (float)Math.Min(
        MachineCatalog.Settings.MaximumPressureExplosionRadius, Math.Sqrt(intensity) * MachineCatalog.Settings.PressureExplosionRadiusScale);
    public static float CenterDamage(double intensity) => intensity <= 0d ? 0f : (float)Math.Min(
        MachineCatalog.Settings.MaximumPressureExplosionDamage, Math.Sqrt(intensity) * MachineCatalog.Settings.PressureExplosionDamageScale);
    private static MachineArchive Archive => SaveDataMgr.Instance?.SaveData?.Mechanical;
    public static void Enqueue(string world, Vector2 center, double intensity, string sourceId,
        List<FluidBatch> gas, List<FluidBatch> liquid)
    {
        if (!GameNetwork.HasStateAuthority) return;
        if (string.IsNullOrWhiteSpace(world) || string.IsNullOrWhiteSpace(sourceId) || !FluidUnits.IsFinite(intensity) || intensity < 0d ||
            !float.IsFinite(center.x) || !float.IsFinite(center.y)) throw new ArgumentException("压力爆炸快照无效。");
        MachineArchive archive = Archive ?? throw new InvalidOperationException("气罐破裂缺少权威存档队列。");
        if (archive.RecentPressureExplosionSources.Contains(sourceId)) return;
        foreach (var pending in archive.PressureExplosions) if (pending.SourceId == sourceId) return;
        if (++archive.NextPressureExplosionId == 0) ++archive.NextPressureExplosionId;
        var entry = new PressureExplosionEvent { Id = archive.NextPressureExplosionId, WorldKey = world,
            PresentationWorldKey = world, PressureEnergyJoules = intensity,
            Center = world.StartsWith("ship:", StringComparison.Ordinal) ? center : NormalizeInWorld(world, center), Intensity = intensity, SourceId = sourceId };
        AddMaterials(entry, gas); AddMaterials(entry, liquid);
        // 只燃烧已经从真实库存移交的批次，共用储罐不能按罐体数量复制燃料。
        FreezeHydrogenOxygenReaction(entry);
        SnapshotHandler?.Invoke(entry);
        archive.PressureExplosions.Add(entry);
    }
    private static void FreezeHydrogenOxygenReaction(PressureExplosionEvent entry)
    {
        decimal hydrogen = 0m, oxygen = 0m;
        foreach (PressureExplosionMaterial material in entry.Materials)
        {
            if (FluidInventory.Same(material.FluidId, FluidIds.Hydrogen)) hydrogen += material.GasMoles;
            if (FluidInventory.Same(material.FluidId, FluidIds.Oxygen)) oxygen += material.GasMoles;
        }
        decimal extent = AtmosphereService.Quantize(Math.Min(hydrogen / 2m, oxygen));
        if (extent <= 0m) return;
        double inputEnergy = ConsumeGas(entry, FluidIds.Hydrogen, extent * 2m) + ConsumeGas(entry, FluidIds.Oxygen, extent);
        double capacity = (double)extent * (2d * FluidCatalog.Default.Find(FluidIds.Hydrogen).GasHeatCapacityJPerMolKelvin +
            FluidCatalog.Default.Find(FluidIds.Oxygen).GasHeatCapacityJPerMolKelvin);
        double temperature = Math.Max(1d, inputEnergy / Math.Max(1e-12d, capacity));
        FluidDefinition water = FluidCatalog.Default.Find(FluidIds.PureWater);
        double productEnergy = water.EnergyAt(extent * 2m, 0m, temperature);
        entry.ChemicalEnergyJoules = (double)extent * HydrogenOxygenEnergyJoulesPerOxygenMol;
        entry.ProductHeatingJoules = Math.Min(entry.ChemicalEnergyJoules, Math.Max(0d, productEnergy - inputEnergy));
        entry.Materials.RemoveAll(material => material.GasMoles <= 0m && material.LiquidMoles <= 0m);
        entry.Materials.Add(new PressureExplosionMaterial { FluidId = FluidIds.PureWater, GasMoles = extent * 2m,
            InternalEnergyJoules = inputEnergy + entry.ProductHeatingJoules });
        entry.Intensity = entry.PressureEnergyJoules + entry.ChemicalEnergyJoules - entry.ProductHeatingJoules;
    }
    private static double ConsumeGas(PressureExplosionEvent entry, string fluidId, decimal requested)
    {
        double energy = 0d;
        foreach (PressureExplosionMaterial material in entry.Materials)
        {
            if (!FluidInventory.Same(material.FluidId, fluidId) || material.GasMoles <= 0m) continue;
            decimal amount = Math.Min(requested, material.GasMoles);
            FluidDefinition definition = FluidCatalog.Default.Find(fluidId);
            double capacity = (double)material.GasMoles * definition.GasHeatCapacityJPerMolKelvin +
                (double)material.LiquidMoles * definition.LiquidHeatCapacityJPerMolKelvin;
            double temperature = Math.Max(1d, (material.InternalEnergyJoules - (double)material.GasMoles *
                definition.GasEnergyOffsetJPerMol) / Math.Max(1e-12d, capacity));
            double removed = Math.Min(material.InternalEnergyJoules, definition.EnergyAt(amount, 0m, temperature));
            material.GasMoles -= amount; material.InternalEnergyJoules = Math.Max(0d, material.InternalEnergyJoules - removed);
            requested -= amount; energy += removed;
            if (requested <= 0m) break;
        }
        if (requested > 0m) throw new InvalidOperationException("爆炸反应超过已移交的燃料库存。");
        return energy;
    }
    public static Vector2 NormalizeInWorld(string world, Vector2 position)
        => TryGetTopology(world, out WorldTopologyBounds bounds) ? bounds.NormalizePosition(position) : position;
    public static Vector2 DeltaInWorld(string world, Vector2 from, Vector2 to)
        => TryGetTopology(world, out WorldTopologyBounds bounds) ? bounds.ShortestDelta(from, to) : to - from;
    private static bool TryGetTopology(string world, out WorldTopologyBounds bounds)
    {
        bounds = default;
        var planets = SaveDataMgr.Instance?.SaveData?.PlanetData_Dict;
        return planets != null && !string.IsNullOrWhiteSpace(world) &&
            planets.TryGetValue(WorldAddress.FromWorldKey(world).PlanetId, out PlanetData planet) && WorldTopologyBounds.TryCreate(planet, out bounds);
    }
    private static void AddMaterials(PressureExplosionEvent entry, List<FluidBatch> batches)
    {
        if (batches == null) return;
        foreach (FluidBatch batch in batches)
            if (!batch.IsEmpty) entry.Materials.Add(new PressureExplosionMaterial { FluidId = batch.FluidId,
                GasMoles = batch.GasMoles, LiquidMoles = batch.LiquidMoles, InternalEnergyJoules = batch.InternalEnergyJoules });
    }
    #endregion

    #region 权威结算与连锁
    public static void Tick(float seconds)
    {
        if (!GameNetwork.HasStateAuthority || Archive is not MachineArchive archive) return;
        string world = MachineWorld.WorldKey;
        if (budgetFrame != Time.frameCount) { budgetFrame = Time.frameCount; handledThisFrame = 0; }
        for (; handledThisFrame < EventsPerTick; handledThisFrame++)
        {
            int index = archive.PressureExplosions.FindIndex(value => value.WorldKey == world);
            if (index < 0) break;
            PressureExplosionEvent entry = archive.PressureExplosions[index];
            archive.PressureExplosions.RemoveAt(index);
            archive.RecentPressureExplosionSources.Add(entry.SourceId);
            if (archive.RecentPressureExplosionSources.Count > 4096) archive.RecentPressureExplosionSources.RemoveAt(0);
            float radius = Radius(entry.Intensity), peak = CenterDamage(entry.Intensity);
            bool handled = WorldHandler?.Invoke(entry, radius, peak) == true;
            if (!handled) ReleaseMaterials(entry, entry.WorldKey, entry.Center);
            if (!handled && radius > 0f && peak > 0f)
            {
                bool surface = WorldAddress.FromWorldKey(entry.WorldKey).IsSurface &&
                    SaveDataMgr.Instance.SaveData.PlanetData_Dict.ContainsKey(entry.WorldKey);
                var targets = ItemMgr.Instance != null ? new List<Item>(ItemMgr.Instance.WorldRunTimeItems.Values) : new();
                foreach (Item target in targets)
                {
                    if (target == null || target.Owner != null || target.InHand || target.gameObject.scene.name != entry.WorldKey) continue;
                    if (MachineWorld.OwnsWorldItem(target.itemData) || surface &&
                        Mod_Building.TryReadBuildingData(target.itemData, out _, out var building) && building.Role == BuildingRole.PlacedBuilding) continue;
                    Vector2 position = SceneManager.GetActiveScene().name == entry.WorldKey
                        ? WorldLocalPresentation.ToLogical(target.transform.position) : (Vector2)target.itemData.transform.position;
                    float distance = DeltaInWorld(entry.WorldKey, entry.Center, position).magnitude;
                    if (distance > radius) continue;
                    target.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.HurtPressureExplosion(entry.Id, entry.Center, peak * (1f - distance / radius));
                }
                if (surface)
                    FlatWorld.Spaceflight.SpaceSurfaceQuery.ApplyStructureDamageInRadius(entry.WorldKey, entry.Center, radius, peak, true, entry.Id);
                else MachineWorld.ApplyPressureExplosion(entry.WorldKey, entry.Center, radius, peak);
                if (SceneManager.GetActiveScene().name == entry.WorldKey)
                    FlatWorld.NaturalEntities.NaturalEntityEcsService.ApplyPressureExplosion(entry.WorldKey, entry.Id, entry.Center, radius, peak);
            }
            Present(entry);
        }
        if (!archive.PressureExplosions.Exists(value => value.WorldKey == world)) MachineWorld.CompleteFluidRuptures();
        AdvanceLiquidSpills(world);
    }
    public static void ReleaseMaterials(PressureExplosionEvent entry, string world, Vector2 center)
    {
        AtmosphereService.TryGetForWorld(world, out AtmosphereState atmosphere);
        foreach (PressureExplosionMaterial material in entry.Materials)
        {
            FluidBatch batch = material.Batch;
            FluidDefinition definition = FluidCatalog.Default.Find(batch.FluidId);
            if (batch.GasMoles > 0m && atmosphere != null)
                AtmosphereService.Emit(atmosphere, new FluidBatch(batch.FluidId, batch.GasMoles, 0m, 0d));
            if (batch.LiquidMoles <= 0m || string.IsNullOrWhiteSpace(definition.LiquidId)) continue;
            if (GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetLiquidDefinition(definition.LiquidId, out LiquidDefinition liquid) || liquid.WorldWater == null) continue;
            double capacity = (double)batch.GasMoles * definition.GasHeatCapacityJPerMolKelvin +
                (double)batch.LiquidMoles * definition.LiquidHeatCapacityJPerMolKelvin;
            double temperature = (batch.InternalEnergyJoules - (double)batch.GasMoles * definition.GasEnergyOffsetJPerMol) / Math.Max(1e-12d, capacity);
            Archive.PendingPressureLiquidSpills.Add(new PressureLiquidSpillState
            {
                WorldKey = world, FluidId = batch.FluidId, Center = NormalizeInWorld(world, center),
                LiquidMoles = batch.LiquidMoles, InternalEnergyJoules = definition.EnergyAt(0m, batch.LiquidMoles, Math.Max(1d, temperature))
            });
        }
    }
    public static bool CanAdvanceLiquidSpill(PressureLiquidSpillState spill)
        => spill != null && spill.LiquidMoles > 0m && spill.NextCellIndex < MaximumSpillCells;
    private static void AdvanceLiquidSpills(string world)
    {
        MachineArchive archive = Archive;
        if (archive == null) return;
        if (spillBudgetFrame != Time.frameCount) { spillBudgetFrame = Time.frameCount; spillCellsThisFrame = 0; }
        foreach (PressureLiquidSpillState spill in archive.PendingPressureLiquidSpills)
        {
            if (!CanAdvanceLiquidSpill(spill)) continue;
            var spaceSession = FlatWorld.Spaceflight.SpaceSession.Current;
            // 只有已加载的权威船列表确认船已删除，才允许备用世界接过旧船溢液。
            if (spill.WorldKey.StartsWith("ship:", StringComparison.Ordinal) && spaceSession?.State != null &&
                spaceSession.GetShip(spill.WorldKey.Substring(5)) == null && !string.IsNullOrWhiteSpace(spill.DetachedWorldKey) &&
                (spill.WorldKey == world || spill.DetachedWorldKey == world))
            { spill.WorldKey = spill.DetachedWorldKey; spill.Center = spill.DetachedCenter; }
            if (spill.WorldKey != world) continue;
            // 航行中的船先保留液体余量，落地后再投影到真实地表，不空跑溢流格预算。
            if (spill.WorldKey.StartsWith("ship:", StringComparison.Ordinal))
            {
                string shipId = spill.WorldKey.Substring(5);
                if (spaceSession?.State == null || spaceSession.GetShip(shipId) == null) continue;
                using var shipScope = MachineWorld.UseScope(spill.WorldKey);
                if (!MachineWorld.TryResolveFluidSurfacePoint(spill.Center, out string surfaceWorld, out Vector2 surfacePosition)) continue;
                spill.WorldKey = surfaceWorld; spill.Center = surfacePosition;
            }
            if (spill.WorldKey == "SpaceScene") continue;
            FluidDefinition definition = FluidCatalog.Default.Find(spill.FluidId);
            if (GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetLiquidDefinition(definition.LiquidId, out LiquidDefinition liquid) ||
                liquid.WorldWater == null || liquid.WorldWater.DepthPerServing <= 0f) continue;
            while (spillCellsThisFrame < SpillCellChecksPerFrame && CanAdvanceLiquidSpill(spill))
            {
                int index = spill.NextCellIndex++;
                spillCellsThisFrame++;
                Vector2Int offset = SpiralCell(index);
                Vector2 point = index == 0 ? spill.Center : new Vector2(Mathf.Floor(spill.Center.x) + offset.x + .5f,
                    Mathf.Floor(spill.Center.y) + offset.y + .5f);
                decimal servings = FluidUnits.MolToServings(definition, spill.LiquidMoles);
                float depth = (float)Math.Min(1m, servings * (decimal)liquid.WorldWater.DepthPerServing);
                float accepted = 0f;
                if (WorldAddress.FromWorldKey(spill.WorldKey).IsSurface)
                    accepted = FlatWorld.Spaceflight.SpaceSurfaceQuery.ReleaseLiquidInWorld(spill.WorldKey, point, liquid.Id, depth);
                else if (SceneManager.GetActiveScene().name == spill.WorldKey)
                    WorldLiquidSystem.TryPour(point, liquid.Id, depth, out accepted);
                if (accepted <= 0f) continue;
                decimal moles = Math.Min(spill.LiquidMoles, FluidUnits.ServingsToMol(definition,
                    (decimal)accepted / (decimal)liquid.WorldWater.DepthPerServing));
                double fraction = (double)(moles / spill.LiquidMoles);
                spill.LiquidMoles -= moles; spill.InternalEnergyJoules *= 1d - fraction;
            }
            if (spillCellsThisFrame >= SpillCellChecksPerFrame) break;
        }
        // 满格或异种液体挡住的余量仍保存，有限扫描结束后不占持续运行预算。
        archive.PendingPressureLiquidSpills.RemoveAll(spill => spill.LiquidMoles <= 0m);
    }
    private static Vector2Int SpiralCell(int index)
    {
        if (index <= 0) return Vector2Int.zero;
        int ring = (int)Math.Ceiling((Math.Sqrt(index + 1d) - 1d) / 2d);
        int offset = (ring * 2 + 1) * (ring * 2 + 1) - 1 - index;
        if (offset < ring * 2) return new Vector2Int(ring - offset, -ring);
        if (offset < ring * 4) return new Vector2Int(-ring, -ring + offset - ring * 2);
        if (offset < ring * 6) return new Vector2Int(-ring + offset - ring * 4, ring);
        return new Vector2Int(ring, ring - offset + ring * 6);
    }
    public static void Present(PressureExplosionEvent entry)
    {
        if (entry == null) return;
        float radius = Radius(entry.Intensity);
        bool handled = PresentationHandler?.Invoke(entry, radius) == true;
        if (!handled && SceneManager.GetActiveScene().name == (entry.PresentationWorldKey ?? entry.WorldKey))
            PresentAt(WorldLocalPresentation.ProjectPosition(entry.Center), radius);
        // 发布独立于主机视野，远端玩家仍能收到这一笔唯一表现。
        Presented?.Invoke(entry, radius);
    }
    public static void PresentAt(Vector2 center, float radius)
    {
        if (radius <= 0f || !float.IsFinite(center.x) || !float.IsFinite(center.y) || Camera.main == null) return;
        Camera camera = Camera.main;
        Vector3 viewport = camera.WorldToViewportPoint(new Vector3(center.x, center.y, 0f));
        float margin = camera.orthographic ? Mathf.Clamp(radius, .4f, 8f) / Mathf.Max(1f, camera.orthographicSize * 2f) : .1f;
        if (viewport.z <= 0f || viewport.x < -margin || viewport.x > 1f + margin || viewport.y < -margin || viewport.y > 1f + margin) return;
        if (VisualEffectManager.Instance != null)
        {
            GameObject visual = VisualEffectManager.Instance.PlayEffect("Particle_Hit", center, .6f);
            if (visual != null)
            {
                visual.transform.localScale = Vector3.one;
                EmitExplosionFlashAndShock(visual, center, Mathf.Clamp(radius, .4f, 8f));
            }
        }
        AudioService.Instance.PlayAt(AudioEventIds.CombatHit, center, Mathf.Clamp(radius / 4f, .35f, 1f), .65f);
    }
    private static void EmitExplosionFlashAndShock(GameObject visual, Vector2 center, float radius)
    {
        ParticleSystem particles = visual.GetComponent<ParticleSystem>();
        if (particles == null) return;
        ParticleSystem.MainModule main = particles.main;
        main.stopAction = ParticleSystemStopAction.None;
        Transform frame = main.simulationSpace == ParticleSystemSimulationSpace.Custom ? main.customSimulationSpace : particles.transform;
        bool world = main.simulationSpace == ParticleSystemSimulationSpace.World;
        Vector3 position = world ? (Vector3)center : (frame ?? particles.transform).InverseTransformPoint(center);
        var flash = new ParticleSystem.EmitParams { position = position, velocity = Vector3.zero, startLifetime = .12f,
            startSize = radius * .8f, startColor = new Color(1f, .94f, .65f, 1f), applyShapeToPosition = false };
        particles.Emit(flash, 1);
        // 使用已有粒子材质和对象池，环形冲击只发有限颗粒并随统一回收清空。
        for (int i = 0; i < 32; i++)
        {
            float angle = i * Mathf.PI * 2f / 32f;
            Vector3 velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * (radius / .32f);
            if (!world) velocity = (frame ?? particles.transform).InverseTransformVector(velocity);
            var shock = new ParticleSystem.EmitParams { position = position, velocity = velocity, startLifetime = .32f,
                startSize = Mathf.Clamp(radius * .025f, .05f, .18f), startColor = new Color(1f, .55f, .12f, .8f), applyShapeToPosition = false };
            particles.Emit(shock, 1);
        }
    }
    #endregion
}

public static partial class MachineWorld
{
    #region 数据机械范围伤害
    public static void ApplyPressureExplosion(string world, Vector2 center, float radius, float peak)
    {
        if (!GameNetwork.HasStateAuthority || WorldKey != world || radius <= 0f) return;
        var targets = new List<MachineEntity>(nodes.Values);
        foreach (MachineEntity node in targets)
        {
            if (!Contains(node)) continue;
            float distance = PressureExplosionQueue.DeltaInWorld(world, center, node.Position).magnitude;
            if (distance <= radius) TryApplyEnvironmentalDamage(node, peak * (1f - distance / radius));
        }
    }
    #endregion
}
