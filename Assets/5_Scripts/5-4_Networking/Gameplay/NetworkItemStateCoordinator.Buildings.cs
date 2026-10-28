using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Building;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkItemStateCoordinator
    {
        #region 建造与拆除事务

        private readonly NetworkBuildingRequestLedger buildingRequests = new(RejectPendingBuildingRequest);

        private static void RejectPendingBuildingRequest(NetworkBuildingRequestLedger.Request request, string reason)
        {
            if (request.Kind == NetworkBuildingRequestKind.Place)
                request.Building?.RejectNetworkPlacement(reason);
            else
                request.Building?.RejectNetworkDismantle(reason);
        }

        private bool TryBeginClientBuilding(Mod_Building building, Vector3 position)
        {
            if (!NetworkClient.active)
                return false;

            if (ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction())
            {
                building?.RejectNetworkPlacement("正在等待服务器确认拾取库存");
                return true;
            }

            Item source = building?.item;
            if (source?.itemData?.Stack == null || source.itemData.Stack.Amount < 1f ||
                NetworkClient.connection == null || !IsFinite(position))
            {
                building?.RejectNetworkPlacement("网络连接或建造材料无效");
                return true;
            }

            if (buildingRequests.Contains(building, NetworkBuildingRequestKind.Place))
                return true;

            byte[] payload = CaptureSafely(source);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
            {
                building.RejectNetworkPlacement("无法生成建造材料快照");
                return true;
            }

            uint token = buildingRequests.Add(NetworkBuildingRequestKind.Place,
                building, source.itemData.Guid, Time.unscaledTime);

            NetworkClient.Send(CreateBuildingPlaceRequest(building, position, token, payload));
            return true;
        }

        #region 联机建造请求组包
        // 所有建筑都从同一放置入口读取朝向，避免非机械建筑在组包时丢失旋转。
        internal static NetworkBuildingPlaceRequest CreateBuildingPlaceRequest(
            Mod_Building building, Vector3 position, uint requestToken, byte[] sourcePayload)
            => new()
            {
                RealmRevision = NetworkGameplayWorldScope.LocalRevision,
                RequestToken = requestToken,
                SourceItemGuid = building.item.itemData.Guid,
                ItemId = building.item.itemData.IDName,
                SourcePayload = sourcePayload,
                Position = NormalizeBuildingPosition(position),
                RotationQuarterTurns = building.PlacementQuarterTurns,
                HorizontalMirrorX = building.PlacementHorizontalMirrorX
            };
        #endregion

        private void OnServerBuildingPlaceRequest(
            NetworkConnectionToClient connection,
            NetworkBuildingPlaceRequest request)
        {
            NetworkBuildingPlaceResponse response = new NetworkBuildingPlaceResponse
            {
                RequestToken = request.RequestToken,
                SourceItemGuid = request.SourceItemGuid,
                Accepted = false,
                RemainingAmount = 0f,
                Reason = "建造请求无效"
            };

            int buildingGuid = 0;
            try
            {
                if (!TryValidateBuildingRequest(connection, request, out ItemData sourceData,
                        out ItemSlot sourceSlot, out Player actor, out string reason))
                    throw new InvalidOperationException(reason);

                Mod_HotBar hotBar = actor.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
                var placement = new BuildingPlacementRequest
                {
                    SourceData = sourceData,
                    SourceItem = hotBar.CurentSelectItem,
                    SourceInventory = hotBar.Data,
                    SourceSlot = sourceSlot,
                    Actor = actor,
                    Position = NormalizeBuildingPosition(request.Position),
                    AuthorityPosition = GetConnectionLogicalPosition(connection),
                    MaximumDistance = Mod_Building.ResolveMaxPlacementDistance(actor, Mod_InteractSender.DefaultMaxInteractDistance),
                    HorizontalMirrorX = request.HorizontalMirrorX,
                    RotationQuarterTurns = request.RotationQuarterTurns,
                    Extension = BuildingPlacementLifecycle.GetExtension(hotBar.CurentSelectItem),
                    CandidatePrepared = data =>
                    {
                        buildingGuid = data.Guid;
                        networkInstantiationsInProgress.Add(buildingGuid);
                    },
                    Publish = PublishBuildingPlacement
                };
                if (!BuildingPlacementService.TryPlace(placement, out BuildingPlacementResult result, out reason))
                    throw new InvalidOperationException(reason);
                response.RemainingAmount = result.RemainingAmount;
                response.Accepted = true;
                response.Reason = string.Empty;
            }
            catch (Exception exception)
            {
                response.Reason = LimitReason(exception.Message);
                Debug.LogWarning($"[联机建造] 服务端拒绝 {request.ItemId}：{response.Reason}");
            }
            finally
            {
                if (buildingGuid != 0) networkInstantiationsInProgress.Remove(buildingGuid);
            }

            connection?.Send(response);
        }

        // 网络层只发布权威结果，生成、扣料和回滚都由领域放置事务完成。
        private bool PublishBuildingPlacement(BuildingPlacementResult result)
        {
            if (result.TileCell.HasValue)
                return PublishTerrainPlacement(result.TileCell.Value);
            if (!string.IsNullOrWhiteSpace(result.ShipId))
                return result.PlacedData != null && GetComponent<NetworkSpaceStateCoordinator>()?.TryPublishBuildingPlacement(
                    result.ShipId, result.PlacedData.Guid) == true;
            if (result.Machine != null)
            {
                BroadcastMechanicalSnapshot(result.Machine);
                machineStateReplication.ForgetPendingNode(result.Machine.Id);
                return true;
            }
            if (result.Item != null)
            {
                Vector3 position = result.Item.transform.position;
                return PublishServerSpawn(result.Item, position, position, 0.05f, false);
            }
            throw new InvalidOperationException("当前建筑后端尚未提供网络快照发布器");
        }

        private void OnClientBuildingPlaceResponse(NetworkBuildingPlaceResponse response)
        {
            if (!buildingRequests.TryTake(response.RequestToken, NetworkBuildingRequestKind.Place,
                    out NetworkBuildingRequestLedger.Request request))
                return;
            if (request.ItemGuid != response.SourceItemGuid || request.Building == null)
                return;

            if (response.Accepted)
                request.Building.CompleteNetworkPlacement(response.RemainingAmount);
            else
                request.Building.RejectNetworkPlacement(response.Reason);
        }


        private bool TryBeginNetworkBuildingDismantle(Mod_Building building)
        {
            if (NetworkClient.active)
                return TryBeginClientBuildingDismantle(building);

            if (!NetworkServer.active)
                return false;

            Item buildingItem = building?.item;
            Item summoner = null;
            try
            {
                if (buildingItem?.itemData == null || !building.CanCommitDismantle)
                    throw new InvalidOperationException("目标不是可拆除的建筑");

                if (!building.TryCreateDismantledSummoner(out summoner, out string reason))
                    throw new InvalidOperationException(reason);

                Vector3 position = GetLogicalItemPosition(summoner);
                if (!PublishServerSpawn(summoner, position, position, 0.05f, false))
                    throw new InvalidOperationException("服务端无法发布建筑召唤器");

                building.ReleasePlacementOccupancy();
                BroadcastDespawn(buildingItem);
                ItemMgr.Instance.DespawnItem(buildingItem, saveData: false);
                building.CompleteNetworkDismantle();
            }
            catch (Exception exception)
            {
                if (summoner != null && ItemMgr.Instance != null)
                    ItemMgr.Instance.DespawnItem(summoner, saveData: false);
                building?.RejectNetworkDismantle(LimitReason(exception.Message));
            }

            return true;
        }

        /// <summary>客户端对纯数据机械发送已有建筑拆除请求，不伪造世界 Item。</summary>
        private bool TryBeginClientMechanicalDismantle(MachineEntity node)
        {
            if (!NetworkClient.active || NetworkServer.active) return false;
            if (node == null || NetworkClient.connection == null) return true;
            uint token = buildingRequests.Add(NetworkBuildingRequestKind.Dismantle,
                null, node.Id, Time.unscaledTime);
            NetworkClient.Send(new NetworkBuildingDismantleRequest
            { RealmRevision = NetworkGameplayWorldScope.LocalRevision, RequestToken = token, BuildingGuid = node.Id });
            return true;
        }

        private bool TryBeginClientBuildingDismantle(Mod_Building building)
        {
            if (!NetworkClient.active)
                return false;

            Item worldBuilding = building?.item;
            if (worldBuilding?.itemData == null || NetworkClient.connection == null ||
                !building.CanCommitDismantle)
            {
                building?.RejectNetworkDismantle("网络连接或建筑状态无效");
                return true;
            }

            if (buildingRequests.Contains(building, NetworkBuildingRequestKind.Dismantle))
                return true;

            uint token = buildingRequests.Add(NetworkBuildingRequestKind.Dismantle,
                building, worldBuilding.itemData.Guid, Time.unscaledTime);

            NetworkClient.Send(new NetworkBuildingDismantleRequest
            { RealmRevision = NetworkGameplayWorldScope.LocalRevision,
                RequestToken = token,
                BuildingGuid = worldBuilding.itemData.Guid
            });
            return true;
        }

        private void OnServerBuildingDismantleRequest(
            NetworkConnectionToClient connection,
            NetworkBuildingDismantleRequest request)
        {
            NetworkBuildingDismantleResponse response = new NetworkBuildingDismantleResponse
            {
                RequestToken = request.RequestToken,
                BuildingGuid = request.BuildingGuid,
                Accepted = false,
                Reason = "拆除请求无效"
            };

            Item summoner = null;
            try
            {
                if (connection?.identity == null || request.RequestToken == 0 || request.BuildingGuid == 0)
                    throw new InvalidOperationException("身份或建筑标识无效");

                MachineEntity mechanical = MachineWorld.GetById(request.BuildingGuid);
                if (mechanical != null)
                {
                    if (WorldTopologyRuntime.Distance(GetConnectionLogicalPosition(connection),
                            WorldTopologyRuntime.NormalizePosition(mechanical.Snapshot.transform.position)) > MaxPickupDistance)
                        throw new InvalidOperationException("机械建筑超出拆除距离");
                    if (MachineWorld.IsShipScope)
                    {
                        if (!Mod_Building.TryDismantleMechanical(mechanical, out _, out string shipReason))
                            throw new InvalidOperationException(shipReason);
                        response.Accepted = true;
                        response.Reason = string.Empty;
                        connection.Send(response);
                        return;
                    }
                    if (!Mod_Building.TryPrepareMechanicalDismantle(mechanical, out summoner, out string mechanicalReason))
                        throw new InvalidOperationException(mechanicalReason);
                    Vector3 dropPosition = GetLogicalItemPosition(summoner);
                    if (!PublishServerSpawn(summoner, dropPosition, dropPosition, 0.05f, false))
                        throw new InvalidOperationException("服务端无法发布机械召唤器");
                    MachineWorld.Remove(mechanical.Id);
                    response.Accepted = true;
                    response.Reason = string.Empty;
                    connection.Send(response);
                    return;
                }

                Item buildingItem = ItemMgr.Instance?.GetItemByGuid(request.BuildingGuid);
                Mod_Building building = buildingItem?.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
                if (building == null || !building.CanCommitDismantle || !NetworkGameplayWorldScope.OwnsItem(connection, buildingItem))
                    throw new InvalidOperationException("目标不是可拆除的建筑");

                if (WorldTopologyRuntime.Distance(GetConnectionLogicalPosition(connection), GetLogicalItemPosition(buildingItem)) >
                    MaxPickupDistance)
                {
                    throw new InvalidOperationException("目标超出拆除距离");
                }

                if (!building.TryCreateDismantledSummoner(out summoner, out string reason))
                    throw new InvalidOperationException(reason);

                Vector3 position = GetLogicalItemPosition(summoner);
                if (!PublishServerSpawn(summoner, position, position, 0.05f, false))
                    throw new InvalidOperationException("服务端无法发布建筑召唤器");

                building.ReleasePlacementOccupancy();
                BroadcastDespawn(buildingItem);
                ItemMgr.Instance.DespawnItem(buildingItem, saveData: false);
                response.Accepted = true;
                response.Reason = string.Empty;
            }
            catch (Exception exception)
            {
                response.Reason = LimitReason(exception.Message);
                if (summoner != null && ItemMgr.Instance != null)
                    ItemMgr.Instance.DespawnItem(summoner, saveData: false);
            }

            connection?.Send(response);
        }

        private void OnClientBuildingDismantleResponse(NetworkBuildingDismantleResponse response)
        {
            if (!buildingRequests.TryTake(response.RequestToken, NetworkBuildingRequestKind.Dismantle,
                    out NetworkBuildingRequestLedger.Request request))
                return;
            if (request.ItemGuid != response.BuildingGuid)
                return;

            if (response.Accepted)
                request.Building?.CompleteNetworkDismantle();
            else
            {
                if (request.Building != null) request.Building.RejectNetworkDismantle(response.Reason);
                else Debug.LogWarning("[联机机械拆除] " + response.Reason);
            }
        }


        #region 联机建造请求校验

        private static bool TryValidateBuildingRequest(
            NetworkConnectionToClient connection,
            NetworkBuildingPlaceRequest request,
            out ItemData sourceData,
            out ItemSlot authoritativeSlot,
            out Player authoritativePlayer,
            out string reason)
        {
            sourceData = null;
            authoritativeSlot = null;
            authoritativePlayer = null;
            reason = null;
            if (connection?.identity?.GetComponent<NetworkWorldPlayer>()?.HasServerPickupTransaction == true)
            {
                reason = "正在确认拾取库存";
                return false;
            }
            if (connection?.identity == null || request.RequestToken == 0 || request.SourceItemGuid == 0 ||
                string.IsNullOrWhiteSpace(request.ItemId) || request.ItemId.Length > 128 ||
                !IsFinite(request.Position))
            {
                reason = "身份、坐标或距离校验失败";
                return false;
            }

            if (!ItemNetworkStateSerialization.IsValidPayload(request.SourcePayload) ||
                !ItemNetworkStateSerialization.TryDeserializeItemData(request.SourcePayload, out sourceData) ||
                sourceData.Guid != request.SourceItemGuid ||
                !string.Equals(sourceData.IDName, request.ItemId, StringComparison.Ordinal) ||
                sourceData.Stack == null || sourceData.Stack.Amount < 1f)
            {
                reason = "材料快照无效";
                return false;
            }

            bool hasBuildingModule = false;
            if (sourceData.ModuleDataDic != null)
            {
                foreach (ModuleData moduleData in sourceData.ModuleDataDic.Values)
                {
                    if (moduleData != null && string.Equals(moduleData.ID, ModText.Building, StringComparison.Ordinal))
                    {
                        hasBuildingModule = true;
                        break;
                    }
                }
            }

            if (!hasBuildingModule)
            {
                reason = "材料不包含建筑模块";
                return false;
            }

            if (GameRes.Instance?.GetPrefab(request.ItemId) == null || ItemMgr.Instance == null)
            {
                reason = "服务器缺少建筑预制体";
                return false;
            }

            NetworkWorldPlayer networkPlayer = connection.identity.GetComponent<NetworkWorldPlayer>();
            authoritativePlayer = networkPlayer?.CorePlayer;
            Mod_HotBar hotBar = authoritativePlayer?.itemMods?
                .GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
            if (hotBar?.Data?.itemSlots == null || hotBar.CurrentIndex < 0 ||
                hotBar.CurrentIndex >= hotBar.Data.itemSlots.Count)
            {
                reason = "服务器尚未收到快捷栏状态";
                return false;
            }

            authoritativeSlot = hotBar.Data.itemSlots[hotBar.CurrentIndex];
            ItemData authoritativeData = authoritativeSlot?.itemData;
            if (authoritativeData?.Stack == null || authoritativeData.Stack.Amount < 1f ||
                authoritativeData.Guid != request.SourceItemGuid ||
                !string.Equals(authoritativeData.IDName, request.ItemId, StringComparison.Ordinal))
            {
                reason = "服务端快捷栏材料不匹配";
                return false;
            }

            if (!Mod_Building.IsValidSummonerData(authoritativeData, out reason))
                return false;

            // 客户端快照只用于请求身份校验，实际建筑数据必须来自服务端快捷栏。
            sourceData = authoritativeData;
            return true;
        }

        #endregion


        private static Vector3 NormalizeBuildingPosition(Vector3 position)
        {
            Vector3 logical = WorldTopologyRuntime.NormalizePosition(position);
            return WorldTopologyRuntime.NormalizePosition(
                new Vector3(Mathf.Floor(logical.x) + 0.5f, Mathf.Floor(logical.y) + 0.5f, 0f));
        }
        #endregion
    }
}
