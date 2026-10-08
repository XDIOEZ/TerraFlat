using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Networking;
using UnityEngine;
using Building_Data = Mod_Building.Building_Data;

namespace FlatWorld.Gameplay.Building
{
    public sealed class BuildingPlacementRequest
    {
        #region 放置输入
        public ItemData SourceData;
        public Inventory_Data SourceInventory;
        public ItemSlot SourceSlot;
        public Player Actor;
        public Vector3 Position;
        public Vector3 AuthorityPosition;
        public float MaximumDistance;
        public bool HorizontalMirrorX;
        public int? RotationQuarterTurns;
        public IBuildingPlacementExtension Extension;
        public Action<ItemData> CandidatePrepared;
        public Func<BuildingPlacementResult, bool> Publish;

        public string BuildingId
        {
            get
            {
                Mod_Building.TryReadBuildingData(SourceData, out _, out Building_Data state);
                return !string.IsNullOrWhiteSpace(state?.BuildingPrefabId)
                    ? state.BuildingPrefabId : Mod_Building.GetBuildingPrefabId(SourceData?.IDName);
            }
        }
        #endregion
    }

    public sealed class BuildingPlacementResult
    {
        #region 放置结果
        public string BuildingId { get; }
        public Item Item { get; }
        public MachineEntity Machine { get; }
        public ItemData PlacedData { get; }
        public float RemainingAmount { get; internal set; }
        internal Func<bool> Rollback { get; }
        internal Action NotifyCommitted { get; }

        // 每种后端只声明自身的撤销和提交动作，事务不识别具体建筑。
        public BuildingPlacementResult(string buildingId, Func<bool> rollback, Action notifyCommitted,
            Item item = null, MachineEntity machine = null, ItemData placedData = null)
        {
            BuildingId = buildingId;
            Rollback = rollback ?? throw new ArgumentNullException(nameof(rollback));
            NotifyCommitted = notifyCommitted;
            Item = item;
            Machine = machine;
            PlacedData = placedData;
        }
        #endregion
    }

    public interface IBuildingPlacementBackend
    {
        bool CanHandle(BuildingPlacementRequest request);
        BuildingPlacementResult Place(BuildingPlacementRequest request);
    }

    public static class BuildingPlacementService
    {
        #region 后端注册
        private sealed class Registration : IDisposable
        {
            public string Id;
            public int Priority;
            public IBuildingPlacementBackend Backend;
            public Registration Previous;
            public bool Released;

            public void Dispose()
            {
                if (Released) return;
                Released = true;
                if (!backends.TryGetValue(Id, out Registration current) || !ReferenceEquals(current, this)) return;
                Registration previous = Previous;
                while (previous != null && previous.Released) previous = previous.Previous;
                if (previous == null) backends.Remove(Id);
                else backends[Id] = previous;
            }
        }

        private static readonly Dictionary<string, Registration> backends = new(StringComparer.Ordinal);
        private static bool initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { backends.Clear(); initialized = false; }

        public static IDisposable Register(string id, IBuildingPlacementBackend backend,
            int priority = 0, bool replace = false)
        {
            EnsureBuiltIns();
            if (string.IsNullOrWhiteSpace(id) || backend == null) throw new ArgumentException("建筑后端注册无效");
            backends.TryGetValue(id, out Registration previous);
            if (previous != null && !replace) throw new InvalidOperationException("建筑后端已注册：" + id);
            var registration = new Registration { Id = id, Backend = backend, Priority = priority, Previous = previous };
            backends[id] = registration;
            return registration;
        }

        private static void EnsureBuiltIns()
        {
            if (initialized) return;
            initialized = true;
            Register("tile", new TileBuildingPlacementBackend(), 200);
            Register("machine", new MachineBuildingPlacementBackend(), 100);
            Register("item", new ItemBuildingPlacementBackend(), int.MinValue);
        }

        private static IBuildingPlacementBackend ResolveBackend(BuildingPlacementRequest request)
        {
            EnsureBuiltIns();
            var candidates = new List<Registration>(backends.Values);
            candidates.Sort((a, b) => a.Priority != b.Priority
                ? b.Priority.CompareTo(a.Priority) : string.CompareOrdinal(a.Id, b.Id));
            foreach (Registration candidate in candidates)
                if (!candidate.Released && candidate.Backend.CanHandle(request)) return candidate.Backend;
            throw new InvalidOperationException("没有可用的建筑放置后端");
        }
        #endregion

        #region 统一放置事务
        public static bool TryPlace(BuildingPlacementRequest request, out BuildingPlacementResult result,
            out string reason)
        {
            result = null;
            reason = null;
            bool materialMutationStarted = false;
            float originalAmount = request?.SourceData?.Stack?.Amount ?? 0f;
            try
            {
                if (request == null || !GameNetwork.HasStateAuthority)
                    throw new InvalidOperationException("当前端没有建造权限");
                if (request.SourceInventory?.itemSlots == null || request.SourceSlot == null ||
                    !request.SourceInventory.itemSlots.Contains(request.SourceSlot) ||
                    !ReferenceEquals(request.SourceSlot.itemData, request.SourceData) || originalAmount < 1f)
                    throw new InvalidOperationException("建造材料不属于当前真实库存槽位");
                if (!Mod_Building.IsValidSummonerData(request.SourceData, out reason))
                    throw new InvalidOperationException(reason);
                if (!IsFinite(request.Position) || !IsFinite(request.AuthorityPosition) ||
                    float.IsNaN(request.MaximumDistance) || request.MaximumDistance < 0f ||
                    !Mod_Building.IsWithinPlacementDistance(request.AuthorityPosition,
                        request.Position, request.MaximumDistance))
                    throw new InvalidOperationException("建筑坐标无效或超出建造距离");
                Vector2Int cell = WorldTopologyRuntime.NormalizeCell(new Vector2Int(
                    Mathf.FloorToInt(request.Position.x), Mathf.FloorToInt(request.Position.y)));
                if (request.Extension != null && !request.Extension.ValidatePlacement(cell, out reason))
                    throw new InvalidOperationException(reason);

                result = ResolveBackend(request).Place(request);
                if (result == null) throw new InvalidOperationException("建筑后端未返回放置结果");
                // 记录修改意图，库存事件即使抛异常也能恢复已经扣除的数量。
                materialMutationStarted = true;
                if (!request.SourceInventory.TryConsumeFromSlot(request.SourceSlot, 1, out ItemData remaining))
                    throw new InvalidOperationException("建造材料库存事务提交失败");
                result.RemainingAmount = Mathf.Max(0f, remaining?.Stack?.Amount ?? 0f);
                request.Actor?.Save();
                if (request.Publish != null && !request.Publish(result))
                    throw new InvalidOperationException("无法发布建筑放置结果");
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                bool candidateRemoved = result == null;
                try { if (result != null) candidateRemoved = result.Rollback(); }
                catch (Exception rollbackException) { Debug.LogException(rollbackException); }
                if (!candidateRemoved)
                    Debug.LogError("[建筑安装] 世界候选撤销失败，保留材料扣除以免留下免费建筑");
                if (materialMutationStarted && candidateRemoved)
                {
                    try
                    {
                        if (!request.SourceInventory.TrySetSlotItemAmount(request.SourceSlot, request.SourceData, originalAmount))
                            Debug.LogError("[建筑安装] 材料回滚失败：真实库存槽位无法恢复");
                        request.Actor?.Save();
                    }
                    catch (Exception rollbackException) { Debug.LogException(rollbackException); }
                }
                result = null;
                return false;
            }

            // 成功提交后的通知异常只报告问题，不能把已广播的建筑和材料重新回滚。
            try { result.NotifyCommitted?.Invoke(); }
            catch (Exception exception) { Debug.LogException(exception); }
            try { GameplayProgressEvents.PublishBuildingPlaced(request.Actor, result.BuildingId); }
            catch (Exception exception) { Debug.LogException(exception); }
            return true;
        }

        private static bool IsFinite(Vector3 value)
            => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        #endregion
    }
}
