using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.Pool;
using Stopwatch = System.Diagnostics.Stopwatch;

public partial class ItemMgr
{
    #region 世界散落物物质结算

    private static readonly Unity.Profiling.ProfilerMarker FlamePresentationMarker =
        new("ItemMgr.LateUpdate.WorldMatterFlames");
    private const float WorldMatterTickInterval = 1f;
    private const int WorldMatterItemsPerStep = 64;
    private static readonly long WorldMatterStepBudgetTicks = System.Math.Max(1L, Stopwatch.Frequency / 1000L);
    private readonly List<Item> worldMatterSnapshot = new();
    private readonly Dictionary<Item, WorldMatterEntry> worldMatterEntries = new(ItemReferenceComparer.Instance);
    private readonly Dictionary<Item, WorldMatterStructure> worldMatterStructures = new(ItemReferenceComparer.Instance);
    // 每帧只刷新确实存在火焰的散落物，其他物质条目仍按原周期结算。
    private readonly HashSet<WorldMatterEntry> worldMatterFlames = new();
    private double worldMatterTime;
    private double nextWorldMatterSweepTime = WorldMatterTickInterval;
    private int worldMatterCursor;

    private sealed class WorldMatterEntry
    {
        public Item Item;
        public ItemData Data;
        public uint Generation;
        public double LastTime;
        public WorldMatterStructure Structure;
        public ShaderFlameEffect Flame;
    }

    // 层级能力只在结构或代际改变时读取，动态物种和投射物附着仍使用当前状态。
    internal sealed class WorldMatterStructure
    {
        private readonly Item item;
        private readonly ItemData data;
        private readonly ItemMods modules;
        private readonly uint generation;
        private readonly uint structureVersion;
        private readonly bool hasAiComponent;
        private readonly bool hasTileReceiver;
        private readonly Mod_Droping dropping;
        private readonly Mod_Projectile projectile;
        private readonly Mod_Combustion combustion;
        private readonly Mod_CombustionVisual combustionVisual;

        internal WorldMatterStructure(Item item)
        {
            this.item = item;
            data = item.itemData;
            modules = item.itemMods;
            generation = item.RuntimeGeneration;
            structureVersion = modules?.StructureVersion ?? 0;
            // 燃烧能力随模块结构缓存，运行时启停仍按当前状态判断。
            combustion = modules?.GetMod_ByID<Mod_Combustion>(Mod_Combustion.ModuleId);
            combustionVisual = modules?.GetMod_ByID<Mod_CombustionVisual>(Mod_CombustionVisual.ModuleId);
            dropping = modules?.GetMod_ByID<Mod_Droping>(ModText.Drop);
            using var scope = ListPool<MonoBehaviour>.Get(out var behaviours);
            item.GetComponentsInChildren(true, behaviours);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                hasAiComponent |= behaviour is IAIActor or Mod_ItemChunkAssigner;
                hasTileReceiver |= behaviour is Mod_TileEffectReceiver;
                if (projectile == null && behaviour is Mod_Projectile candidate) projectile = candidate;
            }
        }

        internal bool IsCurrent(Item current) => ReferenceEquals(item, current) &&
            generation == current.RuntimeGeneration && ReferenceEquals(data, current.itemData) &&
            ReferenceEquals(modules, current.itemMods) && structureVersion == (current.itemMods?.StructureVersion ?? 0);

        internal Mod_Combustion CachedCombustion => combustion;
        internal Mod_CombustionVisual CachedCombustionVisual => combustionVisual;
        internal Mod_Combustion ActiveCombustion => combustion != null && combustion.Enabled && combustion.IsRuntimeLoaded
            ? combustion : null;
        internal Mod_CombustionVisual ActiveCombustionVisual => combustionVisual != null &&
            combustionVisual.Enabled && combustionVisual.IsRuntimeLoaded ? combustionVisual : null;

        internal bool BlocksWorldMatter => DropInProgress || hasTileReceiver ||
            (item is not Player && item is not global::Map &&
             (hasAiComponent || MonsterManager.IsRegisteredSpeciesId(item.itemData?.IDName))) ||
            (projectile != null && projectile.HasActiveWorldAttachment);

        internal bool DropInProgress => dropping?.drop != null &&
            !dropping.drop.waterFloating && !dropping.drop.waterSinking;
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
                // 热分解先交接新物质，避免同一轮燃烧把已满足转化条件的材料直接扣没。
                if (ItemMatterRuntime.TryCreateSolidTransitionOutputs(entry.Data, out IReadOnlyList<ItemData> products))
                {
                    var spawned = new List<DroppedItemHandle>();
                    try
                    {
                        Vector2 position = item.transform.position;
                        for (int index = 1; index < products.Count; index++)
                        {
                            DroppedItemHandle handle = DroppedItemService.Spawn(products[index], position, position);
                            if (!handle.IsValid) throw new System.InvalidOperationException("材料转化产物未成功落地。");
                            spawned.Add(handle);
                        }
                        products[0].Guid = entry.Data.Guid;
                        products[0].inHand = entry.Data.inHand;
                        ReplaceRuntimeItem(item, products[0]);
                    }
                    catch
                    {
                        foreach (DroppedItemHandle handle in spawned) DroppedItemService.Remove(handle);
                        throw;
                    }
                    continue;
                }
                bool consumedAll = false;
                Mod_Combustion combustion = entry.Structure.ActiveCombustion;
                if (combustion != null)
                {
                    if (waterContact && combustion.IsBurning)
                        combustion.Extinguish();
                    else if (!waterContact && !combustion.IsBurning && WorldItemMatterRules.CanIgnite(entry.Data))
                        combustion.Ignite();
                }
                else
                {
                    changed |= ItemMatterRuntime.AdvanceCombustion(entry.Data, seconds, waterContact,
                        ItemEnvironmentSources.CanSustainCombustion(item), out consumedAll);
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

            RefreshWorldItemCombustionVisual(entry, alreadyValidated: true);
        }
    }

    private bool IsWorldMatterCandidate(Item item)
    {
        if (item == null || !item.IsInitialized || item.DestructionHandled || item.IsInPool ||
            !item.gameObject.activeInHierarchy || !IsRuntimeItemRegistered(item) ||
            item.Owner != null || item.InHand || item.itemData?.Stack == null ||
            !item.itemData.Stack.CanBePickedUp || item.itemData.Stack.Amount <= 0f)
            return false;
        if (!worldMatterStructures.TryGetValue(item, out WorldMatterStructure structure) || !structure.IsCurrent(item))
            worldMatterStructures[item] = structure = new WorldMatterStructure(item);
        return !structure.BlocksWorldMatter;
    }

    // 注册或加载完成就建立时间基线，避免首轮分帧扫描丢掉已过去的游戏时间。
    private void TrackWorldItemMatter(Item item)
    {
        if (!ReferenceEquals(item, null)) worldMatterStructures.Remove(item);
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
            {
                entry.Structure = worldMatterStructures[item];
                return entry;
            }
            ReleaseWorldItemMatter(item);
        }

        entry = new WorldMatterEntry
        {
            Item = item,
            Data = item.itemData,
            Generation = item.RuntimeGeneration,
            LastTime = worldMatterTime,
            Structure = worldMatterStructures[item]
        };
        worldMatterEntries.Add(item, entry);
        return entry;
    }

    private bool IsWorldMatterEntryCurrent(WorldMatterEntry entry)
    {
        if (!IsWorldMatterCandidate(entry.Item) || entry.Generation != entry.Item.RuntimeGeneration ||
            !ReferenceEquals(entry.Data, entry.Item.itemData)) return false;
        entry.Structure = worldMatterStructures[entry.Item];
        return true;
    }

    #endregion

    #region 世界散落物燃烧表现与释放

    // 火焰挂在管理器下跟随真实贴图，不改变物品对象池的层级基线。
    private void RefreshWorldItemCombustionVisual(WorldMatterEntry entry, bool alreadyValidated = false)
    {
        if (!alreadyValidated && !IsWorldMatterEntryCurrent(entry))
        {
            DestroyWorldItemCombustionVisual(entry);
            return;
        }

        Mod_Combustion combustion = entry.Structure.ActiveCombustion;
        Mod_CombustionVisual moduleVisual = entry.Structure.ActiveCombustionVisual;
        bool hasModuleVisual = combustion != null && moduleVisual != null;
        bool burning = !hasModuleVisual && (combustion != null
            ? combustion.IsActivelyBurning
            : entry.Data.MatterState?.IsBurning == true);
        if (!burning)
        {
            DestroyWorldItemCombustionVisual(entry);
            return;
        }

        if (entry.Flame == null) entry.Flame = WorldItemCombustionVisual.Create(transform);
        // 特效资源暂不可用时不可留下空索引；下一次物质 Tick 仍可重试。
        if (entry.Flame == null)
        {
            worldMatterFlames.Remove(entry);
            return;
        }
        worldMatterFlames.Add(entry);
        SpriteRenderer sprite = entry.Item.Sprite;
        Vector3 position = sprite != null ? sprite.transform.position : entry.Item.transform.position;
        if (entry.Flame.transform.position != position) entry.Flame.transform.position = position;
        if (sprite != null)
        {
            WorldSortingManager.ReadExternalRendererKey(sprite, out int layer, out int order);
            entry.Flame.SetSorting(layer, order + 2);
        }
        entry.Flame.SetBurning(true);
    }

    private void PresentWorldItemMatter()
    {
        if (worldMatterFlames.Count == 0) return;
        using var sample = FlamePresentationMarker.Auto();
        // Refresh 可销毁火焰/注销物品，租借独立快照同时兼容回调重入。
        using var lease = ListPool<WorldMatterEntry>.Get(out var active);
        active.AddRange(worldMatterFlames);
        for (int i = 0; i < active.Count; i++)
        {
            WorldMatterEntry entry = active[i];
            if (!worldMatterFlames.Contains(entry)) continue;
            if (entry.Flame == null) { DestroyWorldItemCombustionVisual(entry); continue; }
            RefreshWorldItemCombustionVisual(entry);
        }
    }

    private void ReleaseWorldItemMatter(Item item)
    {
        if (ReferenceEquals(item, null))
            return;
        if (!IsRuntimeItemRegistered(item)) worldMatterStructures.Remove(item);
        if (!worldMatterEntries.TryGetValue(item, out WorldMatterEntry entry)) return;
        DestroyWorldItemCombustionVisual(entry);
        worldMatterEntries.Remove(item);
    }

    private void DestroyWorldItemCombustionVisual(WorldMatterEntry entry)
    {
        worldMatterFlames.Remove(entry);
        if (entry.Flame == null) { entry.Flame = null; return; }
        entry.Flame.SetBurning(false);
        UnityEngine.Object.Destroy(entry.Flame.gameObject);
        entry.Flame = null;
    }

    private void ClearWorldItemMatter()
    {
        foreach (WorldMatterEntry entry in worldMatterEntries.Values)
            DestroyWorldItemCombustionVisual(entry);
        worldMatterEntries.Clear();
        worldMatterFlames.Clear();
        worldMatterStructures.Clear();
        worldMatterSnapshot.Clear();
        worldMatterCursor = 0;
        worldMatterTime = 0;
        nextWorldMatterSweepTime = WorldMatterTickInterval;
    }

    #endregion
}
