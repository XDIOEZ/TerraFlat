using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    internal sealed class ShipMassContributionPool
    {
        #region 船体载荷样本复用
        private sealed class Entry
        {
            public (string Prefix, string Identity, int OwnerId) Key;
            public int Slot, DynamicSlot = -1;
            public ulong Stamp;
            public readonly ShipMassContribution Value = new();
        }
        private readonly Dictionary<(string, string, int), Entry> entries = new();
        private readonly List<Entry> slots = new(), dynamicEntries = new();
        private List<ShipMassContribution> source;
        private ulong stamp;

        public void Clear(ShipState ship)
        {
            entries.Clear(); slots.Clear(); dynamicEntries.Clear();
            source = ship.MassContributions; source.Clear();
        }

        public void Begin(ShipState ship)
        {
            if (!ReferenceEquals(source, ship.MassContributions)) Clear(ship);
            unchecked { stamp++; }
        }
        public ShipMassContribution Add(ShipState ship, string prefix, string identity, int ownerId = 0, bool persistent = false)
        {
            var key = (prefix, identity, ownerId);
            if (!entries.TryGetValue(key, out Entry entry))
            {
                entry = new Entry { Key = key, Slot = slots.Count };
                entry.Value.SourceId = prefix == "fluid-group:"
                    ? prefix + ownerId + ":" + identity : prefix + identity;
                entry.Value.Kind = ShipMassKind.Cargo;
                entries.Add(key, entry); slots.Add(entry); source.Add(entry.Value);
                if (!persistent) { entry.DynamicSlot = dynamicEntries.Count; dynamicEntries.Add(entry); }
            }
            entry.Stamp = stamp;
            return entry.Value;
        }

        public void Remove(string prefix, string identity, int ownerId, ShipMassLedger ledger)
        {
            if (entries.TryGetValue((prefix, identity, ownerId), out Entry entry)) Remove(entry, ledger);
        }

        public void CompleteDynamicSources(ShipMassLedger ledger)
        {
            // 固定载荷常驻账本，只检查本步可能离船的人员和散物。
            for (int i = dynamicEntries.Count - 1; i >= 0; i--)
                if (dynamicEntries[i].Stamp != stamp) Remove(dynamicEntries[i], ledger);
        }

        private void Remove(Entry entry, ShipMassLedger ledger)
        {
            ledger.Remove(entry.Value.SourceId);
            entries.Remove(entry.Key);
            int last = slots.Count - 1;
            Entry moved = slots[last]; slots[entry.Slot] = moved; moved.Slot = entry.Slot;
            source[entry.Slot] = moved.Value; slots.RemoveAt(last); source.RemoveAt(last);
            if (entry.DynamicSlot < 0) return;
            last = dynamicEntries.Count - 1; moved = dynamicEntries[last];
            dynamicEntries[entry.DynamicSlot] = moved; moved.DynamicSlot = entry.DynamicSlot;
            dynamicEntries.RemoveAt(last);
        }
        #endregion
    }
}
