using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 船体身份查询索引
        private readonly StateQueryIndex<ShipState> shipQueryIndex = new(value => value.ShipId);
        private readonly StateQueryIndex<ShipFlightState> flightQueryIndex = new(value => value.ShipId);
        private readonly StateQueryIndex<ShipDeviceState> deviceQueryIndex = new(value => value.PieceId);

        private void ClearQueryIndexes()
        { shipQueryIndex.Clear(); flightQueryIndex.Clear(); deviceQueryIndex.Clear(); }

        private sealed class StateQueryIndex<T> where T : class
        {
            private readonly Func<T, string> identity;
            private readonly Dictionary<string, T> entries = new(StringComparer.Ordinal);
            private SpaceStateCollection<T> source;
            private ulong revision;
            public StateQueryIndex(Func<T, string> selector) => identity = selector;
            public T Find(SpaceStateCollection<T> values, string id)
            {
                if (values == null || id == null) return null;
                if (!ReferenceEquals(source, values) || revision != values.Revision)
                {
                    entries.Clear();
                    for (int i = 0; i < values.Count; i++)
                    {
                        T value = values[i];
                        string key = value != null ? identity(value) : null;
                        if (key != null) entries.TryAdd(key, value);
                    }
                    source = values; revision = values.Revision;
                }
                return entries.TryGetValue(id, out T result) ? result : null;
            }
            public void Clear()
            { source = null; revision = 0; entries.Clear(); }
        }
        #endregion
    }
}
