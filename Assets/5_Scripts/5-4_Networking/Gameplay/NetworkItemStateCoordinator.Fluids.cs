using System.Collections.Generic;
using MemoryPack;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public struct NetworkSpacesuitHelmetRequest : NetworkMessage, INetworkWorldRequest {
        public uint RealmRevision;
        public uint WorldRevision => RealmRevision; public uint Token; public int SuitGuid; public bool On; }
    public struct NetworkSpacesuitHelmetResponse : NetworkMessage { public uint Token; public int SuitGuid; public byte[] State; }
    public struct NetworkPortableTankFillRequest : NetworkMessage, INetworkWorldRequest {
        public uint RealmRevision; public uint WorldRevision => RealmRevision;
        public uint Token; public int TankGuid; }
    public struct NetworkPortableTankFillResponse : NetworkMessage {
        public uint Token; public int TankGuid; public bool Accepted; public byte[] State; }
    public struct NetworkAtmosphereState : NetworkMessage { public string WorldKey; public byte[] State; }
    public struct NetworkPressureExplosion : NetworkMessage { public byte[] Event; }

    public sealed partial class NetworkItemStateCoordinator
    {
        #region 流体与宇航服权威同步
        private float nextAtmosphereSync;
        private uint lastHelmetReply;
        private uint lastPortableFillReply;
        private readonly HashSet<ulong> receivedPressureExplosions = new();
        private void StartFluidServerBridge()
        {
            NetworkServer.RegisterHandler<NetworkSpacesuitHelmetRequest>(NetworkGameplayWorldScope.Wrap<NetworkSpacesuitHelmetRequest>(OnSuitHelmet), true);
            NetworkServer.RegisterHandler<NetworkPortableTankFillRequest>(NetworkGameplayWorldScope.Wrap<NetworkPortableTankFillRequest>(OnPortableTankFill), true);
            PressureExplosionQueue.Presented += BroadcastPressureExplosion;
        }
        private void StopFluidServerBridge()
        {
            NetworkServer.UnregisterHandler<NetworkSpacesuitHelmetRequest>();
            NetworkServer.UnregisterHandler<NetworkPortableTankFillRequest>();
            PressureExplosionQueue.Presented -= BroadcastPressureExplosion;
        }
        private void StartFluidClientBridge()
        {
            SpacesuitCommands.RemoteHelmetRequested += SendSuitHelmet;
            PortableGasTankAirFilling.RemoteFillRequested += SendPortableTankFill;
            NetworkClient.RegisterHandler<NetworkSpacesuitHelmetResponse>(ApplySuitHelmet, true);
            NetworkClient.RegisterHandler<NetworkPortableTankFillResponse>(ApplyPortableTankFill, true);
            NetworkClient.RegisterHandler<NetworkAtmosphereState>(ApplyAtmosphere, true);
            NetworkClient.RegisterHandler<NetworkPressureExplosion>(ApplyPressureExplosion, true);
        }
        private void StopFluidClientBridge()
        {
            SpacesuitCommands.RemoteHelmetRequested -= SendSuitHelmet;
            PortableGasTankAirFilling.RemoteFillRequested -= SendPortableTankFill;
            NetworkClient.UnregisterHandler<NetworkSpacesuitHelmetResponse>();
            NetworkClient.UnregisterHandler<NetworkPortableTankFillResponse>();
            NetworkClient.UnregisterHandler<NetworkAtmosphereState>();
            NetworkClient.UnregisterHandler<NetworkPressureExplosion>();
            receivedPressureExplosions.Clear(); lastHelmetReply = 0; lastPortableFillReply = 0;
        }
        private bool SendSuitHelmet(Player actor, int guid, bool on)
        {
            if (!NetworkClient.ready || NetworkServer.active || actor == null || guid == 0) return false;
            NetworkClient.Send(new NetworkSpacesuitHelmetRequest { RealmRevision = NetworkGameplayWorldScope.LocalRevision, Token = NextMachineToken(), SuitGuid = guid, On = on });
            return true;
        }
        private void OnSuitHelmet(NetworkConnectionToClient connection, NetworkSpacesuitHelmetRequest request)
        {
            if (!AcceptMachineToken(connection, request.Token)) return;
            Player actor = connection.identity.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
            if (actor == null || actor.DestructionHandled || actor.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp <= 0f) return;
            if (!SpacesuitCommands.SetHelmet(actor, request.SuitGuid, request.On)) return;
            foreach (ItemData suit in MachineInventoryCommands.OwnedSpacesuits(actor))
                if (suit.Guid == request.SuitGuid)
                {
                    SpacesuitSystem.Flush(suit);
                    connection.Send(new NetworkSpacesuitHelmetResponse { Token = request.Token, SuitGuid = request.SuitGuid,
                        State = FluidTankStorage.FindBinary(suit, Mod_Spacesuit.ModuleId).BitData });
                    return;
                }
        }
        private void ApplySuitHelmet(NetworkSpacesuitHelmetResponse response)
        {
            if (NetworkServer.active || response.Token == 0 || unchecked((int)(response.Token - lastHelmetReply)) <= 0 ||
                response.State == null || response.State.Length > 1024 * 1024) return;
            lastHelmetReply = response.Token;
            Player actor = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
            foreach (ItemData suit in MachineInventoryCommands.OwnedSpacesuits(actor))
                if (suit.Guid == response.SuitGuid)
                {
                    FluidTankStorage.FindBinary(suit, Mod_Spacesuit.ModuleId).BitData = response.State;
                    SpacesuitSystem.GetBinding(suit, actor);
                    SpacesuitSystem.RefreshProtection(actor);
                    FluidTankStorage.NotifyOwner(actor, suit);
                    return;
                }
        }
        private bool SendPortableTankFill(Player actor, int guid)
        {
            if (!NetworkClient.ready || NetworkServer.active || actor == null || guid == 0 ||
                actor != NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.CorePlayer ||
                ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction()) return false;
            NetworkClient.Send(new NetworkPortableTankFillRequest
            { RealmRevision = NetworkGameplayWorldScope.LocalRevision, Token = NextMachineToken(), TankGuid = guid });
            return true;
        }
        private void OnPortableTankFill(NetworkConnectionToClient connection, NetworkPortableTankFillRequest request)
        {
            if (!AcceptMachineToken(connection, request.Token)) return;
            Player actor = connection.identity.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
            Item tank = ItemMgr.Instance?.GetItemByGuid(request.TankGuid);
            bool accepted = actor != null && !actor.DestructionHandled &&
                connection.identity.GetComponent<NetworkWorldPlayer>()?.HasServerPickupTransaction != true &&
                NetworkGameplayWorldScope.OwnsItem(connection, tank) &&
                PortableGasTankAirFilling.Fill(tank, actor);
            connection.Send(new NetworkPortableTankFillResponse
            {
                Token = request.Token,
                TankGuid = request.TankGuid,
                Accepted = accepted,
                State = accepted ? FluidTankStorage.FindBinary(tank.itemData, Mod_FluidTank.ModuleId)?.BitData : null
            });
        }
        private void ApplyPortableTankFill(NetworkPortableTankFillResponse response)
        {
            if (NetworkServer.active || response.Token == 0 ||
                unchecked((int)(response.Token - lastPortableFillReply)) <= 0) return;
            lastPortableFillReply = response.Token;
            if (!response.Accepted || response.State == null || response.State.Length > 1024 * 1024) return;
            Item tank = ItemMgr.Instance?.GetItemByGuid(response.TankGuid);
            if (!FluidTankStorage.AllowsManualAirFill(tank?.itemData)) return;
            Ex_ModData_MemoryPackable binary = FluidTankStorage.FindBinary(tank.itemData, Mod_FluidTank.ModuleId);
            if (binary == null) return;
            binary.BitData = response.State;
            // 下一次读取按二进制引用换代重绑，不能用客户端旧缓存 Flush 覆盖权威结果。
            tank.OnUIRefresh?.Invoke();
            if (tank.Owner != null)
            {
                if (InventoryContextResolver.TryResolveContainingInventory(tank.Owner, tank.itemData, out Inventory inventory))
                    inventory.Data.NotifyItemStateChanged(tank.itemData);
                tank.Owner.OnUIRefresh?.Invoke();
            }
        }
        private void TickFluidNetwork()
        {
            if (!NetworkServer.active || Time.unscaledTime < nextAtmosphereSync) return;
            nextAtmosphereSync = Time.unscaledTime + .5f;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (!connection.isReady || connection.identity == null) continue;
                Player actor = connection.identity.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
                if (actor == null || !AtmosphereService.TryGetForWorld(actor.Data.CurrentSceneName, out AtmosphereState atmosphere)) continue;
                connection.Send(new NetworkAtmosphereState { WorldKey = actor.Data.CurrentSceneName,
                    State = MemoryPackSerializer.Serialize(atmosphere) });
            }
        }
        private void ApplyAtmosphere(NetworkAtmosphereState message)
        {
            if (NetworkServer.active || string.IsNullOrWhiteSpace(message.WorldKey) || message.WorldKey.Length > 128 ||
                message.State == null || message.State.Length > 256 * 1024) return;
            var save = SaveDataMgr.Instance?.SaveData;
            if (save?.PlanetData_Dict == null || !save.PlanetData_Dict.TryGetValue(WorldAddress.FromWorldKey(message.WorldKey).PlanetId, out PlanetData planet)) return;
            AtmosphereState state = MemoryPackSerializer.Deserialize<AtmosphereState>(message.State);
            AtmosphereService.Validate(state, FluidCatalog.Default);
            planet.Atmosphere = state;
        }
        private void BroadcastPressureExplosion(PressureExplosionEvent entry, float radius)
        {
            if (!NetworkServer.active) return;
            var message = new NetworkPressureExplosion { Event = MemoryPackSerializer.Serialize(entry) };
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection.isReady && connection.identity != null &&
                    connection.identity.GetComponent<NetworkWorldPlayer>()?.CorePlayer?.gameObject.scene.name == (entry.PresentationWorldKey ?? entry.WorldKey)) connection.Send(message);
        }
        private void ApplyPressureExplosion(NetworkPressureExplosion message)
        {
            if (NetworkServer.active || message.Event == null || message.Event.Length > 256 * 1024) return;
            PressureExplosionEvent entry = MemoryPackSerializer.Deserialize<PressureExplosionEvent>(message.Event);
            if (entry == null || !receivedPressureExplosions.Add(entry.Id)) return;
            if (receivedPressureExplosions.Count > 4096) receivedPressureExplosions.Clear();
            // 客户端只播表现，不再释放物料或扣一次范围生命。
            PressureExplosionQueue.Present(entry);
        }
        #endregion
    }
}
