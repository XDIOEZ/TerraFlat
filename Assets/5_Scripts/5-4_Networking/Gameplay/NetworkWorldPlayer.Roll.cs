using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkWorldPlayer
    {
        #region 翻滚请求、权威结算与远端表现
        [SyncVar(hook = nameof(OnAuthoritativeRollChanged))]
        private NetworkPlayerRollState authoritativeRoll;
        private Mod_Roll coreRoll;
        private Mod_Stamina rollStamina;
        private Mod_DamageReceiver rollHealth;
        private readonly NetworkRollStaminaHandoff rollStaminaHandoff = new();
        private uint nextRollRequestId, pendingRollRequestId, lastServerRollRequestId, lastOwnedRollSequence;
        private uint serverRollSequence, publishedModuleRollSequence;
        private Mod_Roll publishedRollModule;
        private float nextRollPublishTime;
        private bool hasPendingRollResult;
        private NetworkPlayerRollState pendingRollResult;
        private float pendingRollRemainingStamina;

        private void BindNetworkRoll()
        {
            Mod_Roll current = corePlayer != null ? corePlayer.itemMods?.GetMod_ByID<Mod_Roll>("Roll") : null;
            if (current == coreRoll) return;
            UnbindNetworkRoll();
            coreRoll = current;
            if (coreRoll == null) return;
            rollStamina = corePlayer.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
            rollHealth = corePlayer.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            if (isOwned) coreRoll.NetworkRollRequest = RequestNetworkRoll;
            if (isServer && !coreAvatarIsLocal) coreRoll.EnableNetworkAuthority(true);
            if (isOwned && !isServer && authoritativeRoll.Active) lastOwnedRollSequence = authoritativeRoll.Sequence;
        }

        private void UnbindNetworkRoll(bool endSession = false)
        {
            if (coreRoll != null)
            {
                coreRoll.NetworkRollRequest = null;
                coreRoll.CancelRoll();
                coreRoll.EnableNetworkAuthority(false);
            }
            coreRoll = null;
            rollStamina = null;
            rollHealth = null;
            if (endSession)
            {
                pendingRollRequestId = 0;
                lastOwnedRollSequence = 0;
                hasPendingRollResult = false;
                rollStaminaHandoff.Clear();
            }
        }

        private bool RequestNetworkRoll(Vector2 direction)
        {
            if (!isOwned || authoritativeWorldTransferPending || coreRoll == null || coreRoll.IsRolling || coreRoll.CooldownRemaining > 0f ||
                pendingRollRequestId != 0 || !IsValidVelocity(direction) || direction.sqrMagnitude < .0001f)
                return false;
            if (isServer)
                return TryStartServerRoll(direction);
            if (!NetworkClient.ready || corePlayer == null) return false;
            byte[] baseline = ItemNetworkStateSerialization.Capture(corePlayer, true);
            if (!ItemNetworkStateSerialization.IsValidPayload(baseline)) return false;
            nextRollRequestId++;
            if (nextRollRequestId == 0) nextRollRequestId++;
            pendingRollRequestId = nextRollRequestId;
            CmdRequestRoll(pendingRollRequestId, direction.normalized, baseline, authoritativeRealmRevision);
            return true;
        }

        [Command]
        private void CmdRequestRoll(uint request, Vector2 direction, byte[] baseline, uint realmRevision)
        {
            // 旧世界的动作不能在新世界扣体力或激活无敌；拒绝回执释放客户端待确认请求。
            if (!AcceptWorldSubmission(realmRevision))
            {
                TargetRollResult(connectionToClient, request, false, authoritativeRoll, 0f);
                return;
            }
            using var worldScope = NetworkGameplayWorldScope.Use(this);
            BindNetworkRoll();
            bool fresh = request != 0 && unchecked((int)(request - lastServerRollRequestId)) > 0;
            if (fresh) lastServerRollRequestId = request;
            bool accepted = false;
            if (fresh && !rollStaminaHandoff.IsPending && corePlayer != null &&
                ItemNetworkStateSerialization.TryDeserializeItemData(baseline, out ItemData incoming) &&
                incoming is Data_Player && incoming.Guid == corePlayer.itemData.Guid && incoming.IDName == corePlayer.itemData.IDName)
            {
                ApplyItemStateOnServer(baseline);
                float previousStamina = ReadServerRollStamina();
                accepted = TryStartServerRoll(direction);
                if (accepted && !IsInSpace && !authoritativeGroundCombatPending)
                {
                    rollStamina.modData.WriteData(rollStamina.Data);
                    if (!rollStaminaHandoff.Begin(request, rollStamina.modData, rollStamina.CurrentValue))
                    {
                        coreRoll.CancelRoll();
                        rollStamina.CurrentValue = previousStamina;
                        accepted = false;
                    }
                }
            }
            if (accepted) PublishServerRoll(force: true);
            TargetRollResult(connectionToClient, request, accepted, authoritativeRoll,
                rollStamina?.Data != null ? rollStamina.CurrentValue : 0f);
        }

        private bool TryStartServerRoll(Vector2 direction)
        {
            if (!isServer || authoritativeWorldTransferPending || coreRoll == null || rollStamina == null || rollHealth == null ||
                !IsValidVelocity(direction) || direction.sqrMagnitude < .0001f) return false;
            ReadServerRollStamina();
            if (!coreAvatarIsLocal && !rollHealth.IsRuntimeLoaded)
                rollHealth.ApplyRemoteNetworkData(corePlayer, rollHealth._Data);
            return coreRoll.TryStartRoll(direction.normalized);
        }

        private float ReadServerRollStamina()
        {
            if (rollStamina == null) return 0f;
            if (!coreAvatarIsLocal && !rollStamina.IsRuntimeLoaded)
            {
                // 未加载的地表副本只恢复体力数据和依赖，不能创建远端 HUD 或设备输入。
                rollStamina.modData.ReadData(ref rollStamina.Data);
                rollStamina.BindModuleDependencies(corePlayer.itemMods);
            }
            return rollStamina.Data != null ? rollStamina.CurrentValue : 0f;
        }

        [TargetRpc]
        private void TargetRollResult(NetworkConnectionToClient target, uint request, bool accepted,
            NetworkPlayerRollState state, float remainingStamina)
        {
            if (!isOwned || request != pendingRollRequestId) return;
            if (!accepted)
            {
                pendingRollRequestId = 0;
                hasPendingRollResult = false;
                return;
            }
            pendingRollResult = state;
            pendingRollRemainingStamina = remainingStamina;
            hasPendingRollResult = true;
            BindNetworkRoll();
            ApplyPendingRollResult();
        }

        private void ApplyPendingRollResult()
        {
            if (!hasPendingRollResult || !isOwned || coreRoll == null || rollStamina?.Data == null) return;
            uint request = pendingRollRequestId;
            NetworkPlayerRollState state = pendingRollResult;
            rollStamina.CurrentValue = pendingRollRemainingStamina;
            lastOwnedRollSequence = state.Sequence;
            coreRoll.ApplyNetworkOwnedRoll(state.Direction, state.ResolveElapsed(NetworkTime.time), state.Sequence);
            pendingRollRequestId = 0;
            hasPendingRollResult = false;
            lastSubmittedItemStateHash = 0;
            itemStateDirty = true;
            CmdAcknowledgeRollStamina(request, rollStamina.CurrentValue);
        }

        [Command]
        private void CmdAcknowledgeRollStamina(uint request, float remainingStamina)
        {
            if (!rollStaminaHandoff.Acknowledge(request, remainingStamina) || rollStamina?.Data == null) return;
            // 接收端可能已降低体力上限，正式钳制后的更小值同样完成扣费交接。
            rollStamina.CurrentValue = Mathf.Min(rollStamina.CurrentValue, remainingStamina);
            rollStamina.modData.WriteData(rollStamina.Data);
        }

        private bool ProtectPendingRollStamina(ref byte[] payload)
        {
            if (!rollStaminaHandoff.IsPending) return true;
            if (!ItemNetworkStateSerialization.TryDeserializeItemData(payload, out ItemData incoming) ||
                corePlayer?.itemData == null || incoming.Guid != corePlayer.itemData.Guid ||
                !rollStaminaHandoff.ProtectIncoming(incoming.ModuleDataDic)) return false;
            return ItemNetworkStateSerialization.TrySerializeItemData(incoming, out payload);
        }

        private void UpdateNetworkRoll()
        {
            BindNetworkRoll();
            if (coreRoll == null) return;
            ApplyPendingRollResult();
            if (isServer)
            {
                if (!coreAvatarIsLocal && !coreRoll.IsRuntimeLoaded) coreRoll.AdvanceRoll(Time.deltaTime);
                PublishServerRoll();
                return;
            }
            if (isOwned)
            {
                if (authoritativeRoll.Sequence == lastOwnedRollSequence)
                {
                    if (!authoritativeRoll.Active && coreRoll.IsRolling) coreRoll.CancelRoll();
                    else if (authoritativeRoll.Active)
                        coreRoll.ApplyNetworkOwnedRoll(authoritativeRoll.Direction,
                            authoritativeRoll.ResolveElapsed(NetworkTime.time), authoritativeRoll.Sequence);
                }
                return;
            }
            float elapsed = authoritativeRoll.ResolveElapsed(NetworkTime.time);
            coreRoll.ApplyNetworkPresentation(authoritativeRoll.Active && elapsed < authoritativeRoll.Duration,
                authoritativeRoll.Direction, elapsed, authoritativeRoll.Sequence);
        }

        private void PublishServerRoll(bool force = false)
        {
            if (!isServer || coreRoll == null) return;
            if (coreRoll.IsRolling && (publishedRollModule != coreRoll || publishedModuleRollSequence != coreRoll.Sequence))
            {
                publishedRollModule = coreRoll;
                publishedModuleRollSequence = coreRoll.Sequence;
                serverRollSequence++;
                if (serverRollSequence == 0) serverRollSequence++;
            }
            bool stateChanged = authoritativeRoll.Sequence != serverRollSequence || authoritativeRoll.Active != coreRoll.IsRolling;
            if (!force && !stateChanged && (!coreRoll.IsRolling || Time.unscaledTime < nextRollPublishTime)) return;
            nextRollPublishTime = Time.unscaledTime + networkSendInterval;
            authoritativeRoll = new NetworkPlayerRollState
            {
                Sequence = serverRollSequence, Active = coreRoll.IsRolling, Direction = coreRoll.Direction,
                Elapsed = coreRoll.Elapsed, Duration = coreRoll.Duration, TimeScale = Time.timeScale, SampleTime = NetworkTime.time
            };
        }

        private void OnAuthoritativeRollChanged(NetworkPlayerRollState previous, NetworkPlayerRollState current)
        {
            if (!isServer && !isOwned) UpdateNetworkRoll();
        }
        #endregion
    }
}
