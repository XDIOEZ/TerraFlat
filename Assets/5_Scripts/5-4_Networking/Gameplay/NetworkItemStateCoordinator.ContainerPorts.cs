using System;
using MemoryPack;
using Mirror;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public struct NetworkContainerIntentRequest : NetworkMessage { public uint Token; public string IntentJson; }
    public struct NetworkContainerIntentResponse : NetworkMessage
    {
        public uint Token;
        public int Failure;
        public string ContentId;
        public string Unit;
        public int Items;
        public float Servings;
        public MachineInventoryAddress Source, Target;
        public byte[] SourceInventory, TargetInventory;
    }

    public sealed partial class NetworkItemStateCoordinator
    {
        #region 容器意图网络桥
        private uint pendingContainerIntent;
        private float containerIntentDeadline;
        private void StartContainerServerBridge() => NetworkServer.RegisterHandler<NetworkContainerIntentRequest>(OnContainerIntent, true);
        private void StopContainerServerBridge() => NetworkServer.UnregisterHandler<NetworkContainerIntentRequest>();
        private void StartContainerClientBridge()
        {
            ContainerTransferCommands.RemoteRequested += SendContainerIntent;
            NetworkClient.RegisterHandler<NetworkContainerIntentResponse>(ApplyContainerIntent, true);
        }
        private void StopContainerClientBridge()
        {
            ContainerTransferCommands.RemoteRequested -= SendContainerIntent;
            NetworkClient.UnregisterHandler<NetworkContainerIntentResponse>();
            pendingContainerIntent = 0; containerIntentDeadline = 0f;
        }
        private bool SendContainerIntent(ContainerTransferIntent intent, Player actor)
        {
            if (!NetworkClient.ready || NetworkServer.active || actor == null || actor != NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.CorePlayer ||
                pendingContainerIntent != 0 && Time.unscaledTime < containerIntentDeadline) return false;
            string json = JsonConvert.SerializeObject(intent);
            if (json.Length > 4096) return false;
            pendingContainerIntent = NextMachineToken(); containerIntentDeadline = Time.unscaledTime + 8f;
            NetworkClient.Send(new NetworkContainerIntentRequest { Token = pendingContainerIntent, IntentJson = json }); return true;
        }
        private bool CanUseContainerAddress(NetworkConnectionToClient connection, Player actor, ContainerPortAddress address)
        {
            if (address == null) return true;
            if ((address.ItemId?.Length ?? 0) > 128 || (address.PortId?.Length ?? 0) > 64 || (address.Inventory.PlayerInventory?.Length ?? 0) > 128) return false;
            int machine = address.ColdItem ? address.Inventory.MachineId : address.MachineId;
            return machine == 0 || CanOperateMachine(connection, actor, machine);
        }
        private void OnContainerIntent(NetworkConnectionToClient connection, NetworkContainerIntentRequest request)
        {
            if (!AcceptMachineToken(connection, request.Token) || string.IsNullOrEmpty(request.IntentJson) || request.IntentJson.Length > 4096) return;
            ContainerTransferIntent intent;
            try { intent = JsonConvert.DeserializeObject<ContainerTransferIntent>(request.IntentJson, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }); }
            catch (JsonException) { connection.Send(new NetworkContainerIntentResponse { Token = request.Token, Failure = (int)ContainerTransferFailure.InvalidRequest }); return; }
            Player actor = connection.identity.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
            if (intent == null || actor == null || (intent.Operation?.Length ?? 0) > 32 || (intent.ContentId?.Length ?? 0) > 128 || (intent.Tag?.Length ?? 0) > 128 ||
                !CanUseContainerAddress(connection, actor, intent.Source) || !CanUseContainerAddress(connection, actor, intent.Target))
            { connection.Send(new NetworkContainerIntentResponse { Token = request.Token, Failure = (int)ContainerTransferFailure.AccessDenied }); return; }
            var context = new ContainerTransferContext(actor, GetConnectionLogicalPosition(connection), actor.gameObject.scene.handle, ContainerAccessKind.Manual, "network-container-intent");
            ContainerTransferResult result = ContainerTransferCommands.Execute(intent, actor, context);
            var response = new NetworkContainerIntentResponse { Token = request.Token, Failure = (int)result.Failure, ContentId = result.ContentId, Unit = result.Unit, Items = result.Items, Servings = result.LiquidServings };
            SyncContainerEndpoint(connection, actor, intent.Source, out response.Source, out response.SourceInventory);
            SyncContainerEndpoint(connection, actor, intent.Target, out response.Target, out response.TargetInventory);
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(actor); connection.Send(response);
        }
        private void SyncContainerEndpoint(NetworkConnectionToClient connection, Player actor, ContainerPortAddress address, out MachineInventoryAddress inventoryAddress, out byte[] payload)
        {
            inventoryAddress = default; payload = null;
            if (address == null) return;
            int machine = address.ColdItem ? address.Inventory.MachineId : address.MachineId;
            if (machine != 0)
            { MachineEntity entity = MachineWorld.GetById(machine); if (entity != null) SendMechanicalSnapshot(connection, entity); }
            if (address.ColdItem)
            {
                inventoryAddress = address.Inventory;
                if (address.Inventory.MachineId == 0 || MachineInventoryCommands.IsPrivateInventoryAddress(address.Inventory))
                {
                    Inventory inventory = MachineInventoryCommands.Resolve(actor, address.Inventory);
                    if (inventory != null) payload = MemoryPackSerializer.Serialize(inventory.Data);
                }
            }
            else if (address.ItemGuid != 0)
            {
                Item item = ItemMgr.Instance?.GetItemByGuid(address.ItemGuid);
                if (item != null) ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
            }
        }
        private void ApplyContainerIntent(NetworkContainerIntentResponse response)
        {
            if (NetworkServer.active || response.Token != pendingContainerIntent) return;
            pendingContainerIntent = 0;
            Player actor = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
            ApplyPlayerInventory(actor, response.Source, response.SourceInventory);
            ApplyPlayerInventory(actor, response.Target, response.TargetInventory);
            ContainerTransferCommands.CompleteRemote(new ContainerTransferResult((ContainerTransferFailure)response.Failure, response.ContentId, response.Items, response.Servings, response.Unit));
        }
        #endregion
    }
}
