using System.Collections.Generic;
using MemoryPack;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public struct NetworkSpacesuitHelmetRequest : NetworkMessage { public uint Token; public int SuitGuid; public bool On; }
    public struct NetworkSpacesuitHelmetResponse : NetworkMessage { public uint Token; public int SuitGuid; public byte[] State; }
    public struct NetworkAtmosphereState : NetworkMessage { public string WorldKey; public byte[] State; }
    public struct NetworkPressureExplosion : NetworkMessage { public byte[] Event; }

    public sealed partial class NetworkItemStateCoordinator
    {
        #region 流体与宇航服权威同步
        private float nextAtmosphereSync;
        private uint lastHelmetReply;
        private readonly HashSet<ulong> receivedPressureExplosions = new();
        private void StartFluidServerBridge()
        {
            NetworkServer.RegisterHandler<NetworkSpacesuitHelmetRequest>(OnSuitHelmet, true);
            PressureExplosionQueue.Presented += BroadcastPressureExplosion;
        }
        private void StopFluidServerBridge()
        {
            NetworkServer.UnregisterHandler<NetworkSpacesuitHelmetRequest>();
            PressureExplosionQueue.Presented -= BroadcastPressureExplosion;
        }
        private void StartFluidClientBridge()
        {
            SpacesuitCommands.RemoteHelmetRequested += SendSuitHelmet;
            NetworkClient.RegisterHandler<NetworkSpacesuitHelmetResponse>(ApplySuitHelmet, true);
            NetworkClient.RegisterHandler<NetworkAtmosphereState>(ApplyAtmosphere, true);
            NetworkClient.RegisterHandler<NetworkPressureExplosion>(ApplyPressureExplosion, true);
        }
        private void StopFluidClientBridge()
        {
            SpacesuitCommands.RemoteHelmetRequested -= SendSuitHelmet;
            NetworkClient.UnregisterHandler<NetworkSpacesuitHelmetResponse>();
            NetworkClient.UnregisterHandler<NetworkAtmosphereState>();
            NetworkClient.UnregisterHandler<NetworkPressureExplosion>();
            receivedPressureExplosions.Clear(); lastHelmetReply = 0;
        }
        private bool SendSuitHelmet(Player actor, int guid, bool on)
        {
            if (!NetworkClient.ready || NetworkServer.active || actor == null || guid == 0) return false;
            NetworkClient.Send(new NetworkSpacesuitHelmetRequest { Token = NextMachineToken(), SuitGuid = guid, On = on });
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
        private void TickFluidNetwork()
        {
            if (!NetworkServer.active || Time.unscaledTime < nextAtmosphereSync) return;
            nextAtmosphereSync = Time.unscaledTime + .5f;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (!connection.isReady || connection.identity == null) continue;
                Player actor = connection.identity.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
                if (actor == null || !AtmosphereService.TryGetForWorld(actor.gameObject.scene.name, out AtmosphereState atmosphere)) continue;
                connection.Send(new NetworkAtmosphereState { WorldKey = actor.gameObject.scene.name,
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
                    connection.identity.GetComponent<NetworkWorldPlayer>()?.CorePlayer?.gameObject.scene.name == entry.WorldKey) connection.Send(message);
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
