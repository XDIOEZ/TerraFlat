using System;
using FlatWorld.Spaceflight;
using Mirror;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkWorldPlayer
    {
        #region 世界迁移的输入代际
        [SyncVar] private bool authoritativeWorldTransferPending;
        public bool IsWorldTransferPending => authoritativeWorldTransferPending;
        public uint RealmRevision => authoritativeRealmRevision;
        public bool TryBeginWorldTransfer()
        {
            if (!isServer || authoritativeWorldTransferPending || HasServerPickupTransaction || authoritativeGroundCombatPending ||
                coreRoll?.IsRolling == true || pendingRollRequestId != 0 || pendingTeleportRequest != 0) return false;
            authoritativeWorldTransferPending = true;
            SpaceSessionForNetwork?.RemoveNetworkPassengerInput(displayName);
            return true;
        }
        public void FinishWorldTransfer(bool accepted)
        { if (isServer && !accepted) authoritativeWorldTransferPending = false; }
        public void AcknowledgeWorldTransfer(string worldKey, uint realm)
        {
            if (!isServer || realm != authoritativeRealmRevision || worldKey != authoritativeWorldKey) return;
            authoritativeWorldTransferPending = false;
        }
        private void MaintainPlayerAuthority()
        {
            if (isServer && corePlayer != null && !coreAvatarIsLocal)
                ItemMgr.Instance?.InitializeNetworkAuthorityPlayer(corePlayer);
        }
        private bool PreserveServerSimulation(ref byte[] payload)
        {
            if (!ItemNetworkStateSerialization.TryDeserializeItemData(payload, out ItemData incoming) || incoming.Guid != corePlayer?.itemData?.Guid)
                return false;
            byte[] current = ItemNetworkStateSerialization.Capture(corePlayer, true);
            if (!ItemNetworkStateSerialization.TryDeserializeItemData(current, out ItemData authority)) return false;
            foreach (Module module in corePlayer.Mods.Values)
                if (module is IPlayerStateAuthorityModule && authority.ModuleDataDic.TryGetValue(module.StableName, out ModuleData data))
                    incoming.ModuleDataDic[module.StableName] = data;
            return ItemNetworkStateSerialization.TrySerializeItemData(incoming, out payload);
        }
        private bool AcceptWorldSubmission(uint realm) => !authoritativeWorldTransferPending && realm == authoritativeRealmRevision;
        #endregion
    }
}
