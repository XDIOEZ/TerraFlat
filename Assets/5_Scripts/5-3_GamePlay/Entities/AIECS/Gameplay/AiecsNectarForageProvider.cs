using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>采蜜能力从现有作物和地表花读取资源，目标身份仍由 ECS 保存。</summary>
    internal static class AiecsNectarForageProvider
    {
        #region 目标搜索

        private const string CropTag = "BeeForage.Crop";
        private const string FlowerTag = "BeeForage.Flower";

        private static readonly List<Item> Candidates = new();
        private static readonly HashSet<Item> Dedupe = new();

        public static bool TryFind(Vector2 origin, out Vector2 target, out int guid,
            out AiecsFoodTarget kind)
        {
            target = default;
            guid = 0;
            kind = default;
            ChunkMgr chunks = ChunkMgr.ExistingInstance;
            ItemMgr items = ItemMgr.Instance;
            if (chunks == null || items == null || GameRes.Instance == null)
                return false;
            float nearest = float.PositiveInfinity;
            Vector2 size = ChunkMgr.GetChunkSize();
            Vector2Int current = Chunk.GetChunkPosition(origin);
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    Vector2 center = WorldTopologyRuntime.NormalizePosition(new Vector2(
                        current.x + (x + 0.5f) * size.x,
                        current.y + (y + 0.5f) * size.y));
                    if (!chunks.TryGetRuntimeTerrainTile(center, out RuntimeTerrainTileSample sample) ||
                        !chunks.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
                        continue;
                    IReadOnlyList<NaturalItemPlacement> placements = chunk.Ecology?.Placements;
                    if (placements == null) continue;
                    for (int i = 0; i < placements.Count; i++)
                    {
                        NaturalItemPlacement placement = placements[i];
                        if (!GameRes.Instance.TryGetItemDefinition(placement.ItemId, out RuntimeItemDefinition definition) ||
                            !definition.IsGroundCover || !definition.HasTag(FlowerTag) ||
                            chunks.IsNaturalItemRemoved(chunk.Address, placement.Guid) ||
                            !GroundCoverSystem.TryFindAt(chunk, placement.LocalX, placement.LocalY, chunks,
                                out GroundCoverTarget visible) || visible.Placement.Guid != placement.Guid)
                            continue;
                        Vector2 position = new GroundCoverTarget(chunk, placement, definition).WorldPosition;
                        if (!CanLand(position)) continue;
                        float distance = WorldTopologyRuntime.SqrDistance(origin, position);
                        if (distance >= nearest) continue;
                        nearest = distance;
                        target = WorldTopologyRuntime.NormalizePosition(position);
                        guid = placement.Guid;
                        kind = AiecsFoodTarget.Flower;
                    }
                }

            float radius = Mathf.Sqrt(4f * size.x * size.x + 4f * size.y * size.y);
            items.QueryItemsInCircleNonAlloc(origin, radius, ~0, null, Candidates, Dedupe);
            foreach (Item candidate in Candidates)
            {
                if (candidate == null || candidate.DestructionHandled || !candidate.gameObject.activeInHierarchy ||
                    candidate.itemData?.Tags?.Contains(CropTag) != true ||
                    candidate.itemMods?.GetMod_ByID<Mod_Crop>(ModText.Crop) == null ||
                    !CanLand(candidate.transform.position))
                    continue;
                float distance = WorldTopologyRuntime.SqrDistance(origin, candidate.transform.position);
                if (distance >= nearest) continue;
                nearest = distance;
                target = WorldTopologyRuntime.NormalizePosition(candidate.transform.position);
                guid = candidate.itemData.Guid;
                kind = AiecsFoodTarget.Crop;
            }
            return guid != 0;
        }

        /// <summary>到达后重新检查原资源，防止地表花被采走或作物已卸载时凭空进食。</summary>
        public static bool IsStillAvailable(Vector2 position, int guid, AiecsFoodTarget kind)
        {
            if (kind == AiecsFoodTarget.Crop)
            {
                Item item = ItemMgr.Instance?.GetItemByGuid(guid);
                return item != null && !item.DestructionHandled && item.gameObject.activeInHierarchy &&
                    item.itemData?.Tags?.Contains(CropTag) == true &&
                    item.itemMods?.GetMod_ByID<Mod_Crop>(ModText.Crop) != null &&
                    WorldTopologyRuntime.SqrDistance(item.transform.position, position) <= 0.36f;
            }
            return kind == AiecsFoodTarget.Flower &&
                GroundCoverSystem.TryResolve(position, out GroundCoverTarget flower) &&
                flower.Placement.Guid == guid && flower.Definition.HasTag(FlowerTag);
        }

        private static bool CanLand(Vector2 position)
        {
            WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
            return navigation != null && navigation.TryGetCell(position, out _, out bool walkable) && walkable;
        }

        #endregion
    }
}
