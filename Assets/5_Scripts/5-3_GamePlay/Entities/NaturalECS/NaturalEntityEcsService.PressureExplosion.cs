using FlatWorld.AIECS;
using FlatWorld.Combat;
using FlatWorld.Networking;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    public static partial class NaturalEntityEcsService
    {
        #region 中立环境范围伤害适配
        public static void ApplyPressureExplosion(string world, ulong eventId, Vector2 center, float radius, float peak)
            => ApplyEnvironmentalDamage(world, eventId, center, radius, peak, "explosion");

        /// <summary>雷击和爆炸共享真实资源生命与防御，避开采集资格和普通攻击冷却。</summary>
        public static void ApplyEnvironmentalDamage(string world, ulong eventId, Vector2 center, float radius, float peak,
            string deathCauseId = "unknown")
        {
            if (!GameNetwork.HasStateAuthority || simulation == null || MachineWorld.WorldKey != world || eventId == 0 ||
                !float.IsFinite(radius) || !float.IsFinite(peak) || radius <= 0f || peak <= 0f) return;
            FixedString64Bytes causeId = default;
            if (causeId.CopyFrom(deathCauseId ?? string.Empty) != CopyError.None) causeId = default;
            using ResourceQuery query = QueryBounds(center, Vector2.one * radius);
            foreach (Record record in query)
            {
                float distance = Mathf.Sqrt(SqrDistanceToBounds(center, record.HitBounds));
                if (!record.IsValid || distance > radius || record.DimensionId != WorldEntityRuntime.DimensionId) continue;
                CombatIdentity target = ItemMgr.Instance?.User_Player != null ? GameplayCombatBridge.Identity(ItemMgr.Instance.User_Player) : default;
                var source = new CombatIdentity { Backend = CombatBackend.Environment, Value = eventId,
                    World = target.World, Dimension = target.Dimension };
                var context = new CombatDamageContext
                {
                    Attack = new CombatAttackKey { Source = source, Sequence = (uint)eventId, Pulse = 1 }, Credit = source,
                    DeathCauseId = causeId,
                    Damage = new float4(0, 0, 0, peak * (1f - distance / radius)), Origin = center,
                    HitPoint = (Vector2)record.Snapshot.transform.position, ResourceToolEfficiency = 1f, BuildingMultiplier = 1f,
                    Clock = new CombatClock { Tick = (ulong)Time.frameCount, Time = Time.timeAsDouble, DeltaTime = Time.deltaTime }
                };
                ApplyDamageInternal(record.Handle, context, true);
            }
        }
        #endregion
    }
}
