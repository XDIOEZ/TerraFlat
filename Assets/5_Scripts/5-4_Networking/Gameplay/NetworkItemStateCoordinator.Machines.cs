using System;
using System.Collections.Generic;
using MemoryPack;
using Newtonsoft.Json;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public struct NetworkMachineOperationRequest : NetworkMessage
    {
        public uint Token;
        public int EntityId;
        public string Operation;
        public string Argument;
    }

    public struct NetworkMachineTransferRequest : NetworkMessage
    {
        public uint Token;
        public MachineInventoryAddress Source;
        public MachineInventoryAddress Target;
        public int SourceSlot;
        public int TargetSlot;
        public int ExpectedSourceGuid;
        public string ExpectedSourceId;
        public int ExpectedTargetGuid;
        public string ExpectedTargetId;
        public string Operation;
        public int Amount;
    }

    public struct NetworkMachineTransferResponse : NetworkMessage
    {
        public uint Token;
        public bool Accepted;
        public bool OperationUpdate;
        public MachineInventoryAddress Source;
        public MachineInventoryAddress Target;
        public byte[] SourceInventory;
        public byte[] TargetInventory;
    }

    public sealed partial class NetworkItemStateCoordinator
    {
        #region 机器命令与库存事务
        private uint machineCommandToken;
        private uint pendingMachineTransfer;
        private float machineTransferDeadline;
        private uint lastMachineOperationInventory;
        private readonly Dictionary<int, (NetworkConnectionToClient Connection, uint Token)> machineTokens = new();

        private void StartMachineServerBridge()
        {
            StartContainerServerBridge();
            StartFluidServerBridge();
            NetworkServer.RegisterHandler<NetworkMachineOperationRequest>(OnMachineOperation, true);
            NetworkServer.RegisterHandler<NetworkMachineTransferRequest>(OnMachineTransfer, true);
        }

        private void StopMachineServerBridge()
        {
            StopContainerServerBridge();
            StopFluidServerBridge();
            NetworkServer.UnregisterHandler<NetworkMachineOperationRequest>();
            NetworkServer.UnregisterHandler<NetworkMachineTransferRequest>();
            machineTokens.Clear();
        }

        private void StartMachineClientBridge()
        {
            StartContainerClientBridge();
            StartFluidClientBridge();
            MachineWorld.RemoteOperationRequested += SendMachineOperation;
            MachineInventoryCommands.RemoteTransferRequested += SendMachineTransfer;
            NetworkClient.RegisterHandler<NetworkMachineTransferResponse>(ApplyMachineTransfer, true);
        }

        private void StopMachineClientBridge()
        {
            StopContainerClientBridge();
            StopFluidClientBridge();
            MachineWorld.RemoteOperationRequested -= SendMachineOperation;
            MachineInventoryCommands.RemoteTransferRequested -= SendMachineTransfer;
            NetworkClient.UnregisterHandler<NetworkMachineTransferResponse>();
            pendingMachineTransfer = 0;
            lastMachineOperationInventory = 0;
            machineTransferDeadline = 0f;
        }

        private bool SendMachineOperation(int id, string operation, string argument)
        {
            if (!NetworkClient.ready || NetworkServer.active || string.IsNullOrWhiteSpace(operation) ||
                operation.Length > 64 || (argument?.Length ?? 0) > 2048) return false;
            NetworkClient.Send(new NetworkMachineOperationRequest
            { Token = NextMachineToken(), EntityId = id, Operation = operation, Argument = argument ?? "" });
            return true;
        }

        private uint NextMachineToken()
        { machineCommandToken++; if (machineCommandToken == 0) machineCommandToken++; return machineCommandToken; }

        private bool AcceptMachineToken(NetworkConnectionToClient connection, uint token)
        {
            if (connection?.identity == null || !connection.isAuthenticated || !connection.isReady || token == 0) return false;
            if (machineTokens.TryGetValue(connection.connectionId, out var previous) &&
                ReferenceEquals(previous.Connection, connection) && unchecked((int)(token - previous.Token)) <= 0) return false;
            machineTokens[connection.connectionId] = (connection, token);
            return true;
        }

        private bool CanOperateMachine(NetworkConnectionToClient connection, Player actor, int id)
        {
            MachineEntity entity = MachineWorld.GetById(id);
            if (entity == null || actor == null || actor.DestructionHandled || actor.gameObject.scene.name != MachineWorld.WorldKey) return false;
            if (actor.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp <= 0f) return false;
            float range = actor.GetComponentInChildren<Mod_InteractSender>()?.maxInteractDistance ?? Mod_InteractSender.DefaultMaxInteractDistance;
            return WorldTopologyRuntime.Distance(GetConnectionLogicalPosition(connection), entity.Position) <= range + .05f;
        }

        private void OnMachineOperation(NetworkConnectionToClient connection, NetworkMachineOperationRequest request)
        {
            if (!AcceptMachineToken(connection, request.Token) || string.IsNullOrWhiteSpace(request.Operation) ||
                request.Operation.Length > 64 || (request.Argument?.Length ?? 0) > 2048) return;
            Player actor = connection.identity.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
            if (!CanOperateMachine(connection, actor, request.EntityId)) return;
            MachineEntity entity = MachineWorld.GetById(request.EntityId);
            bool accepted = MachineWorld.Execute(entity, request.Operation, request.Argument, actor);
            if (MachineWorld.Contains(entity)) SendMechanicalSnapshot(connection, entity);
            if (accepted && (request.Operation == "inventory.private-open" ||
                             request.Operation == "inventory.private-layout"))
            {
                string key = request.Operation == "inventory.private-layout"
                    ? MachineInventoryCommands.ReadPrivateLayoutKey(request.Argument) : request.Argument;
                Inventory privateInventory = MachineInventoryCommands.ResolvePrivate(actor, entity, key);
                if (privateInventory != null)
                    connection.Send(new NetworkMachineTransferResponse
                    {
                        Token = request.Token, Accepted = true, OperationUpdate = true,
                        Source = new MachineInventoryAddress
                        { MachineId = entity.Id, PlayerInventory = key },
                        SourceInventory = MemoryPackSerializer.Serialize(privateInventory.Data)
                    });
            }
            if (accepted && request.Operation == "vessel.fill")
            {
                var fill = JsonConvert.DeserializeObject<VesselFillRequest>(request.Argument);
                Inventory source = MachineInventoryCommands.Resolve(actor, fill.Source);
                if (source != null && fill.Source.MachineId == 0)
                    connection.Send(new NetworkMachineTransferResponse
                    {
                        Token = request.Token, Accepted = true, OperationUpdate = true,
                        Source = fill.Source, SourceInventory = MemoryPackSerializer.Serialize(source.Data)
                    });
                else if (source?.MachineOwner != null) SendMechanicalSnapshot(connection, source.MachineOwner);
            }
            if (accepted) ItemNetworkStateSerialization.NotifyRuntimeStateChanged(actor);
        }

        private bool SendMachineTransfer(Inventory source, int sourceIndex, Inventory target, int targetIndex, string operation, int amount)
        {
            if (!NetworkClient.ready || NetworkServer.active || pendingMachineTransfer != 0 && Time.unscaledTime < machineTransferDeadline) return false;
            Player actor = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
            if (!MachineInventoryCommands.TryAddress(actor, source, out var from) || !MachineInventoryCommands.TryAddress(actor, target, out var to) ||
                (uint)sourceIndex >= (uint)source.Data.itemSlots.Count) return false;
            ItemData item = source.Data.itemSlots[sourceIndex].itemData;
            if (item == null) return false;
            ItemData destination = targetIndex >= 0 && targetIndex < target.Data.itemSlots.Count ? target.Data.itemSlots[targetIndex].itemData : null;
            pendingMachineTransfer = NextMachineToken();
            machineTransferDeadline = Time.unscaledTime + 8f;
            NetworkClient.Send(new NetworkMachineTransferRequest
            {
                Token = pendingMachineTransfer, Source = from, Target = to, SourceSlot = sourceIndex, TargetSlot = targetIndex,
                ExpectedSourceGuid = item.Guid, ExpectedSourceId = item.IDName,
                ExpectedTargetGuid = destination?.Guid ?? 0, ExpectedTargetId = destination?.IDName ?? "", Operation = operation, Amount = amount
            });
            return true;
        }

        private void OnMachineTransfer(NetworkConnectionToClient connection, NetworkMachineTransferRequest request)
        {
            if (!AcceptMachineToken(connection, request.Token)) return;
            var response = new NetworkMachineTransferResponse { Token = request.Token, Source = request.Source, Target = request.Target };
            Player actor = connection.identity.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
            if (request.Source.MachineId == 0 && request.Target.MachineId == 0 &&
                !MachineInventoryCommands.IsSpacesuitAddress(request.Source) && !MachineInventoryCommands.IsSpacesuitAddress(request.Target) || actor == null ||
                request.Source.MachineId != 0 && !CanOperateMachine(connection, actor, request.Source.MachineId) ||
                request.Target.MachineId != 0 && !CanOperateMachine(connection, actor, request.Target.MachineId) ||
                (request.Source.PlayerInventory?.Length ?? 0) > 128 || (request.Target.PlayerInventory?.Length ?? 0) > 128 ||
                (request.ExpectedSourceId?.Length ?? 0) > 128 || (request.ExpectedTargetId?.Length ?? 0) > 128)
            { connection.Send(response); return; }
            Inventory source = MachineInventoryCommands.Resolve(actor, request.Source);
            Inventory target = MachineInventoryCommands.Resolve(actor, request.Target);
            if (source == null || target == null) { connection.Send(response); return; }
            ItemData from = (uint)request.SourceSlot < (uint)source.Data.itemSlots.Count ? source.Data.itemSlots[request.SourceSlot].itemData : null;
            ItemData to = (uint)request.TargetSlot < (uint)target.Data.itemSlots.Count ? target.Data.itemSlots[request.TargetSlot].itemData : null;
            bool targetValid = request.Operation == "quick" || (uint)request.TargetSlot < (uint)target.Data.itemSlots.Count &&
                (to?.Guid ?? 0) == request.ExpectedTargetGuid && (to?.IDName ?? "") == request.ExpectedTargetId;
            if (from?.Guid == request.ExpectedSourceGuid && from?.IDName == request.ExpectedSourceId && targetValid &&
                request.Amount >= 0 && request.Amount <= 1000000000)
                response.Accepted = source.ExecuteMachineTransfer(request.SourceSlot, target, request.TargetSlot, request.Operation, request.Amount);
            if (MachineInventoryCommands.IsPrivateInventoryAddress(request.Source))
                response.SourceInventory = MemoryPackSerializer.Serialize(source.Data);
            else if (request.Source.MachineId != 0) SendMechanicalSnapshot(connection, MachineWorld.GetById(request.Source.MachineId));
            else response.SourceInventory = MemoryPackSerializer.Serialize(source.Data);
            if (MachineInventoryCommands.IsPrivateInventoryAddress(request.Target))
                response.TargetInventory = MemoryPackSerializer.Serialize(target.Data);
            else if (request.Target.MachineId != 0) SendMechanicalSnapshot(connection, MachineWorld.GetById(request.Target.MachineId));
            else response.TargetInventory = MemoryPackSerializer.Serialize(target.Data);
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(actor);
            connection.Send(response);
        }

        private void ApplyMachineTransfer(NetworkMachineTransferResponse response)
        {
            if (NetworkServer.active) return;
            if (response.OperationUpdate)
            {
                if (response.Token == 0 || unchecked((int)(response.Token - lastMachineOperationInventory)) <= 0) return;
                lastMachineOperationInventory = response.Token;
            }
            else
            {
                if (response.Token != pendingMachineTransfer) return;
                pendingMachineTransfer = 0;
            }
            Player actor = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
            ApplyPlayerInventory(actor, response.Source, response.SourceInventory);
            ApplyPlayerInventory(actor, response.Target, response.TargetInventory);
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(actor);
        }

        private static void ApplyPlayerInventory(Player actor, MachineInventoryAddress address, byte[] payload)
        {
            if (address.MachineId != 0 && !MachineInventoryCommands.IsPrivateInventoryAddress(address) ||
                payload == null || payload.Length == 0 || payload.Length > 8 * 1024 * 1024) return;
            Inventory inventory = MachineInventoryCommands.Resolve(actor, address);
            if (inventory == null) return;
            MachineInventory.ApplySnapshot(inventory, MemoryPackSerializer.Deserialize<Inventory_Data>(payload));
            Mod_HotBar hotbar = actor.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
            if (hotbar != null && ReferenceEquals(hotbar.RuntimeInventory, inventory)) hotbar.RefreshUI();
        }
        #endregion
    }
}
