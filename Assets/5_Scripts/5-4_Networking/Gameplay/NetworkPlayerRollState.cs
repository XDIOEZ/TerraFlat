using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>服务器发布同一轮翻滚的动作时点，客户端只补齐剩余表现。</summary>
    [Serializable]
    public struct NetworkPlayerRollState : IEquatable<NetworkPlayerRollState>
    {
        #region 翻滚动作快照
        public uint Sequence;
        public bool Active;
        public Vector2 Direction;
        public float Elapsed;
        public float Duration;
        public float TimeScale;
        public double SampleTime;

        public float ResolveElapsed(double now)
        {
            if (!float.IsFinite(Elapsed) || !float.IsFinite(Duration) || Duration <= 0f ||
                !float.IsFinite(TimeScale) || !double.IsFinite(SampleTime) || !double.IsFinite(now))
                return 0f;
            double advance = Math.Max(0d, now - SampleTime) * Math.Max(0f, TimeScale);
            return (float)Math.Clamp(Elapsed + advance, 0d, Duration);
        }

        public bool Equals(NetworkPlayerRollState other) => Sequence == other.Sequence && Active == other.Active &&
            Direction == other.Direction && Elapsed.Equals(other.Elapsed) && Duration.Equals(other.Duration) &&
            TimeScale.Equals(other.TimeScale) && SampleTime.Equals(other.SampleTime);
        public override bool Equals(object obj) => obj is NetworkPlayerRollState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Sequence, Active, Direction, Elapsed, Duration, TimeScale, SampleTime);
        #endregion
    }

    /// <summary>翻滚体力交接完成前，旧的客户端整包不能覆盖已支付的体力。</summary>
    internal sealed class NetworkRollStaminaHandoff
    {
        #region 体力扣费回执保护
        private uint requestId;
        private string stableName;
        private string moduleId;
        private byte[] paidState;
        private float remainingStamina;
        public bool IsPending => requestId != 0;

        public bool Begin(uint request, Ex_ModData_MemoryPackable state, float remaining)
        {
            if (request == 0 || IsPending || state?.BitData == null || state.BitData.Length == 0 ||
                string.IsNullOrEmpty(state.StableName) || !float.IsFinite(remaining) || remaining < 0f)
                return false;
            requestId = request;
            stableName = state.StableName;
            moduleId = state.ModuleId;
            paidState = (byte[])state.BitData.Clone();
            remainingStamina = remaining;
            return true;
        }

        public bool ProtectIncoming(IDictionary<string, ModuleData> modules)
        {
            if (!IsPending) return true;
            if (modules == null || !modules.TryGetValue(stableName, out ModuleData state) ||
                state is not Ex_ModData_MemoryPackable incoming || incoming.ModuleId != moduleId)
                return false;
            incoming.BitData = (byte[])paidState.Clone();
            return true;
        }

        public bool Acknowledge(uint request, float appliedRemaining)
        {
            if (!IsPending || request != requestId || !float.IsFinite(appliedRemaining) ||
                appliedRemaining < 0f || appliedRemaining > remainingStamina + .0001f)
                return false;
            Clear();
            return true;
        }

        public void Clear()
        {
            requestId = 0;
            stableName = null;
            moduleId = null;
            paidState = null;
            remainingStamina = 0f;
        }
        #endregion
    }
}
