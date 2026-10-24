using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

public partial class WeatherMgr
{
    #region 雾天主动限制生物感知

    private ItemMgr fogItemManager;
    private RegionalWeatherEngine fogPerceptionEngine;
    private readonly Dictionary<Item, Int2> fogCreatureRegions = new();
    private readonly Dictionary<Int2, FogPerceptionRegion> fogPerceptionRegions = new();

    // 每区只在雾半径变化时下发生物上限，普通帧不遍历全部生物。
    internal sealed class FogPerceptionRegion
    {
        private readonly object source;
        private readonly HashSet<Item> creatures = new();
        private float radius = float.PositiveInfinity;
        internal int Count => creatures.Count;

        internal FogPerceptionRegion(object owner) => source = owner;

        internal void Add(Item creature)
        {
            creatures.Add(creature);
            Apply(creature);
        }

        internal void Remove(Item creature)
        {
            creatures.Remove(creature);
            if (creature != null) creature.RemovePerceptionRadiusLimit(source);
        }

        internal void Update(float density, WorldRenderingConfig.RenderingDenseFog config)
        {
            float next = ResolveFogPerceptionRadius(density, config);
            if (next == radius) return;
            radius = next;
            foreach (Item creature in creatures) Apply(creature);
        }

        private void Apply(Item creature)
        {
            if (creature == null) return;
            if (float.IsPositiveInfinity(radius)) creature.RemovePerceptionRadiusLimit(source);
            else creature.SetPerceptionRadiusLimit(source, radius);
        }
    }

    internal static float ResolveFogPerceptionRadius(float density, WorldRenderingConfig.RenderingDenseFog config)
    {
        return config?.IsValid == true && config.TryResolveRadii(density, out Vector2 radii) &&
               float.IsFinite(radii.x) ? radii.x : float.PositiveInfinity;
    }

    private void MaintainFogPerception()
    {
        ItemMgr manager = ItemMgr.GetExistingInstance();
        if (!_weatherRuntimeAllowed || !GameNetwork.HasStateAuthority || regionalEngine == null || manager == null)
        {
            ResetFogPerception();
            return;
        }
        if (fogItemManager != manager || !ReferenceEquals(fogPerceptionEngine, regionalEngine))
        {
            ResetFogPerception();
            fogItemManager = manager;
            fogPerceptionEngine = regionalEngine;
            ItemMgr.RuntimeItemRegistered += TrackFogCreature;
            ItemMgr.RuntimeItemUnregistered += ReleaseFogCreature;
            manager.RuntimeAiAddressChanged += TrackFogCreature;
            foreach (Item creature in manager.WorldRunTimeItems.Values) TrackFogCreature(creature);
        }

        WorldRenderingConfig.RenderingDenseFog config = WorldRenderingConfigCatalog.Default.denseFog;
        foreach (var pair in fogPerceptionRegions)
            pair.Value.Update(regionalEngine.TryGetRegionSnapshot(pair.Key, out var weather)
                ? weather.FogDensity : 0f, config);
    }

    private void TrackFogCreature(Item creature)
    {
        if (creature == null) return;
        if (creature is Player || creature.gameObject.scene.name != ownedWorld)
        {
            ReleaseFogCreature(creature);
            return;
        }
        using var scope = EnterOwnedWorld();
        if (fogItemManager == null || regionalEngine == null ||
            !fogItemManager.TryGetRuntimeEntityAddress(creature, out var address))
        {
            ReleaseFogCreature(creature);
            return;
        }
        Int2 region = regionalEngine.ResolveRegion(address.ChunkOrigin);
        if (fogCreatureRegions.TryGetValue(creature, out Int2 previous))
        {
            if (previous.Equals(region)) return;
            ReleaseFogCreature(creature);
        }
        if (!fogPerceptionRegions.TryGetValue(region, out FogPerceptionRegion influence))
        {
            influence = new FogPerceptionRegion(this);
            influence.Update(regionalEngine.TryGetRegionSnapshot(region, out var weather) ? weather.FogDensity : 0f,
                WorldRenderingConfigCatalog.Default.denseFog);
            fogPerceptionRegions.Add(region, influence);
        }
        fogCreatureRegions.Add(creature, region);
        influence.Add(creature);
    }

    private void ReleaseFogCreature(Item creature)
    {
        if (ReferenceEquals(creature, null) || !fogCreatureRegions.Remove(creature, out Int2 region)) return;
        using var scope = EnterOwnedWorld();
        if (!fogPerceptionRegions.TryGetValue(region, out FogPerceptionRegion influence)) return;
        influence.Remove(creature);
        if (influence.Count == 0) fogPerceptionRegions.Remove(region);
    }

    private void ResetFogPerception()
    {
        using var scope = EnterOwnedWorld();
        ItemMgr.RuntimeItemRegistered -= TrackFogCreature;
        ItemMgr.RuntimeItemUnregistered -= ReleaseFogCreature;
        if (fogItemManager != null) fogItemManager.RuntimeAiAddressChanged -= TrackFogCreature;
        foreach (Item creature in fogCreatureRegions.Keys)
            if (creature != null) creature.RemovePerceptionRadiusLimit(this);
        fogCreatureRegions.Clear();
        fogPerceptionRegions.Clear();
        fogItemManager = null;
        fogPerceptionEngine = null;
    }

    #endregion
}
