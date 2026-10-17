using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkWorldPlayer
    {
        #region 拾取库存的临时权威交接
        private readonly HashSet<uint> serverPickupTransactions = new();

        internal bool HasServerPickupTransaction => serverPickupTransactions.Count > 0;

        internal bool SubmitPickupInventoryBaseline()
        {
            if (!isOwned || corePlayer?.IsInitialized != true || !NetworkClient.ready) return false;
            if (isServer || IsInSpace || authoritativeGroundCombatPending ||
                ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction()) return true;
            byte[] payload = ItemNetworkStateSerialization.Capture(corePlayer, true);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload)) return false;
            // 与后续请求使用同一可靠通道，先到达的基线建立本次服务端真实库存。
            CmdSubmitItemState(payload);
            return true;
        }

        internal bool BeginServerPickupTransaction(uint token)
        {
            if (!isServer || token == 0 || corePlayer == null) return false;
            if (!HasServerPickupTransaction && !IsInSpace && !authoritativeGroundCombatPending)
                ItemMgr.Instance?.InitializeNetworkAuthorityPlayer(corePlayer, surfaceCombat: true);
            if (!corePlayer.IsInitialized)
            {
                if (!IsInSpace && !authoritativeGroundCombatPending) ItemMgr.Instance?.ReleaseNetworkAuthorityPlayer(corePlayer);
                return false;
            }
            serverPickupTransactions.Add(token);
            return true;
        }

        internal void EndServerPickupTransaction(uint token)
        {
            if (!serverPickupTransactions.Remove(token) || HasServerPickupTransaction ||
                IsInSpace || authoritativeGroundCombatPending) return;
            ItemMgr.Instance?.ReleaseNetworkAuthorityPlayer(corePlayer);
        }

        internal byte[] CapturePickupInventoryState()
        {
            byte[] payload = corePlayer == null ? null : ItemNetworkStateSerialization.Capture(corePlayer, true);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload)) return System.Array.Empty<byte>();
            authoritativeItemState = payload;
            return payload;
        }

        internal bool ApplyPickupInventoryState(byte[] payload)
        {
            if (isServer) return true;
            if (!isOwned || corePlayer == null ||
                !ItemNetworkStateSerialization.TryDeserializeItemData(payload, out ItemData authority) ||
                authority.Guid != corePlayer.itemData.Guid) return false;
            byte[] current = ItemNetworkStateSerialization.Capture(corePlayer, true);
            if (!ItemNetworkStateSerialization.TryDeserializeItemData(current, out ItemData merged)) return false;
            Mod_HotBar hotbar = corePlayer.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
            int selectedIndex = hotbar?.CurrentIndex ?? -1;
            bool changed = false;
            // 只交接真实收货库存，角色环境、生命和表现继续使用当前本地状态。
            foreach (Module module in corePlayer.Mods.Values)
                if ((module is Mod_HotBar or Mod_Inventory) &&
                    authority.ModuleDataDic.TryGetValue(module.StableName, out ModuleData state))
                {
                    if (merged.ModuleDataDic.TryGetValue(module.StableName, out ModuleData previous) &&
                        ItemNetworkStateSerialization.CalculateModuleHash(previous) == ItemNetworkStateSerialization.CalculateModuleHash(state))
                        continue;
                    merged.ModuleDataDic[module.StableName] = state;
                    changed = true;
                }
            if (!changed) return true;
            if (!ItemNetworkStateSerialization.TrySerializeItemData(merged, out byte[] applied) ||
                !ItemNetworkStateSerialization.Apply(corePlayer, applied, reloadRuntimeModules: true,
                    requireMatchingGuid: true)) return false;
            if (selectedIndex >= 0) hotbar?.ApplyNetworkSelectedIndex(selectedIndex);
            lastSubmittedItemStateHash = 0;
            itemStateDirty = true;
            return true;
        }

        private void ApplyPickupNonInventoryState(byte[] payload)
        {
            if (corePlayer == null || (!IsInSpace && !authoritativeGroundCombatPending &&
                !FlatWorld.Spaceflight.SpaceSession.ClientGroundCombatPending) ||
                !ItemNetworkStateSerialization.TryDeserializeItemData(payload, out ItemData authority) ||
                authority.Guid != corePlayer.itemData.Guid) return;
            byte[] current = ItemNetworkStateSerialization.Capture(corePlayer, true);
            if (!ItemNetworkStateSerialization.TryDeserializeItemData(current, out ItemData merged)) return;
            // 等待收货只保护库存，仍及时接收服务器生命、体力和太空生存状态。
            foreach (Module module in corePlayer.Mods.Values)
                if (module is not (Mod_HotBar or Mod_Inventory) &&
                    (IsInSpace || module is Mod_DamageReceiver or Mod_Stamina or Mod_PlayerDeathState) &&
                    authority.ModuleDataDic.TryGetValue(module.StableName, out ModuleData state))
                    merged.ModuleDataDic[module.StableName] = state;
            if (ItemNetworkStateSerialization.TrySerializeItemData(merged, out byte[] applied))
                ItemNetworkStateSerialization.Apply(corePlayer, applied, reloadRuntimeModules: true, requireMatchingGuid: true);
        }
        #endregion
    }
}
