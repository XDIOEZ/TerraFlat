using FlatWorld.AIECS;
using FlatWorld.Combat;
using FlatWorld.Networking;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    public static partial class NaturalEntityEcsService
    {
        #region 压力爆炸正式伤害适配
        public static void ApplyPressureExplosion(string world, ulong eventId, Vector2 center, float radius, float peak)
        {
            if (!GameNetwork.HasStateAuthority || simulation == null || MachineWorld.WorldKey != world || radius <= 0f) return;
            using ResourceQuery query = QueryBounds(center, Vector2.one * radius);
            foreach (Record record in query)
            {
                float distance = WorldTopologyRuntime.Distance(center, record.Snapshot.transform.position);
                if (!record.IsValid || distance > radius || record.DimensionId != WorldEntityRuntime.DimensionId) continue;
                CombatIdentity target = ItemMgr.Instance?.User_Player != null ? GameplayCombatBridge.Identity(ItemMgr.Instance.User_Player) : default;
                var source = new CombatIdentity { Backend = CombatBackend.Environment, Value = eventId,
                    World = target.World, Dimension = target.Dimension };
                var context = new CombatDamageContext
                {
                    Attack = new CombatAttackKey { Source = source, Sequence = (uint)eventId, Pulse = 1 }, Credit = source,
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
