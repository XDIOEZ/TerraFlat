using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    internal sealed class ShipMassContributionPool
    {
        #region 船体载荷样本复用
        private sealed class Entry
        {
            public string Prefix, Identity;
            public int OwnerId;
            public readonly ShipMassContribution Value = new();
        }
        private readonly List<Entry> entries = new();
        private int cursor;

        public void Begin(ShipState ship)
        {
            cursor = 0;
            ship.MassContributions.Clear();
        }
        public ShipMassContribution Add(ShipState ship, string prefix, string identity, int ownerId = 0)
        {
            if (cursor == entries.Count) entries.Add(new Entry());
            Entry entry = entries[cursor++];
            // 稳定来源复用字符串和样本，每步仍覆盖全部实际数值。
            if (entry.Prefix != prefix || entry.Identity != identity || entry.OwnerId != ownerId)
            {
                entry.Prefix = prefix; entry.Identity = identity; entry.OwnerId = ownerId;
                entry.Value.SourceId = prefix == "fluid-group:"
                    ? prefix + ownerId + ":" + identity : prefix + identity;
            }
            ShipMassContribution value = entry.Value;
            value.SupportPieceId = null; value.Kind = ShipMassKind.Cargo;
            value.LocalX = value.LocalY = value.MassKg = value.IntrinsicInertiaKgM2 = 0d;
            ship.MassContributions.Add(value);
            return value;
        }
        #endregion
    }
}
