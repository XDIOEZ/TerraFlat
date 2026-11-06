using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkWorldPlayer
    {
        #region 对讲机权威请求与定向接收
        private uint nextRadioRequest, latestRadioResponse;
        private readonly RadioRequestSequence radioRequestSequence = new();
        private readonly Dictionary<(int Source, int Receiver, ulong Epoch), ulong> receivedRadioSequences = new();
        private string receivedRadioWorld;
        private uint receivedRadioRealm;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterRadioBridges()
        {
            RadioTransceiverCommands.RemoteConfigurationRequested -= SendRadioConfiguration;
            RadioTransceiverCommands.RemoteConfigurationRequested += SendRadioConfiguration;
            RadioTransceiverCommands.RemoteMessageRequested -= SendRadioText;
            RadioTransceiverCommands.RemoteMessageRequested += SendRadioText;
            RadioTextDelivery.RemoteDeliveryRequested -= SendRadioDelivery;
            RadioTextDelivery.RemoteDeliveryRequested += SendRadioDelivery;
        }

        private static bool TryGetLocalRadioPlayer(Item actor, out NetworkWorldPlayer player)
        {
            player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            return NetworkClient.ready && player != null && player.isOwned && player.corePlayer == actor &&
                !player.authoritativeWorldTransferPending;
        }
        private static bool SendRadioConfiguration(Item actor, int id, bool on, int channel)
        {
            if (!TryGetLocalRadioPlayer(actor, out NetworkWorldPlayer player)) return false;
            player.CmdConfigureRadio(player.AllocateRadioRequest(), id, on, channel,
                GameplayWorldRegistry.GetItemWorldKey(actor), player.authoritativeRealmRevision);
            return true;
        }
        private static bool SendRadioText(Item actor, int id, string text)
        {
            if (!TryGetLocalRadioPlayer(actor, out NetworkWorldPlayer player)) return false;
            player.CmdSendRadioText(player.AllocateRadioRequest(), id, text,
                GameplayWorldRegistry.GetItemWorldKey(actor), player.authoritativeRealmRevision);
            return true;
        }
        private uint AllocateRadioRequest()
        {
            if (++nextRadioRequest == 0) ++nextRadioRequest;
            return nextRadioRequest;
        }

        [Command]
        private void CmdConfigureRadio(uint request, int id, bool on, int channel, string world, uint realm)
        {
            bool accepted = false;
            if (radioRequestSequence.TryAccept(request) && AcceptRadioRequestWorld(world, realm))
            {
                using var scope = NetworkGameplayWorldScope.Use(this);
                accepted = RadioTransceiverCommands.SetConfiguration(corePlayer, id, on, channel);
            }
            ReplyRadioRequest(request, id, accepted);
        }
        [Command]
        private void CmdSendRadioText(uint request, int id, string text, string world, uint realm)
        {
            bool accepted = false;
            if (radioRequestSequence.TryAccept(request) && AcceptRadioRequestWorld(world, realm))
            {
                using var scope = NetworkGameplayWorldScope.Use(this);
                accepted = RadioTransceiverCommands.TrySend(corePlayer, id, text);
            }
            ReplyRadioRequest(request, id, accepted);
        }
        private bool AcceptRadioRequestWorld(string world, uint realm) => AcceptWorldSubmission(realm) && corePlayer != null &&
            !string.IsNullOrEmpty(world) && world == authoritativeWorldKey &&
            world == GameplayWorldRegistry.GetItemWorldKey(corePlayer);

        private void ReplyRadioRequest(uint request, int id, bool accepted)
        {
            byte[] payload = null;
            if (RadioTransceiverCommands.TryFindActiveDevice(corePlayer, id, out ItemData device))
                payload = FluidTankStorage.FindBinary(device, Mod_RadioTransceiver.ModuleId)?.BitData;
            TargetRadioRequestResult(connectionToClient, request, id, accepted, payload, authoritativeWorldKey, authoritativeRealmRevision);
        }
        [TargetRpc]
        private void TargetRadioRequestResult(NetworkConnectionToClient connection, uint request, int id, bool accepted,
            byte[] payload, string world, uint realm)
        {
            if (!isOwned || realm != authoritativeRealmRevision || world != GameplayWorldRegistry.GetItemWorldKey(corePlayer) ||
                request == 0 || unchecked((int)(request - latestRadioResponse)) <= 0) return;
            latestRadioResponse = request;
            if (payload != null && RadioTransceiverCommands.TryFindActiveDevice(corePlayer, id, out ItemData device))
            {
                Ex_ModData_MemoryPackable binary = FluidTankStorage.FindBinary(device, Mod_RadioTransceiver.ModuleId);
                if (binary != null) { binary.BitData = payload; RadioTransceiverSystem.Bind(device, corePlayer); }
            }
            if (!accepted) RadioTextDelivery.ReportFeedback(corePlayer, "无线电操作失败，请检查电源和频道");
        }

        private static void SendRadioDelivery(Item receiver, string world, RadioTextMessage message)
        {
            if (!NetworkServer.active || string.IsNullOrEmpty(world)) return;
            RadioSignalService service = MachineWorld.GetRadioServiceForWorld(world);
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (connection == null || connection is LocalConnectionToClient || !connection.isReady ||
                    connection.identity == null) continue;
                NetworkWorldPlayer player = connection.identity.GetComponent<NetworkWorldPlayer>();
                if (player?.corePlayer == null || player.authoritativeWorldTransferPending ||
                    GameplayWorldRegistry.GetItemWorldKey(player.corePlayer) != world) continue;
                if (receiver != null && player.corePlayer != receiver) continue;
                if (receiver == null && (service == null || !service.IsWithinDistance(player.corePlayer.transform.position,
                    message.ReceiverPosition, RadioTextDelivery.StationListeningDistance))) continue;
                player.TargetReceiveRadio(connection, world, player.authoritativeRealmRevision, message.SourceId, message.ReceiverId,
                    message.Epoch, message.Sequence, message.Channel, message.SourcePosition, message.Sender, message.Text,
                    message.MessageId, message.ReceiverPosition);
            }
        }
        [TargetRpc]
        private void TargetReceiveRadio(NetworkConnectionToClient connection, string world, uint realm, int sourceId, int receiverId,
            ulong epoch, ulong sequence, int channel, Vector2 sourcePosition, string sender, string text, string messageId, Vector2 position)
        {
            if (!isOwned || corePlayer == null || realm != authoritativeRealmRevision ||
                world != GameplayWorldRegistry.GetItemWorldKey(corePlayer)) return;
            if (world != receivedRadioWorld || realm != receivedRadioRealm)
            { receivedRadioSequences.Clear(); receivedRadioWorld = world; receivedRadioRealm = realm; }
            var key = (sourceId, receiverId, epoch);
            if (sequence == 0 || receivedRadioSequences.TryGetValue(key, out ulong previous) && sequence <= previous) return;
            receivedRadioSequences[key] = sequence;
            RadioTextDelivery.Present(corePlayer, new RadioTextMessage(world, sourceId, receiverId, epoch, sequence,
                channel, sourcePosition, sender, text, messageId, position));
        }
        #endregion
    }
}
