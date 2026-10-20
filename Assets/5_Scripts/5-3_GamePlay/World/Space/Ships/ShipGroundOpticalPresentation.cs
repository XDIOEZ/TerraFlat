using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    /// <summary>普通地面光纤只按正式建筑的安装、占地和卸载事件刷新四邻外形。</summary>
    public static class ShipGroundOpticalPresentation
    {
        #region 按场景与本地格索引
        private sealed class Entry
        {
            internal Mod_ShipPart Module;
            internal Item Item;
            internal int Scene;
            internal Vector2Int Cell;
            internal SpriteRenderer[] Parts;
            internal Sprite Source;
            internal int Mask = -1;
        }
        private static readonly Dictionary<Mod_ShipPart, Entry> entries = new();
        private static readonly Dictionary<(int Scene, Vector2Int Cell), List<Entry>> cells = new();
        private static readonly Dictionary<int, Scene> scenes = new();
        private static readonly Dictionary<RuntimeItemDefinition, bool> endpointDefinitions = new();
        private static readonly Dictionary<string, IDisposable> occupancySubscriptions = new(StringComparer.Ordinal);
        static ShipGroundOpticalPresentation() => SharedSpriteMeshCache.Clearing += endpointDefinitions.Clear;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Item.RuntimeStructureChanged -= OnItemChanged;
            ClearOccupancySubscriptions();
            MachineWorld.CellChanged -= OnCellChanged;
            foreach (Entry entry in entries.Values) ShipFiberConnectionVisual.Release(ref entry.Parts);
            entries.Clear(); cells.Clear(); scenes.Clear(); endpointDefinitions.Clear();
        }

        internal static void Register(Mod_ShipPart module)
        {
            if (module?.item == null || module.Configuration == null ||
                (module.Configuration.Kind != ShipPieceKind.Fiber &&
                 !ShipOpticalGraph.IsOpticalEndpoint(module.Configuration.Kind))) return;
            // 重复订阅先撤后加，同时覆盖关闭域重载的静态事件初始化顺序。
            Item.RuntimeStructureChanged -= OnItemChanged;
            Item.RuntimeStructureChanged += OnItemChanged;
            string world = module.item.gameObject.scene.name;
            if (!occupancySubscriptions.ContainsKey(world))
            {
                using var scope = GameplayWorldRegistry.TryGet(world, out _) ? GameplayWorldRegistry.UseWorld(world) : null;
                occupancySubscriptions.Add(world, BuildingOccupancyRegistry.SubscribeCells(OnCellChanged));
            }
            MachineWorld.CellChanged -= OnCellChanged;
            MachineWorld.CellChanged += OnCellChanged;
            if (entries.ContainsKey(module)) { Refresh(module); return; }
            var entry = new Entry { Module = module, Item = module.item };
            entries.Add(module, entry);
            AddCell(entry);
            RefreshNeighborhood(entry.Scene, entry.Cell);
        }

        internal static void Unregister(Mod_ShipPart module)
        {
            if (module == null || !entries.Remove(module, out Entry entry)) return;
            RemoveCell(entry);
            ShipFiberConnectionVisual.Release(ref entry.Parts);
            RefreshNeighborhood(entry.Scene, entry.Cell);
            if (entries.Count != 0) return;
            Item.RuntimeStructureChanged -= OnItemChanged;
            ClearOccupancySubscriptions();
            MachineWorld.CellChanged -= OnCellChanged;
        }

        internal static void Refresh(Mod_ShipPart module)
        {
            if (!entries.TryGetValue(module, out Entry entry)) return;
            if (module.Configuration.Kind != ShipPieceKind.Fiber && !ShipOpticalGraph.IsOpticalEndpoint(module.Configuration.Kind))
            { Unregister(module); return; }
            if (module.Configuration.Kind != ShipPieceKind.Fiber) ShipFiberConnectionVisual.Release(ref entry.Parts);
            entry.Mask = -1;
            int oldScene = entry.Scene; Vector2Int oldCell = entry.Cell;
            RemoveCell(entry); AddCell(entry);
            if (oldScene != entry.Scene || oldCell != entry.Cell) RefreshNeighborhood(oldScene, oldCell);
            RefreshNeighborhood(entry.Scene, entry.Cell);
        }

        private static void AddCell(Entry entry)
        {
            entry.Scene = entry.Item.gameObject.scene.handle;
            scenes[entry.Scene] = entry.Item.gameObject.scene;
            Vector2 logical = WorldLocalPresentation.ToLogical((Vector2)entry.Item.transform.position);
            entry.Cell = WorldTopologyRuntime.NormalizeCell(new Vector2Int(Mathf.FloorToInt(logical.x), Mathf.FloorToInt(logical.y)));
            var key = (entry.Scene, entry.Cell);
            if (!cells.TryGetValue(key, out List<Entry> values)) cells.Add(key, values = new List<Entry>(2));
            values.Add(entry);
        }

        private static void RemoveCell(Entry entry)
        {
            var key = (entry.Scene, entry.Cell);
            if (!cells.TryGetValue(key, out List<Entry> values)) return;
            values.Remove(entry);
            if (values.Count == 0) cells.Remove(key);
        }

        private static void OnItemChanged(Item item)
        {
            Mod_ShipPart module = item?.itemMods?.GetMod_ByID<Mod_ShipPart>(Mod_ShipPart.ModuleId);
            if (module != null) Refresh(module);
        }

        private static void ClearOccupancySubscriptions()
        {
            foreach (IDisposable subscription in occupancySubscriptions.Values) subscription.Dispose();
            occupancySubscriptions.Clear();
        }

        // 占地事件携带同步世界上下文，后台地表和本机画面刷新各自的光纤。
        private static void OnCellChanged(Vector2Int cell)
        {
            if (MachineWorld.IsShipScope) return;
            Scene scene = SceneManager.GetSceneByName(MachineWorld.WorldKey);
            if (!scene.IsValid()) scene = GameplayWorldRegistry.CurrentScene;
            RefreshNeighborhood(scene.handle, cell);
        }

        private static void RefreshNeighborhood(int scene, Vector2Int cell)
        {
            RefreshCell(scene, cell);
            RefreshCell(scene, WorldTopologyRuntime.NormalizeCell(cell + Vector2Int.up));
            RefreshCell(scene, WorldTopologyRuntime.NormalizeCell(cell + Vector2Int.right));
            RefreshCell(scene, WorldTopologyRuntime.NormalizeCell(cell + Vector2Int.down));
            RefreshCell(scene, WorldTopologyRuntime.NormalizeCell(cell + Vector2Int.left));
        }
        #endregion

        #region 四邻外形刷新
        private static bool IsInstalled(Entry entry)
            => entry.Module != null && entry.Item != null && !entry.Item.DestructionHandled &&
               entry.Module.Enabled && entry.Item.gameObject.activeInHierarchy &&
               SpaceSession.Current?.Owns(entry.Item.itemData.Guid) != true &&
               entry.Item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.IsInstalled() == true;

        public static int GetConnectionMask(Item fiber)
        {
            Mod_ShipPart module = fiber?.itemMods?.GetMod_ByID<Mod_ShipPart>(Mod_ShipPart.ModuleId);
            if (module == null || module.Configuration.Kind != ShipPieceKind.Fiber ||
                !entries.TryGetValue(module, out Entry entry) || !IsInstalled(entry)) return 0;
            int mask = 0;
            if (HasNode(entry.Scene, entry.Cell + Vector2Int.up)) mask |= 1;
            if (HasNode(entry.Scene, entry.Cell + Vector2Int.right)) mask |= 2;
            if (HasNode(entry.Scene, entry.Cell + Vector2Int.down)) mask |= 4;
            if (HasNode(entry.Scene, entry.Cell + Vector2Int.left)) mask |= 8;
            return mask;
        }

        private static bool HasNode(int scene, Vector2Int cell)
        {
            cell = WorldTopologyRuntime.NormalizeCell(cell);
            if (cells.TryGetValue((scene, cell), out List<Entry> values))
                foreach (Entry entry in values) if (IsInstalled(entry)) return true;
            if (!scenes.TryGetValue(scene, out Scene target)) return false;
            if (!target.IsValid() || !target.isLoaded) return false;
            using var world = MachineWorld.UseScope(target.name);
            MachineEntity node = MachineWorld.GetAtCurrentWorld(cell, 0);
            if (node == null || node.Cell != cell || GameRes.ExistingInstance == null ||
                !GameRes.ExistingInstance.TryGetItemDefinition(node.Definition.Id, out RuntimeItemDefinition definition)) return false;
            if (!endpointDefinitions.TryGetValue(definition, out bool endpoint))
            {
                ShipPartConfiguration part = Mod_ShipPart.Read(node.Snapshot);
                endpoint = part != null && ShipOpticalGraph.IsOpticalEndpoint(part.Kind);
                endpointDefinitions.Add(definition, endpoint);
            }
            return endpoint;
        }

        private static void RefreshCell(int scene, Vector2Int cell)
        {
            if (!cells.TryGetValue((scene, cell), out List<Entry> values)) return;
            foreach (Entry entry in values)
            {
                if (entry.Module.Configuration.Kind != ShipPieceKind.Fiber) continue;
                if (!IsInstalled(entry)) { ShipFiberConnectionVisual.Release(ref entry.Parts); entry.Mask = -1; continue; }
                SpriteRenderer source = entry.Item.Sprite;
                if (source == null) continue;
                int mask = GetConnectionMask(entry.Item);
                if (entry.Parts != null && entry.Parts[0] != null && entry.Parts[0].sprite != null &&
                    entry.Mask == mask && entry.Source == source.sprite) continue;
                bool created = entry.Parts == null;
                source.transform.localRotation = Quaternion.Euler(0f, 0f, -entry.Item.transform.eulerAngles.z);
                ShipFiberConnectionVisual.Update(source, ref entry.Parts, mask);
                entry.Mask = mask; entry.Source = source.sprite;
                // 新增子片后让正式建筑绘制桥重新收集一次，之后只切换已有子片的显示。
                if (created) BuildingDepthMeshBridge.Register(entry.Item);
            }
        }
        #endregion
    }
}
