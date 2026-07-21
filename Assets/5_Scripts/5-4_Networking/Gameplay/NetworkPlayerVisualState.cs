// AI-Context: Mirror 玩家视觉快照；只放高频且不适合写入 ModuleData 的短生命状态。

using System;

namespace FlatWorld.Networking.Gameplay
{
    [Serializable]
    public struct NetworkPlayerVisualState : IEquatable<NetworkPlayerVisualState>
    {
        public bool IsMoving;
        public bool IsRunning;
        public bool IsAttacking;
        public bool CanUseSkill;
        public int SkillId;
        public int AnimatorStateHash;

        public static NetworkPlayerVisualState Idle => default;

        public bool Equals(NetworkPlayerVisualState other)
        {
            return IsMoving == other.IsMoving &&
                   IsRunning == other.IsRunning &&
                   IsAttacking == other.IsAttacking &&
                   CanUseSkill == other.CanUseSkill &&
                   SkillId == other.SkillId &&
                   AnimatorStateHash == other.AnimatorStateHash;
        }

        public override bool Equals(object obj)
            => obj is NetworkPlayerVisualState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + IsMoving.GetHashCode();
                hash = hash * 31 + IsRunning.GetHashCode();
                hash = hash * 31 + IsAttacking.GetHashCode();
                hash = hash * 31 + CanUseSkill.GetHashCode();
                hash = hash * 31 + SkillId;
                hash = hash * 31 + AnimatorStateHash;
                return hash;
            }
        }
    }
}
