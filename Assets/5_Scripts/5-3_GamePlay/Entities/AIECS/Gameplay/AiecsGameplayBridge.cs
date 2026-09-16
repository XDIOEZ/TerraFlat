using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Navigation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>
    /// 正式模拟与旧玩法的生命周期适配器：只采集少量外部玩家，负责武器 Pulse、旧接收器结算与死亡掉落。
    /// AI↔AI 不经过本桥；世界更换先销毁旧模拟，禁止让旧身份、命中或借用的 Native 数据进入新世界。
    /// </summary>
    public sealed class AiecsGameplayBridge : IDisposable, IGameplayCombatBridge, IWorldNavigationFlowSource
    {
        #region 所有权与外部代理
        private sealed class ExternalProxy
        {
            public Item Item; public DamageReceiver Receiver; public Collider2D BodyCollider, HitCollider;
            public CombatIdentity Key; public Entity Entity; public int Group;
            public string Faction; public int FactionIndex; // 只有实际身份变化时规范化字符串。
        }
        private struct CorpseRecord { public Entity Entity; public double Expires; } // 按模拟时间先进先出。
        private struct DropRecord { public string ItemId; public float2 Position; public int Remaining; } // 队列持有静态 ID，无逐掉落任务对象。
        private static uint worldSequence;
        private static readonly Dictionary<string, uint> DimensionIds = new Dictionary<string, uint>(StringComparer.Ordinal);
        private readonly List<ExternalProxy> proxies = new List<ExternalProxy>();
        private readonly Dictionary<CombatIdentity, ExternalProxy> proxyLookup = new Dictionary<CombatIdentity, ExternalProxy>();
        private readonly Dictionary<CombatIdentity, Mod_Damage> weapons = new Dictionary<CombatIdentity, Mod_Damage>();
        private readonly Dictionary<int2, int> spawnCellOccupancy = new Dictionary<int2, int>();
        private readonly List<string> factionNames = new List<string>();
        private readonly Dictionary<string, int> factionIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<CorpseRecord> corpses = new Queue<CorpseRecord>();
        private NativeList<Entity> expiredCorpses;
        private readonly Queue<DropRecord> dropQueue = new Queue<DropRecord>();
        private DropRecord activeDrop;
        private readonly Func<string, float2, bool> actorLootSpawner;
        private readonly FlowGoalHandle[] goals;
        private readonly AiecsLosBridge los;
        private AiecsLosView losView;
        private uint factionRevision;
        private ulong actorSequence;
        private ulong spawnCellTick = ulong.MaxValue;
        private readonly uint worldStamp, dimensionStamp;
        private readonly string dimensionName;
        private readonly RuntimeLootTable[] lootTables;
        public AiecsSimulation Simulation { get; private set; }
        public FlowNavigationCache Navigation { get; private set; }
        public AiecsActorTemplate[] Templates { get; private set; }
        public readonly List<string> UnsupportedBuffs = new List<string>();
        public bool PlayerParticipates { get; set; } = true;
        public int PlayerHits { get; private set; }
        public int WeaponPulses { get; private set; }
        public int WeaponCandidates { get; private set; }
        public int PlayerToEcsHits { get; private set; }
        public float PlayerDamage { get; private set; }
        public int Deaths { get; private set; }
        public int Drops { get; private set; }
        public int ActorLootSpawns { get; private set; }
        public int PendingDropRecords => dropQueue.Count + (activeDrop.Remaining > 0 ? 1 : 0);
        public long LosChunkCopies => los.ChunkCopies;
        public int SharedGoalCount => goals.Length;
        #endregion

        #region 世界接入
        /// <summary>每个配置组一个战略目标，另加一个玩家目标；实际单位多少不会增加 Goal 数量。</summary>
        public AiecsGameplayBridge(Player player, FlowNavigationCache navigation, string[] actorIds, string[] actorFactions, float senseOverride,
            bool[] fleeFromHostiles = null, Func<string, float2, bool> actorLootSpawner = null)
        {
            this.actorLootSpawner = actorLootSpawner;
            if (actorIds == null || actorFactions == null || actorIds.Length == 0 || actorIds.Length != actorFactions.Length)
                throw new ArgumentException("AIECS Actor 与阵营目录必须非空且长度一致。");
            if (fleeFromHostiles != null && fleeFromHostiles.Length != actorIds.Length)
                throw new ArgumentException("AIECS 行为策略目录必须与 Actor 目录长度一致。", nameof(fleeFromHostiles));

            Navigation = navigation; worldStamp = ++worldSequence;
            dimensionName = ChunkMgr.Instance.ResolveWorldAddress(player.transform.position).DimensionId;
            if (!DimensionIds.TryGetValue(dimensionName, out dimensionStamp))
            { dimensionStamp = (uint)DimensionIds.Count + 1; DimensionIds.Add(dimensionName, dimensionStamp); }
            for (int i = 0; i < actorFactions.Length; i++) Faction(actorFactions[i]);
            Faction(FactionRelationService.GetFactionId(player)); Faction(string.Empty);
            var definitions = new AiecsDefinition[actorIds.Length]; Templates = new AiecsActorTemplate[actorIds.Length];
            lootTables = new RuntimeLootTable[actorIds.Length];
            float extent = 0f;
            for (int i = 0; i < actorIds.Length; i++)
            {
                Templates[i] = AiecsDefinitionCompiler.Compile(actorIds[i], actorFactions[i], i, Faction(actorFactions[i]), senseOverride,
                    fleeFromHostiles != null && fleeFromHostiles[i], out definitions[i]);
                extent = math.max(extent, math.cmax(math.abs(Templates[i].Body.Perception.Center) + Templates[i].Body.Perception.Extents));
                extent = math.max(extent, math.cmax(math.abs(Templates[i].Body.Hit.Center) + Templates[i].Body.Hit.Extents));
                GameRes.Instance.TryGetLootTable(definitions[i].LootTable.ToString(), out lootTables[i]);
            }
            try
            {
                los = new AiecsLosBridge();
                Simulation = new AiecsSimulation(definitions, AiecsDefinitionCompiler.CompileBuffs(UnsupportedBuffs),
                    FactionValues(), Relations(), actorIds.Length + 1, extent, Time.timeAsDouble);
                goals = new FlowGoalHandle[actorIds.Length + 1];
                foreach (var definition in definitions)
                    foreach (var effect in definition.OnHitBuffs)
                        if (!Simulation.SupportsBuff(effect.Id)) throw new InvalidOperationException("Actor 引用了未编译 Buff：" + effect.Id);
                expiredCorpses = new NativeList<Entity>(16, Allocator.Persistent);
                float2 position = (Vector2)player.transform.position;
                for (int i = 0; i < goals.Length; i++)
                { goals[i] = navigation.CreateGoal(position); Simulation.SetGroupGoal(i, goals[i]); }
                GameplayCombatBridge.Register(this);
                AddPlayer(player, actorIds.Length);
                WorldNavigationFlowRegistry.Register(this);
            }
            catch { Dispose(); throw; }
        }

        /// <summary>为少量真实玩家维护受击/感知代理；引用只存在 Bridge 中。</summary>
        private void AddPlayer(Player player, int group)
        {
            DamageReceiver receiver = player.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
            if (receiver == null) throw new InvalidOperationException("当前玩家没有生命接收器，无法建立双向战斗入口。");
            var proxy = new ExternalProxy { Item = player, Receiver = receiver, Group = group,
                BodyCollider = player.GetComponent<Collider2D>(), HitCollider = receiver.GetComponent<Collider2D>(), Key = IdentityOf(player) };
            proxy.Faction = FactionRelationService.GetFactionId(player); proxy.FactionIndex = Faction(proxy.Faction);
            AiecsBody body = ExternalBody(proxy);
            var template = new AiecsActorTemplate { Definition = 0, Faction = proxy.FactionIndex, Body = body,
                Vital = new AiecsVital { Hp = receiver.Hp, MaxHp = receiver.MaxHp, LastDamageTime = double.NegativeInfinity, ReceivedMultiplier = 1f } };
            proxy.Entity = Simulation.Spawn(template, proxy.Key, (Vector2)player.transform.position, group, true, true);
            proxies.Add(proxy); proxyLookup.Add(proxy.Key, proxy);
        }

        /// <summary>玩家复活或对象池代际改变后重新建立代理，旧实体版本和旧命中保持失效。</summary>
        public void EnsurePlayer(Player player)
        {
            if (player == null || !player.gameObject.activeInHierarchy) return;
            CombatIdentity key = IdentityOf(player);
            if (proxyLookup.ContainsKey(key)) return;
            for (int i = proxies.Count - 1; i >= 0; i--)
            {
                if (proxies[i].Item != player) continue;
                Simulation.Despawn(proxies[i].Entity); proxyLookup.Remove(proxies[i].Key); proxies.RemoveAt(i);
            }
            AddPlayer(player, Templates.Length);
        }

        /// <summary>只在合法已加载位置创建 Entity；低血量演示同样使用真实部位生命比例。</summary>
        public bool Spawn(int templateIndex, float2 position, float healthRatio = 1f)
        {
            return Spawn(templateIndex, position, healthRatio, out _, out _);
        }

        /// <summary>正式生态生成时返回稳定身份与 Entity 句柄，供批量计数和远距离回收使用。</summary>
        public bool Spawn(int templateIndex, float2 position, float healthRatio, out Entity entity, out CombatIdentity identity)
        {
            entity = Entity.Null;
            identity = default;
            if ((uint)templateIndex >= (uint)Templates.Length)
                return false;

            AiecsActorTemplate template = Templates[templateIndex]; var view = Navigation.Read();
            position = view.Domain.Normalize(position);
            if (!view.CanOccupy(position, template.Body.Radius)) return false;
            int2 spawnCell = view.Domain.Normalize((int2)math.floor(position));
            EnsureSpawnCellSnapshot(view.Domain);
            if (!TryAddSpawnCell(spawnCell)) return false;
            template.Vital.Hp *= healthRatio;
            for (int i = 0; i < template.Anatomy.Parts.Length; i++)
            { var part = template.Anatomy.Parts[i]; part.Hp *= healthRatio; template.Anatomy.Parts[i] = part; }
            identity = new CombatIdentity { Backend = CombatBackend.Entity, Value = ++actorSequence, Generation = 1, World = worldStamp, Dimension = dimensionStamp };
            try { entity = Simulation.Spawn(template, identity, position, templateIndex); }
            catch { RemoveSpawnCell(spawnCell); throw; }
            return true;
        }

        /// <summary>同一模拟 Tick 首次生成前按真实 ECS 位置重建容量表，后续批量生成只做 O(1) 计数。</summary>
        private void EnsureSpawnCellSnapshot(WorldTopologyDomain domain)
        {
            if (spawnCellTick == Simulation.Clock.Tick) return;
            spawnCellOccupancy.Clear();
            AiecsSpatialView spatial = Simulation.Spatial;
            if (spatial.Samples.IsCreated)
            {
                for (int i = 0; i < spatial.Samples.Length; i++)
                {
                    AiecsTargetSample sample = spatial.Samples[i];
                    if (sample.Dead != 0 || sample.Identity.External != 0) continue;
                    AddSpawnCell(domain.Normalize((int2)math.floor(sample.Position)));
                }
            }
            spawnCellTick = Simulation.Clock.Tick;
        }

        /// <summary>达到当前每格容量后拒绝继续出生；调用方可像现有开发入口一样尝试附近格。</summary>
        private bool TryAddSpawnCell(int2 cell)
        {
            spawnCellOccupancy.TryGetValue(cell, out int count);
            if (count >= Simulation.CellCapacity) return false;
            spawnCellOccupancy[cell] = count + 1;
            return true;
        }

        private void AddSpawnCell(int2 cell)
        {
            spawnCellOccupancy.TryGetValue(cell, out int count);
            spawnCellOccupancy[cell] = count + 1;
        }

        private void RemoveSpawnCell(int2 cell)
        {
            if (!spawnCellOccupancy.TryGetValue(cell, out int count)) return;
            if (count <= 1) spawnCellOccupancy.Remove(cell);
            else spawnCellOccupancy[cell] = count - 1;
        }

        /// <summary>玩家维度或导航缓存更换时必须结束整个旧模拟。</summary>
        public bool IsCurrentWorld(Player player) => player != null && Navigation != null && WorldNavigationManager.ExistingInstance != null &&
            WorldNavigationManager.ExistingInstance.OwnsSharedNavigation(Navigation) &&
            ChunkMgr.ExistingInstance != null && ChunkMgr.ExistingInstance.ResolveWorldAddress(player.transform.position).DimensionId == dimensionName;

        /// <summary>只暴露真实外部玩家代理的共享目标，复活代际、死亡和世界切换后旧目标不可观察。</summary>
        public bool TryGetPlayerFlow(Player player, out FlowNavigationCache cache, out FlowGoalHandle goal)
        {
            cache = null;
            goal = default;
            if (Simulation == null || player == null || player.itemData == null ||
                !player.gameObject.activeInHierarchy || !IsCurrentWorld(player) ||
                !proxyLookup.TryGetValue(IdentityOf(player), out ExternalProxy proxy) || proxy.Item != player ||
                proxy.Receiver == null || proxy.Receiver.Hp <= 0f)
                return false;

            goal = goals[proxy.Group];
            if (!Navigation.IsValid(goal))
                return false;

            cache = Navigation;
            return true;
        }

        /// <summary>每个模拟 Tick 同步外部代理、少量战略中心、地图版本和事件，不逐个读取 AI GameObject。</summary>
        public void Step(float deltaTime, double time)
        {
            Simulation.Complete();
            for (int i = proxies.Count - 1; i >= 0; i--)
            {
                var proxy = proxies[i];
                if (proxy.Item == null || proxy.Receiver == null || IdentityOf(proxy.Item) != proxy.Key || !proxy.Item.gameObject.activeInHierarchy)
                { Simulation.Despawn(proxy.Entity); proxyLookup.Remove(proxy.Key); proxies.RemoveAt(i); continue; }
                string faction = FactionRelationService.GetFactionId(proxy.Item);
                if (!string.Equals(faction, proxy.Faction, StringComparison.Ordinal))
                { proxy.Faction = faction; proxy.FactionIndex = Faction(faction); }
                Simulation.UpdateExternal(proxy.Entity, proxy.Key, (Vector2)proxy.Item.transform.position, ExternalBody(proxy),
                    proxy.Receiver.Hp, proxy.Receiver.MaxHp, proxy.FactionIndex, PlayerParticipates && proxy.HitCollider.enabled);
                Navigation.UpdateGoal(goals[proxy.Group], (Vector2)proxy.Item.transform.position);
            }
            if (factionRevision != FactionRelationService.Revision) RefreshFactions();
            var centers = Simulation.GroupPositions;
            for (int i = 0; i < Templates.Length; i++) if (centers[i].z > 0f) Navigation.UpdateGoal(goals[i], centers[i].xy);
            losView = los.Read(Navigation.Read()); Simulation.Difficulty = GameplayCombatBridge.Difficulty();
            Simulation.Step(Navigation, losView, deltaTime, time);
            Publish();
            PublishDrops(64);
            expiredCorpses.Clear();
            while (corpses.Count > 0 && corpses.Peek().Expires <= time)
            {
                expiredCorpses.Add(corpses.Dequeue().Entity);
            }
            Simulation.DespawnBatch(expiredCorpses.AsArray());
        }
        #endregion

        #region 身份与武器输入
        /// <summary>运行身份组合 Item UID、真实重载代际和当前世界/维度。</summary>
        private CombatIdentity IdentityOf(Item item) => new CombatIdentity { Backend = CombatBackend.External,
            Value = unchecked((uint)item.itemData.Guid), Generation = item.RuntimeGeneration, World = worldStamp, Dimension = dimensionStamp };

        /// <summary>当前世界中的真实玩家和武器取得相同世界域，不能拿旧世界事件伤害新实例。</summary>
        public bool TryGetIdentity(Item item, out CombatIdentity identity)
        {
            identity = default;
            if (item?.itemData == null || ChunkMgr.Instance == null ||
                ChunkMgr.Instance.ResolveWorldAddress(item.transform.position).DimensionId != dimensionName) return false;
            identity = IdentityOf(item); return true;
        }

        /// <summary>实际窗口才查询稀疏桶；OBB 精筛与旧目标共同预约武器的 MaxAttackTargets。</summary>
        public void QueryWeaponPulse(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context)
        {
            if (Simulation == null || !GameDifficultyService.IsPlayer(weapon.item) || context.Attack.Source.World != worldStamp) return;
            var view = Simulation.Spatial;
            if (!view.Samples.IsCreated) return;
            // 导航可能在模拟与武器 Update 之间发布新表；实际 Pulse 重新借用当前 LOS 索引。
            losView = los.Read(Navigation.Read());
            foreach (var effect in context.OnHitBuffs)
                if (!Simulation.SupportsBuff(effect.Id)) throw new InvalidOperationException("武器引用了尚未迁移的 ECS Buff：" + effect.Id);
            int sourceFaction = Faction(context.Faction.ToString()); context.Faction = factionNames[sourceFaction];
            // 新阵营注册可能替换矩阵，因此重新取得视图。
            view = Simulation.Spatial;
            WeaponPulses++; weapons[context.Attack.Source] = weapon;
            view.BucketRange(shape.Center, shape.BoundsExtents + view.MaximumBodyExtent, out int2 first, out int2 size);
            for (int y = 0; y < size.y && weapon.RemainingAttackTargets > 0; y++)
            for (int x = 0; x < size.x && weapon.RemainingAttackTargets > 0; x++)
            {
                int2 bucket = view.NormalizeBucket(first + new int2(x, y));
                for (int faction = 0; faction < factionNames.Count && weapon.RemainingAttackTargets > 0; faction++)
                {
                    if (!view.IsHostile(sourceFaction, faction) || !view.Buckets.TryGetFirstValue(new int3(bucket, faction), out int index, out var iterator)) continue;
                    do
                    {
                        WeaponCandidates++;
                        var target = view.Samples[index];
                        if (target.Identity.External != 0 || target.Dead != 0 || target.Identity.Key.World != worldStamp ||
                            !shape.Intersects(target.ShapeNear(shape.Center, view.Domain, true)) || !losView.Visible(shape.Center, target.Position)) continue;
                        if (!Simulation.Entities.Exists(target.Entity) || Simulation.Entities.GetComponentData<AiecsVital>(target.Entity).Dead != 0 ||
                            !weapon.TryReserveExternalTarget(target.Identity.Key)) continue;
                        var hit = context; hit.HitPoint = target.Position;
                        Simulation.SubmitHit(new AiecsHitEvent { Target = target.Entity, TargetKey = target.Identity.Key, Context = hit });
                    } while (weapon.RemainingAttackTargets > 0 && view.Buckets.TryGetNextValue(out index, ref iterator));
                }
            }
        }

        /// <summary>只有玩家 Bridge 采集 Collider 形状；Native AI 的任何阶段均没有物理对象。</summary>
        private static AiecsBody ExternalBody(ExternalProxy proxy)
        {
            float2 position = (Vector2)proxy.Item.transform.position;
            PerceptionShape2D perception = ColliderShape(proxy.BodyCollider, position);
            return new AiecsBody { Perception = perception, Hit = ColliderShape(proxy.HitCollider, position),
                Radius = perception.IsCircle != 0 ? perception.Radius : math.cmax(perception.Extents), Facing = new float2(1, 0) };
        }

        /// <summary>将实际外部体型冻结为局部圆/AABB，查询时再映射到目标最近镜像。</summary>
        private static PerceptionShape2D ColliderShape(Collider2D collider, float2 position)
        {
            if (collider == null) throw new InvalidOperationException("玩家代理缺少真实 Collider 形状。");
            if (collider is CircleCollider2D circle)
                return PerceptionShape2D.Circle((float2)(Vector2)circle.transform.TransformPoint(circle.offset) - position,
                    circle.radius * Mathf.Max(Mathf.Abs(circle.transform.lossyScale.x), Mathf.Abs(circle.transform.lossyScale.y)));
            if (collider is BoxCollider2D box)
            {
                float2 x = (Vector2)box.transform.TransformVector(Vector3.right);
                float2 y = (Vector2)box.transform.TransformVector(Vector3.up);
                return PerceptionShape2D.Aabb((float2)(Vector2)box.transform.TransformPoint(box.offset) - position,
                    (math.abs(x) * box.size.x + math.abs(y) * box.size.y) * 0.5f);
            }
            Bounds bounds = collider.bounds;
            return PerceptionShape2D.Aabb((float2)(Vector2)bounds.center - position, (Vector2)bounds.extents);
        }
        #endregion

        #region 结果发布与阵营表
        /// <summary>外部生命与死亡掉落各走原权威接口；不重新判断 ECS 死亡或直接写玩家 Hp。</summary>
        private void Publish()
        {
            foreach (var hit in Simulation.ExternalHits)
            {
                if (!proxyLookup.TryGetValue(hit.TargetKey, out var proxy) || proxy.Item == null || IdentityOf(proxy.Item) != hit.TargetKey) continue;
                float damage = proxy.Receiver.Hurt(hit.Context);
                if (damage >= 0f) { PlayerHits++; PlayerDamage += damage; }
            }
            foreach (var result in Simulation.DamageResults)
            {
                if (!weapons.TryGetValue(result.Context.Attack.Source, out var weapon) || weapon == null) continue;
                if (result.Loss >= 0f) PlayerToEcsHits++;
                weapon.PublishExternalDamage(result.Context, result.Loss);
            }
            foreach (var death in Simulation.DeathEvents)
            {
                Deaths++;
                // 死亡旗标已由核心锁存；先登记尸体回收，掉落反馈异常不能留下永生尸体。
                corpses.Enqueue(new CorpseRecord { Entity = death.Entity, Expires = death.Clock.Time + 2f });
                RuntimeLootTable table = lootTables[death.Definition];
                if (table == null) continue;
                foreach (var entry in table.Entries)
                {
                    if (UnityEngine.Random.value > entry.Probability) continue;
                    int count = GameDifficultyService.ScaleRandomizedAmount(UnityEngine.Random.Range(entry.MinAmount, entry.MaxAmount + 1), GameDifficultyService.Current.World.LootAmountMultiplier);
                    if (count > 0) QueueLoot(entry.ItemId, death.Position, count);
                }
            }
        }

        /// <summary>游戏事件与死亡表共用的预算交付入口；只登记权威数量，不绕过生成、占格和后端规则。</summary>
        public void QueueLoot(string itemId, float2 position, int amount)
        {
            if (amount <= 0 || !math.all(math.isfinite(position)))
                throw new ArgumentException("战利品请求必须具有正整数数量与有效位置。");
            if (!GameRes.Instance.TryGetItemDefinition(itemId, out _))
                throw new InvalidOperationException("战利品引用了缺失定义：" + itemId);
            dropQueue.Enqueue(new DropRecord { ItemId = itemId, Position = position, Remaining = amount });
        }

        /// <summary>死亡先结算一次，掉落实体按预算发布；不为每份战利品创建 Item 外壳。</summary>
        private void PublishDrops(int budget)
        {
            while (budget-- > 0 && (activeDrop.Remaining > 0 || dropQueue.Count > 0))
            {
                if (activeDrop.Remaining == 0) activeDrop = dropQueue.Dequeue();
                if (!GameRes.Instance.TryGetItemDefinition(activeDrop.ItemId, out RuntimeItemDefinition definition))
                    throw new InvalidOperationException("战利品引用了缺失定义：" + activeDrop.ItemId);
                if (definition.IsActor)
                {
                    bool spawnedActor = actorLootSpawner != null
                        ? actorLootSpawner(activeDrop.ItemId, activeDrop.Position)
                        : DroppedItemService.TrySpawnLootActor(activeDrop.ItemId, (Vector2)activeDrop.Position);
                    if (!spawnedActor)
                    {
                        // 拥挤/窗口暂未就绪是可重试结果。转到队尾，不扣数量、不阻断整个模拟。
                        dropQueue.Enqueue(activeDrop);
                        activeDrop = default;
                        break;
                    }
                    ActorLootSpawns++;
                }
                else DroppedItemService.SpawnLoot(activeDrop.ItemId, (Vector2)activeDrop.Position);
                // 只有真实生成成功才消费队列；任何反馈异常都不能先减掉尚未交付的战利品。
                activeDrop.Remaining--;
                Drops++;
            }
        }

        /// <summary>运行时新阵营追加稳定索引；这是稀少配置变化，不在每只 AI 中处理字符串。</summary>
        private int Faction(string value)
        {
            value = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (factionIndices.TryGetValue(value, out int index)) return index;
            index = factionNames.Count; factionNames.Add(value); factionIndices.Add(value, index);
            if (Simulation != null) RefreshFactions(); return index;
        }

        /// <summary>重建少量阵营之间的矩阵，复用现有友好/中立/敌对规则。</summary>
        private byte[] Relations()
        {
            int size = factionNames.Count; var matrix = new byte[size * size];
            for (int i = 0; i < size; i++) for (int j = 0; j < size; j++)
                matrix[i * size + j] = (byte)(FactionRelationService.GetRelation(factionNames[i], factionNames[j]) == FactionRelation.Hostile ? 1 : 0);
            return matrix;
        }

        /// <summary>冻结字符串表，只在内容或阵营规则变化时分配。</summary>
        private FixedString128Bytes[] FactionValues()
        {
            var values = new FixedString128Bytes[factionNames.Count];
            for (int i = 0; i < values.Length; i++) values[i] = factionNames[i]; return values;
        }

        /// <summary>在旧 Native 读取完成后发布新阵营表。</summary>
        private void RefreshFactions() { Simulation.SetFactions(FactionValues(), Relations()); factionRevision = FactionRelationService.Revision; }

        /// <summary>注销输入后完成 Job，归还共享目标，并清空所有外部引用。</summary>
        public void Dispose()
        {
            WorldNavigationFlowRegistry.Unregister(this);
            GameplayCombatBridge.Unregister(this); Simulation?.Dispose(); Simulation = null; los?.Dispose();
            if (Navigation != null && goals != null) foreach (var goal in goals) Navigation.RemoveGoal(goal);
            if (expiredCorpses.IsCreated) expiredCorpses.Dispose();
            Navigation = null; proxies.Clear(); proxyLookup.Clear(); weapons.Clear(); corpses.Clear(); dropQueue.Clear(); activeDrop = default;
        }
        #endregion
    }
}
