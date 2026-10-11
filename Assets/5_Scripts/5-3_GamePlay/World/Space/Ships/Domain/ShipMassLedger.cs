using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    internal sealed class ShipMassLedger
    {
        #region 按来源增量维护质量的一阶和二阶矩
        private sealed class Entry
        {
            public double Mass, X, Y, Inertia;
            public ulong Stamp;
            public bool Persistent;
        }
        private readonly Dictionary<object, Entry> entries = new();
        private readonly List<object> removed = new();
        private double total, sumX, sumY, originInertia;
        private ulong stamp;
        private bool changed;
        public void Begin() { unchecked { stamp++; } }
        public void Clear()
        { entries.Clear(); total = sumX = sumY = originInertia = 0d; changed = true; }
        public void Set(object source, double mass, double x, double y, double inertia, bool persistent = false)
        {
            if (source == null || !FluidUnits.IsFinite(mass) || mass < 0d || !FluidUnits.IsFinite(x) || !FluidUnits.IsFinite(y) ||
                !FluidUnits.IsFinite(inertia) || inertia < 0d) throw new InvalidOperationException("船体质量贡献必须是有限非负数。");
            if (!entries.TryGetValue(source, out Entry entry)) entries.Add(source, entry = new Entry());
            entry.Stamp = stamp; entry.Persistent = persistent;
            if (entry.Mass == mass && entry.X == x && entry.Y == y && entry.Inertia == inertia) return;
            Accumulate(entry, -1d);
            entry.Mass = mass; entry.X = x; entry.Y = y; entry.Inertia = inertia;
            Accumulate(entry, 1d); changed = true;
        }
        private void Accumulate(Entry entry, double sign)
        {
            total += sign * entry.Mass;
            sumX += sign * entry.Mass * entry.X; sumY += sign * entry.Mass * entry.Y;
            originInertia += sign * (entry.Inertia + entry.Mass * (entry.X * entry.X + entry.Y * entry.Y));
        }
        public void Commit(ShipState ship)
        {
            removed.Clear();
            foreach (var pair in entries) if (!pair.Value.Persistent && pair.Value.Stamp != stamp) removed.Add(pair.Key);
            foreach (object key in removed) { Accumulate(entries[key], -1d); entries.Remove(key); changed = true; }
            if (!changed) return;
            // 在船本地坐标算矩；微小消减误差不能产生负质量或负惯量。
            double mass = Math.Max(0d, total);
            double x = mass > 0d ? sumX / mass : ship.CenterOfMassLocalX;
            double y = mass > 0d ? sumY / mass : ship.CenterOfMassLocalY;
            double inertia = Math.Max(0d, originInertia - (mass > 0d ? (sumX * sumX + sumY * sumY) / mass : 0d));
            ShipStructureService.ApplyMassProperties(ship, mass, x, y, inertia); changed = false;
        }
        #endregion
    }
}
