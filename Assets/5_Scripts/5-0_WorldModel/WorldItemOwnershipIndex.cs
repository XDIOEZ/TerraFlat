using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FlatWorld.WorldModel
{
    /// <summary>由世界统一维护物品集合及物品到世界的反向索引。</summary>
    public sealed class WorldItemOwnershipIndex<T> where T : class
    {
        #region 世界物品归属
        public readonly struct Ownership
        {
            public readonly string WorldKey;
            public readonly int WorldIdentity;
            public readonly ulong Revision;

            internal Ownership(string worldKey, int worldIdentity, ulong revision)
            { WorldKey = worldKey; WorldIdentity = worldIdentity; Revision = revision; }
        }

        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public bool Equals(T left, T right) => ReferenceEquals(left, right);
            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }

        private sealed class WorldItems
        {
            public readonly int Identity;
            public readonly HashSet<T> Items;
            public ulong Revision;
            public WorldItems(int identity, IEqualityComparer<T> comparer)
            { Identity = identity; Items = new HashSet<T>(comparer); }
        }

        private readonly IEqualityComparer<T> comparer = new ReferenceComparer();
        private readonly Dictionary<string, WorldItems> worlds = new(StringComparer.Ordinal);
        private readonly Dictionary<T, Ownership> owners;
        private readonly RuntimeWorldIdentityIndex identities;
        public ulong Revision { get; private set; }

        public WorldItemOwnershipIndex(RuntimeWorldIdentityIndex identities)
        {
            this.identities = identities ?? throw new ArgumentNullException(nameof(identities));
            owners = new Dictionary<T, Ownership>(comparer);
        }

        public bool TryGetOwnership(T item, out Ownership ownership)
        {
            ownership = default;
            return !ReferenceEquals(item, null) && owners.TryGetValue(item, out ownership);
        }

        public int GetWorldItemCount(string worldKey)
            => worldKey != null && worlds.TryGetValue(worldKey, out WorldItems world) ? world.Items.Count : 0;
        public ulong GetWorldOwnershipRevision(string worldKey)
            => worldKey != null && worlds.TryGetValue(worldKey, out WorldItems world) ? world.Revision : 0UL;

        // 生命周期事务只复制所属世界的成员，迁移期间不遍历会被修改的全局物品表。
        public void CopyWorldItems(string worldKey, ICollection<T> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (worldKey != null && worlds.TryGetValue(worldKey, out WorldItems world))
                foreach (T item in world.Items) destination.Add(item);
        }


        // 迁移先移出旧世界再加入新世界，版本使在途感知结果不能复用旧归属。
        public bool AssignItemToWorld(T item, string worldKey)
        {
            if (ReferenceEquals(item, null)) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(worldKey)) return DetachItem(item);
            bool hadOwner = owners.TryGetValue(item, out Ownership old);
            if (hadOwner && string.Equals(old.WorldKey, worldKey, StringComparison.Ordinal)) return false;
            unchecked { Revision++; }
            if (hadOwner) RemoveFromWorld(item, old.WorldKey);
            if (!worlds.TryGetValue(worldKey, out WorldItems world))
                worlds.Add(worldKey, world = new WorldItems(identities.GetOrCreateRuntimeWorldIdentity(worldKey), comparer));
            world.Items.Add(item);
            world.Revision = Revision;
            owners[item] = new Ownership(worldKey, world.Identity, Revision);
            return true;
        }

        public bool DetachItem(T item)
        {
            if (ReferenceEquals(item, null) || !owners.Remove(item, out Ownership old)) return false;
            unchecked { Revision++; }
            RemoveFromWorld(item, old.WorldKey);
            return true;
        }

        private void RemoveFromWorld(T item, string worldKey)
        {
            WorldItems world = worlds[worldKey];
            world.Items.Remove(item);
            world.Revision = Revision;
            if (world.Items.Count == 0) worlds.Remove(worldKey);
        }

        public void Clear()
        {
            if (owners.Count == 0) return;
            owners.Clear(); worlds.Clear();
            unchecked { Revision++; }
        }
        #endregion
    }
}
