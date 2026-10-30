using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FlatWorld.Spaceflight
{
    #region 电梯线路与单一轿厢
    public enum SpaceElevatorEndpoint { Ground = 0, Station = 1 }

    [Serializable]
    public sealed class SpaceElevatorState
    {
        [Newtonsoft.Json.JsonExtensionData] public System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken> UnknownOptionalFields;
        public string ElevatorId = Guid.NewGuid().ToString("N");
        public string GroundTerminalId, StationTerminalId, StationShipId, GroundWorldKey;
        public double GroundX, GroundY;
        public SpaceElevatorEndpoint CabinEndpoint, Destination, CargoDestination;
        public bool Travelling;
        public double RemainingSeconds;
        public string Status = "轿厢停在地面";
        // 两端仓库与运输缓冲分别保存正式实例封包，乘坐预约不跨读档保留。
        public List<string> CargoSnapshots = new();
        public List<string> GroundWarehouseSnapshots = new();
        public List<string> StationWarehouseSnapshots = new();
        public bool DeliveryPending;
        [JsonIgnore] public string RiderProfileId;
    }

    public static class SpaceElevatorDomain
    {
        // 地表经度线性投影到固定赤道直径，边缘留出站台自身的安全余量。
        public static SpaceVector2 GetEquatorAnchorOffset(double normalizedX, double radiusMeters, double axisRadians)
        {
            if (!double.IsFinite(normalizedX) || !double.IsFinite(radiusMeters) || radiusMeters <= 0d ||
                !double.IsFinite(axisRadians)) throw new ArgumentOutOfRangeException(nameof(normalizedX));
            double extent = Math.Max(0d, radiusMeters - Math.Min(4d, radiusMeters * .1d));
            double distance = (Math.Max(0d, Math.Min(1d, normalizedX)) * 2d - 1d) * extent;
            return SpaceVector2.FromAngle(axisRadians) * distance;
        }

        public static bool HasStationClearance(SpaceVector2 first, SpaceVector2 second)
            => first.IsFinite && second.IsFinite && (first - second).SqrMagnitude >= 64d;

        public static bool HasStationFootprintClearance(SpaceVector2 candidate, SpaceVector2 existingAnchor,
            SpaceVector2 localMin, SpaceVector2 localMax)
            => candidate.IsFinite && existingAnchor.IsFinite && localMin.IsFinite && localMax.IsFinite &&
                (candidate.X + 4d <= existingAnchor.X + localMin.X ||
                 candidate.X - 3d >= existingAnchor.X + localMax.X ||
                 candidate.Y + 4d <= existingAnchor.Y + localMin.Y ||
                 candidate.Y - 3d >= existingAnchor.Y + localMax.Y);

        public static bool CanAccessCargo(SpaceElevatorState state, SpaceElevatorEndpoint endpoint)
            => state != null && !state.Travelling && state.CabinEndpoint == endpoint;

        public static bool TryBegin(SpaceElevatorState state, SpaceElevatorEndpoint destination,
            double travelSeconds, out string reason)
        {
            reason = null;
            if (state == null) reason = "电梯线路不存在。";
            else if (state.Travelling) reason = "轿厢正在运行。";
            else if (destination is not (SpaceElevatorEndpoint.Ground or SpaceElevatorEndpoint.Station)) reason = "电梯目的地无效。";
            else if (state.CabinEndpoint == destination) reason = "轿厢已经停在这一端。";
            else if (!double.IsFinite(travelSeconds) || travelSeconds <= 0d) reason = "电梯运行时间配置无效。";
            if (reason != null) return false;
            state.Destination = destination;
            state.RemainingSeconds = travelSeconds;
            state.Travelling = true;
            state.Status = destination == SpaceElevatorEndpoint.Station ? "正在前往空间站" : "正在返回地面";
            return true;
        }

        public static bool Step(SpaceElevatorState state, double seconds)
        {
            if (state == null || !state.Travelling || !double.IsFinite(seconds) || seconds <= 0d) return false;
            state.RemainingSeconds = Math.Max(0d, state.RemainingSeconds - seconds);
            if (state.RemainingSeconds > 0d) return false;
            state.CabinEndpoint = state.Destination;
            state.Travelling = false;
            state.Status = state.CabinEndpoint == SpaceElevatorEndpoint.Station ? "轿厢停在空间站" : "轿厢停在地面";
            return true;
        }

        public static bool HasCargo(Inventory_Data inventory)
        {
            if (inventory?.itemSlots == null) return false;
            foreach (ItemSlot slot in inventory.itemSlots)
                if (slot?.itemData?.Stack?.Amount > 0f) return true;
            return false;
        }

        public static bool TryPackCargo(Inventory_Data warehouse, Inventory_Data transit, out string reason)
        {
            reason = "请先处理上一批尚未入库的物资。";
            if (HasCargo(transit)) return false;
            if (warehouse == null) { reason = "电梯仓库不存在。"; return false; }
            return warehouse.TryTransferAllToEmptyInventory(transit, out reason);
        }

        public static bool DeliverCargo(Inventory_Data transit, Inventory_Data warehouse)
        {
            if (transit?.itemSlots == null || warehouse?.itemSlots == null || ReferenceEquals(transit, warehouse)) return false;
            for (int index = 0; index < transit.itemSlots.Count; index++)
                if (transit.itemSlots[index]?.itemData != null)
                {
                    try { TryTransferCargo(transit, index, warehouse, out _); }
                    catch (Exception error) { UnityEngine.Debug.LogException(error); }
                }
            return !HasCargo(transit);
        }

        // 转移复用库存的真实容量与接收策略，不创建物资副本。
        public static bool TryTransferCargo(Inventory_Data source, int sourceIndex, Inventory_Data destination, out string reason)
        {
            reason = "这一格没有可转移的物资。";
            if (source?.itemSlots == null || destination?.itemSlots == null || ReferenceEquals(source, destination) ||
                sourceIndex < 0 || sourceIndex >= source.itemSlots.Count) return false;
            ItemSlot slot = source.itemSlots[sourceIndex];
            if (slot?.itemData?.Stack == null || slot.Amount <= 0) return false;
            bool moved = false;
            for (int i = 0; i < destination.itemSlots.Count && slot.itemData != null; i++)
            {
                ItemSlot target = destination.itemSlots[i];
                if (target != null && source.GetTransferableQuantityTo(slot, destination, target, slot.Amount) > 0)
                    moved |= source.TransferItemQuantityTo(slot, destination, target, slot.Amount);
            }
            reason = moved ? "物资已转移。" : "目标库存没有可用容量或不接收这种物品。";
            return moved;
        }
    }
    #endregion
}
