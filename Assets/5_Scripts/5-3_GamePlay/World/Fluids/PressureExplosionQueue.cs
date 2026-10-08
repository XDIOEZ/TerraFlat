using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

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
    public Vector2 Center;
    public double Intensity;
    public List<PressureExplosionMaterial> Materials = new();
    #endregion
}

public partial class MachineArchive
{
    #region 压力爆炸存档
    public ulong NextPressureExplosionId;
    public List<PressureExplosionEvent> PressureExplosions = new();
    public List<string> RecentPressureExplosionSources = new();
    #endregion
}

/// <summary>破裂先提交唯一事件；每个事件的物料、范围伤害与连锁只结算一次。</summary>
public static class PressureExplosionQueue
{
    #region 事件入队与有限曲线
    public static event Action<PressureExplosionEvent, float> Presented;
    private const int EventsPerTick = 32;
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
            Center = WorldTopologyRuntime.NormalizePosition(center), Intensity = intensity, SourceId = sourceId };
        AddMaterials(entry, gas); AddMaterials(entry, liquid);
        archive.PressureExplosions.Add(entry);
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
        for (int handled = 0; handled < EventsPerTick; handled++)
        {
            int index = archive.PressureExplosions.FindIndex(value => value.WorldKey == world);
            if (index < 0) break;
            PressureExplosionEvent entry = archive.PressureExplosions[index];
            archive.PressureExplosions.RemoveAt(index);
            archive.RecentPressureExplosionSources.Add(entry.SourceId);
            if (archive.RecentPressureExplosionSources.Count > 4096) archive.RecentPressureExplosionSources.RemoveAt(0);
            Release(entry);
            float radius = Radius(entry.Intensity), peak = CenterDamage(entry.Intensity);
            if (radius > 0f && peak > 0f)
            {
                var targets = ItemMgr.Instance != null ? new List<Item>(ItemMgr.Instance.WorldRunTimeItems.Values) : new();
                foreach (Item target in targets)
                {
                    if (target == null || target.Owner != null || target.InHand || target.gameObject.scene.name != entry.WorldKey) continue;
                    float distance = WorldTopologyRuntime.Distance(entry.Center, WorldLocalPresentation.ToLogical(target.transform.position));
                    if (distance > radius) continue;
                    target.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.HurtPressureExplosion(entry.Id, entry.Center, peak * (1f - distance / radius));
                }
                MachineWorld.ApplyPressureExplosion(entry.WorldKey, entry.Center, radius, peak);
                FlatWorld.NaturalEntities.NaturalEntityEcsService.ApplyPressureExplosion(entry.WorldKey, entry.Id, entry.Center, radius, peak);
            }
            Present(entry);
        }
        if (!archive.PressureExplosions.Exists(value => value.WorldKey == world)) MachineWorld.CompleteFluidRuptures();
    }
    private static void Release(PressureExplosionEvent entry)
    {
        AtmosphereService.TryGetForWorld(entry.WorldKey, out AtmosphereState atmosphere);
        foreach (PressureExplosionMaterial material in entry.Materials)
        {
            FluidBatch batch = material.Batch;
            FluidDefinition definition = FluidCatalog.Default.Find(batch.FluidId);
            if (batch.GasMoles > 0m && atmosphere != null)
                AtmosphereService.Emit(atmosphere, new FluidBatch(batch.FluidId, batch.GasMoles, 0m, 0d));
            if (batch.LiquidMoles <= 0m || string.IsNullOrWhiteSpace(definition.LiquidId)) continue;
            if (GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetLiquidDefinition(definition.LiquidId, out LiquidDefinition liquid) || liquid.WorldWater == null) continue;
            decimal servings = FluidUnits.MolToServings(definition, batch.LiquidMoles);
            float depth = (float)Math.Min(servings, 1000000m) * liquid.WorldWater.DepthPerServing;
            WorldLiquidSystem.TryPour(entry.Center, liquid.Id, depth, out _);
        }
    }
    public static void Present(PressureExplosionEvent entry)
    {
        if (entry == null || MachineWorld.WorldKey != entry.WorldKey) return;
        float radius = Radius(entry.Intensity);
        if (radius > 0f && VisualEffectManager.Instance != null)
        {
            GameObject visual = VisualEffectManager.Instance.PlayEffect("Particle_Hit", WorldLocalPresentation.ProjectPosition(entry.Center), .6f);
            if (visual != null) visual.transform.localScale = Vector3.one * Mathf.Clamp(radius, .4f, 8f);
        }
        Presented?.Invoke(entry, radius);
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
            float distance = WorldTopologyRuntime.Distance(center, node.Position);
            if (distance <= radius) TryApplyEnvironmentalDamage(node, peak * (1f - distance / radius));
        }
    }
    #endregion
}
