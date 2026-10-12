using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

public partial class ItemMgr
{
    #region 世界散落物物质结算

    private const float WorldMatterTickInterval = 1f;
    private const int WorldMatterItemsPerStep = 64;
    private static readonly long WorldMatterStepBudgetTicks = System.Math.Max(1L, Stopwatch.Frequency / 1000L);
    private readonly List<Item> worldMatterSnapshot = new();
    private readonly Dictionary<Item, WorldMatterEntry> worldMatterEntries = new(ItemReferenceComparer.Instance);
    private double worldMatterTime;
    private double nextWorldMatterSweepTime = WorldMatterTickInterval;
    private int worldMatterCursor;

    private sealed class WorldMatterEntry
    {
        public Item Item;
        public ItemData Data;
        public uint Generation;
        public double LastTime;
        public ParticleSystem Particles;
        public ParticleSystemRenderer ParticleRenderer;
    }

    // 完整散落物独立于模块调度推进，休眠模块的普通联机掉落也能受热燃烧。
    private void TickWorldItemMatter(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return;
        worldMatterTime += deltaTime;
        if (worldMatterCursor >= worldMatterSnapshot.Count)
        {
            if (worldMatterTime < nextWorldMatterSweepTime) return;
            worldMatterSnapshot.Clear();
            worldMatterSnapshot.AddRange(RuntimeItems);
            worldMatterCursor = 0;
            nextWorldMatterSweepTime = worldMatterTime + WorldMatterTickInterval;
        }

        long deadline = Stopwatch.GetTimestamp() + WorldMatterStepBudgetTicks;
        for (int processed = 0; worldMatterCursor < worldMatterSnapshot.Count &&
             processed < WorldMatterItemsPerStep && Stopwatch.GetTimestamp() < deadline; processed++)
        {
            Item item = worldMatterSnapshot[worldMatterCursor++];
            if (!IsWorldMatterCandidate(item))
            {
                ReleaseWorldItemMatter(item);
                continue;
            }

            WorldMatterEntry entry = GetWorldMatterEntry(item);
            float seconds = (float)(worldMatterTime - entry.LastTime);
            entry.LastTime = worldMatterTime;
            if (GameNetwork.HasStateAuthority && seconds > 0f)
            {
                bool changed = WorldItemMatterRules.Advance(entry.Data, item.transform.position, seconds,
                    liquidContact: true, out bool waterContact);
                bool consumedAll = false;
                Mod_Combustion combustion = GetWorldMatterCombustion(item);
                if (combustion != null)
                {
                    if (waterContact && combustion.IsBurning)
                        combustion.Extinguish();
                    else if (!waterContact && !combustion.IsBurning && WorldItemMatterRules.CanIgnite(entry.Data))
                        combustion.Ignite();
                }
                else
                {
                    changed |= ItemMatterRuntime.AdvanceCombustion(entry.Data, seconds, waterContact, out consumedAll);
                }

                if (!IsWorldMatterEntryCurrent(entry)) continue;
                if (consumedAll)
                {
                    DespawnItem(item, saveData: false);
                    continue;
                }
                if (changed)
                    ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
                if (!IsWorldMatterEntryCurrent(entry)) continue;
            }

            RefreshWorldItemCombustionVisual(entry);
        }
    }

    private bool IsWorldMatterCandidate(Item item)
    {
        if (item == null || !item.IsInitialized || item.DestructionHandled || item.IsInPool ||
            !item.gameObject.activeInHierarchy || !IsRuntimeItemRegistered(item) ||
            item.Owner != null || item.InHand || item.itemData?.Stack == null ||
            !item.itemData.Stack.CanBePickedUp || item.itemData.Stack.Amount <= 0f ||
            RuntimeAiEntityUtility.IsAiEntity(item) || Mod_Droping.IsDropInProgress(item))
            return false;
        if (item.GetComponentInChildren<Mod_TileEffectReceiver>(true) != null)
            return false;
        Mod_Projectile projectile = item.GetComponentInChildren<Mod_Projectile>(true);
        return projectile == null || !projectile.HasActiveWorldAttachment;
    }

    // 注册或加载完成就建立时间基线，避免首轮分帧扫描丢掉已过去的游戏时间。
    private void TrackWorldItemMatter(Item item)
    {
        if (IsWorldMatterCandidate(item))
            GetWorldMatterEntry(item);
        else
            ReleaseWorldItemMatter(item);
    }

    private WorldMatterEntry GetWorldMatterEntry(Item item)
    {
        if (worldMatterEntries.TryGetValue(item, out WorldMatterEntry entry))
        {
            if (entry.Generation == item.RuntimeGeneration && ReferenceEquals(entry.Data, item.itemData))
                return entry;
            ReleaseWorldItemMatter(item);
        }

        entry = new WorldMatterEntry
        {
            Item = item,
            Data = item.itemData,
            Generation = item.RuntimeGeneration,
            LastTime = worldMatterTime
        };
        worldMatterEntries.Add(item, entry);
        return entry;
    }

    private bool IsWorldMatterEntryCurrent(WorldMatterEntry entry)
        => IsWorldMatterCandidate(entry.Item) && entry.Generation == entry.Item.RuntimeGeneration &&
           ReferenceEquals(entry.Data, entry.Item.itemData);

    private static Mod_Combustion GetWorldMatterCombustion(Item item)
    {
        Mod_Combustion combustion = item.itemMods?.GetMod_ByID<Mod_Combustion>(Mod_Combustion.ModuleId);
        return combustion != null && combustion.Enabled && combustion.IsRuntimeLoaded ? combustion : null;
    }

    #endregion

    #region 世界散落物燃烧表现与释放

    // 火焰挂在管理器下跟随真实贴图，不改变物品对象池的层级基线。
    private void RefreshWorldItemCombustionVisual(WorldMatterEntry entry)
    {
        if (!IsWorldMatterEntryCurrent(entry))
        {
            DestroyWorldItemCombustionVisual(entry);
            return;
        }

        Mod_Combustion combustion = GetWorldMatterCombustion(entry.Item);
        Mod_CombustionVisual moduleVisual = entry.Item.itemMods?.GetMod_ByID<Mod_CombustionVisual>(Mod_CombustionVisual.ModuleId);
        bool hasModuleVisual = combustion != null && moduleVisual != null &&
            moduleVisual.Enabled && moduleVisual.IsRuntimeLoaded;
        bool burning = !hasModuleVisual && (combustion != null
            ? combustion.IsActivelyBurning
            : entry.Data.MatterState?.IsBurning == true);
        if (!burning)
        {
            DestroyWorldItemCombustionVisual(entry);
            return;
        }

        if (entry.Particles == null)
        {
            entry.Particles = WorldItemCombustionVisual.Create(transform);
            entry.ParticleRenderer = entry.Particles.GetComponent<ParticleSystemRenderer>();
        }
        SpriteRenderer sprite = entry.Item.Sprite;
        entry.Particles.transform.position = sprite != null ? sprite.transform.position : entry.Item.transform.position;
        if (sprite != null && entry.ParticleRenderer != null)
        {
            WorldSortingManager.ReadExternalRendererKey(sprite, out int layer, out int order);
            entry.ParticleRenderer.sortingLayerID = layer;
            entry.ParticleRenderer.sortingOrder = order + 2;
        }
        if (!entry.Particles.isPlaying)
            entry.Particles.Play(true);
    }

    private void PresentWorldItemMatter()
    {
        foreach (WorldMatterEntry entry in worldMatterEntries.Values)
            if (entry.Particles != null)
                RefreshWorldItemCombustionVisual(entry);
    }

    private void ReleaseWorldItemMatter(Item item)
    {
        if (ReferenceEquals(item, null) || !worldMatterEntries.TryGetValue(item, out WorldMatterEntry entry))
            return;
        DestroyWorldItemCombustionVisual(entry);
        worldMatterEntries.Remove(item);
    }

    private static void DestroyWorldItemCombustionVisual(WorldMatterEntry entry)
    {
        if (entry.Particles == null) return;
        entry.Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        UnityEngine.Object.Destroy(entry.Particles.gameObject);
        entry.Particles = null;
        entry.ParticleRenderer = null;
    }

    private void ClearWorldItemMatter()
    {
        foreach (WorldMatterEntry entry in worldMatterEntries.Values)
            DestroyWorldItemCombustionVisual(entry);
        worldMatterEntries.Clear();
        worldMatterSnapshot.Clear();
        worldMatterCursor = 0;
        worldMatterTime = 0;
        nextWorldMatterSweepTime = WorldMatterTickInterval;
    }

    #endregion
}
