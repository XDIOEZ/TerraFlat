using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using FlatWorld.Networking;
using Unity.Entities;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    public static partial class NaturalEntityEcsService
    {
        #region 树冠事件与危险坠落

        private sealed partial class Record : ICanopyFruitSink
        {
            public double CanopyStepFrom, CanopyStepTo;
            public bool CanopyLiveStep;
            public float NextCanopyPresentationTime;

            public void BeginFall(CanopyFruitRecord fruit)
            {
                Vector2 start = CrownPosition(this, fruit.Slot);
                Vector2 end = ChooseCanopyLanding(this);
                fruit.StartX = start.x; fruit.StartY = start.y;
                fruit.EndX = end.x; fruit.EndY = end.y;
                SweepCanopyFlight(this, fruit);
                MarkPresentationDirty(this);
            }

            public void Land(CanopyFruitRecord fruit)
            {
                fruit.HitConsumed = true;
                SpawnCanopyOutput(this, fruit, false);
                MarkPresentationDirty(this);
            }
        }

        private static readonly List<RaycastHit2D> canopyHits = new();
        private static readonly HashSet<Mod_DamageReceiver> canopyReceivers = new();
        private const float CanopyGrowthPresentationInterval = 0.25f;

        private static CanopyFruitState GetCanopy(Record record)
        {
            Entity entity = simulation.GetEntity(record.Handle.Id);
            return simulation.Manager.GetComponentObject<EntityCanopyFruitModule>(entity).State;
        }

        private static void AdvanceCanopy(Record record)
        {
            if (!ReadClock(out _, out double now)) return;
            CanopyFruitState state = GetCanopy(record);
            if (state.Stopped) return;
            if (!state.Initialized)
            {
                if (!simulation.TryGet(record.Handle.Id, out EntityGrowth growth) || growth.Progress < growth.MaxProgress) return;
                CanopyFruitTimeline.Initialize(state, now, unchecked((uint)record.Snapshot.Guid));
            }
            int previousNextId = state.NextId;
            record.CanopyStepFrom = state.Time;
            record.CanopyStepTo = now;
            record.CanopyLiveStep = record.CanopyWasLive && now >= state.Time && now - state.Time <= 0.25d;
            foreach (CanopyFruitRecord fruit in state.Flights) SweepCanopyFlight(record, fruit);
            record.CanopyWasLive = CanopyFruitTimeline.Advance(state, record.Profile.Canopy.Settings, now, record);

            // 坠落果仍逐帧刷新；树冠缓慢生长只低频刷新，避免静态树每帧重提整套表现。
            bool hasFlights = state.Flights.Count > 0;
            bool hasGrowingFruit = false;
            for (int index = 0; index < state.Fruits.Count; index++)
            {
                if (state.Time < state.Fruits[index].MatureAt)
                {
                    hasGrowingFruit = true;
                    break;
                }
            }

            if (hasFlights || state.NextId != previousNextId)
            {
                MarkPresentationDirty(record);
                record.NextCanopyPresentationTime = Time.time + CanopyGrowthPresentationInterval;
            }
            else if (hasGrowingFruit && Time.time >= record.NextCanopyPresentationTime)
            {
                MarkPresentationDirty(record);
                record.NextCanopyPresentationTime = Time.time + CanopyGrowthPresentationInterval;
            }
            else if (!hasGrowingFruit)
            {
                record.NextCanopyPresentationTime = 0f;
            }
        }

        private static Vector2 CrownPosition(Record record, int slot)
        {
            var configuration = record.Profile.Canopy;
            float angle = slot * Mathf.PI * 2f / configuration.Settings.MaximumCount;
            Vector2 local = configuration.CrownCenter + new Vector2(Mathf.Cos(angle) * configuration.CrownSpread.x,
                Mathf.Sin(angle) * configuration.CrownSpread.y);
            NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
            ResolveBodyVisual(record, body, out Sprite sprite, out Matrix4x4 matrix, out _, out _);
            if (configuration.UseNormalizedCrownAnchor && sprite != null)
            {
                Vector2 uv = configuration.CrownAnchorUV + new Vector2(Mathf.Cos(angle) * configuration.CrownSpreadUV.x,
                    Mathf.Sin(angle) * configuration.CrownSpreadUV.y);
                local = (Vector2.Scale(uv, sprite.rect.size) - sprite.pivot) / sprite.pixelsPerUnit;
            }
            return matrix.MultiplyPoint3x4(local);
        }

        private static double NextCanopyRandom(Record record) => CanopyFruitTimeline.Next(GetCanopy(record), 0, 1000000) / 1000000d;

        private static Vector2 ChooseCanopyLanding(Record record)
        {
            float angle = (float)NextCanopyRandom(record) * Mathf.PI * 2f;
            float radius = Mathf.Sqrt((float)NextCanopyRandom(record)) * record.Profile.Canopy.DropScatterRadius;
            NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
            return new Vector2(body.Position.x, body.Position.y) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        /// <summary>短期落果仅查询现有受击接口，历史补算不追溯伤害，也不创建果实碰撞体。</summary>
        private static void SweepCanopyFlight(Record record, CanopyFruitRecord fruit)
        {
            if (!record.CanopyLiveStep || fruit.HitConsumed || record.CanopyStepTo <= fruit.FallAt ||
                record.CanopyStepFrom >= fruit.LandAt) return;
            Vector2 logicalStart = Mod_CanopyFruit.FlightPosition(fruit, Math.Max(record.CanopyStepFrom, fruit.FallAt));
            Vector2 logicalEnd = Mod_CanopyFruit.FlightPosition(fruit, Math.Min(record.CanopyStepTo, fruit.LandAt));
            Vector2 start = WorldLocalPresentation.ProjectPosition(logicalStart);
            Vector2 end = WorldTopologyRuntime.NearestImagePosition(start, logicalEnd);
            Vector2 displacement = end - start;
            if (displacement.sqrMagnitude <= 0f) return;
            ContactFilter2D filter = new()
            {
                useTriggers = true, useLayerMask = true,
                layerMask = LayerMask.GetMask("DamageReciver"), useDepth = false
            };
            canopyHits.Clear(); canopyReceivers.Clear();
            Physics2D.CircleCast(start, record.Profile.Canopy.CollisionRadius, displacement.normalized,
                filter, canopyHits, displacement.magnitude);
            canopyHits.Sort((a, b) => a.distance.CompareTo(b.distance));
            var damage = new FallingFruitDamage(record.Profile.Canopy.BluntDamage);
            foreach (RaycastHit2D hit in canopyHits)
            {
                Mod_DamageReceiver target = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(hit.collider);
                if (target == null || !canopyReceivers.Add(target)) continue;
                double hitTime = Math.Min(record.CanopyStepTo, fruit.LandAt - 0.000001d);
                if (FallingFruitDamage.TryHit(fruit, hitTime, () => target.Hurt(damage),
                    () => NextCanopyRandom(record), record.Profile.Canopy.SplitChance)) break;
            }
        }

        private static void SpawnCanopyOutput(Record record, CanopyFruitRecord fruit, bool chopped)
        {
            string itemId = fruit.Split ? record.Profile.Canopy.SplitItemId : record.Profile.Canopy.FruitItemId;
            if (fruit.OutputAmount < 0)
            {
                float multiplier = YieldMultiplier(record, record.Profile.Canopy.FruitItemId);
                if (chopped) multiplier *= GameDifficultyService.Current.World.LootAmountMultiplier;
                fruit.OutputAmount = GameDifficultyService.ScaleRandomizedAmount(1, multiplier);
            }
            if (fruit.OutputAmount <= 0) return;
            DroppedItemService.SpawnLoot(itemId, new Vector2(fruit.EndX, fruit.EndY), fruit.OutputAmount, radius: 0f, duration: 0f);
            fruit.OutputAmount = 0; // 已交付的记录即使稍后保存或回调失败，也不再次发放。
        }

        /// <summary>先分批追平树冠事件再结束实体；保留未完成游标，砍伐只释放成熟果。</summary>
        private static bool FinishCanopy(Record record, bool chopped)
        {
            if (record.Profile.Canopy == null) return true;
            CanopyFruitState state = GetCanopy(record);
            if (state.Stopped) return true;
            record.CanopyLiveStep = false;
            double now = state.Time;
            if (ReadClock(out _, out double current)) now = current;
            if (chopped && !CanopyFruitTimeline.Advance(state, record.Profile.Canopy.Settings, now, record)) return false;
            for (int i = state.Flights.Count - 1; i >= 0; i--)
            {
                record.Land(state.Flights[i]);
                state.Flights.RemoveAt(i);
            }
            for (int i = state.Fruits.Count - 1; i >= 0; i--)
            {
                CanopyFruitRecord fruit = state.Fruits[i];
                if (chopped && CanopyFruitTimeline.CanDropOnDeath(fruit, now))
                {
                    Vector2 landing = ChooseCanopyLanding(record);
                    fruit.EndX = landing.x; fruit.EndY = landing.y;
                    SpawnCanopyOutput(record, fruit, true);
                }
                state.Fruits.RemoveAt(i);
            }
            state.Stopped = true;
            return true;
        }

        /// <summary>保存前只完成已发生的死亡与土壤提交，不推进游戏时钟或运行测试。</summary>
        public static void PrepareForCapture(NaturalEntityHandle handle)
        {
            if (!Contains(handle) || !GameNetwork.HasStateAuthority) return;
            Record record = records[handle.Id];
            CommitSoil(record);
            if (simulation.GetBody(handle.Id).Dead != 0) PublishDeath(record);
        }

        #endregion
    }
}
