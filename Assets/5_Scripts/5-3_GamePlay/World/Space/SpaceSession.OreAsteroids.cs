using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 矿带生成与权威掉落事务
        private readonly Dictionary<string, SpriteRenderer> oreAsteroidViews = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DroppedItemVisual> oreAsteroidVisuals = new(StringComparer.Ordinal);
        private GameRes oreAsteroidResourceOwner;
        private int oreAsteroidResourceReloadVersion;

        private void EnsureSectorOreAsteroidsGenerated(UniverseState sector)
        {
            if (!GameNetwork.HasStateAuthority || State.GeneratedOreAsteroidSectors.Contains(sector.Sector)) return;
            List<SpaceOreAsteroidState> generated = SpaceOreAsteroidCatalog.LoadDefault().GenerateSectorOreAsteroids(owner.Seed, sector);
            State.OreAsteroids.AddRange(generated);
            State.GeneratedOreAsteroidSectors.Add(sector.Sector);
        }

        public SpaceSectorPosition GetOreAsteroidPosition(SpaceOreAsteroidState asteroid)
        {
            BodyState center = GetBody(asteroid.CenterBodyId);
            return SpaceSectorCoordinates.Normalize(center.Sector, center.PositionMeters + asteroid.OffsetMeters);
        }

        public double ApplyResolvedOreAsteroidDamage(SpaceOreAsteroidState asteroid, double damage)
        {
            if (!GameNetwork.HasStateAuthority || State == null || asteroid == null || !State.OreAsteroids.Contains(asteroid) ||
                !asteroid.IsAlive || !double.IsFinite(damage) || damage <= 0d) return 0d;
            if (damage < asteroid.HitPoints)
            {
                asteroid.TryApplyResolvedDamage(damage, false, out double nonlethalApplied);
                return nonlethalApplied;
            }
            SpaceOreAsteroidComposition composition = SpaceOreAsteroidCatalog.LoadDefault().GetComposition(asteroid.CompositionId);
            SpaceSectorPosition position = GetOreAsteroidPosition(asteroid);
            SpaceVector2 velocity = GetBody(asteroid.CenterBodyId).VelocityMetersPerSecond;
            using var lease = UnityEngine.Pool.ListPool<SpaceLooseItemState>.Get(out var prepared);
            prepared.Add(PrepareOreAsteroidDrop(composition.ItemId, asteroid.OreAmount, position, velocity, -.2d, 0, out int oreGuid));
            if (asteroid.StoneAmount > 0)
                prepared.Add(PrepareOreAsteroidDrop("Ore_Stone", asteroid.StoneAmount, position, velocity, .2d, oreGuid, out _));
            // 两份冷载荷都成功创建后才提交致死状态，失败不会吞矿或留下半笔掉落。
            int requiredCapacity = State.LooseItems.Count + prepared.Count;
            if (State.LooseItems.Capacity < requiredCapacity) State.LooseItems.Capacity = requiredCapacity;
            if (!asteroid.TryApplyResolvedDamage(damage, true, out double applied)) return 0d;
            State.LooseItems.AddRange(prepared);
            return applied;
        }

        private SpaceLooseItemState PrepareOreAsteroidDrop(string itemId, int amount, SpaceSectorPosition position,
            SpaceVector2 velocity, double offset, int excludedGuid, out int allocatedGuid)
        {
            ItemData data = GameRes.ExistingInstance?.CreateItemData(itemId);
            if (data == null || amount <= 0) throw new InvalidOperationException("矿石小行星无法创建掉落：" + itemId);
            data.Guid = allocatedGuid = AllocateSessionDropGuid(excludedGuid == 0 ? data.Guid : excludedGuid);
            data.inHand = false; data.Stack.CanBePickedUp = true; data.Stack.Amount = amount;
            SpaceSectorPosition point = SpaceSectorCoordinates.Normalize(position.Sector, position.LocalMeters + new SpaceVector2(offset, 0d));
            return new SpaceLooseItemState
            {
                InSpace = true, Snapshot = Encode(data), WorldKey = SpaceSectorCoordinates.SceneKey(point.Sector),
                PickupAfterSimulationSeconds = State.SimulationSeconds + .35d, Scale = Vector3.one * .5f,
                Motion = new SpaceMotionState { Sector = point.Sector, PositionMeters = point.LocalMeters,
                    VelocityMetersPerSecond = velocity + new SpaceVector2(offset * 2d, .2d), RadiusMeters = .15d }
            };
        }
        #endregion

        #region 正式原生物理的近身岩石代理
        private void AddNearbyOreAsteroidPhysicsBodies(double seconds)
        {
            int movingBodyCount = spacePhysics.ActiveBodies.Count;
            if (movingBodyCount == 0) return;
            double activation = SpaceOreAsteroidCatalog.LoadDefault().Configuration.PhysicsActivationDistanceMeters;
            foreach (SpaceOreAsteroidState asteroid in State.OreAsteroids)
            {
                if (!asteroid.IsAlive) continue;
                SpaceSectorPosition position = GetOreAsteroidPosition(asteroid);
                bool near = false;
                for (int index = 0; index < movingBodyCount; index++)
                {
                    SpacePhysicsBody moving = spacePhysics.ActiveBodies[index];
                    double reach = activation + moving.RadiusMeters + asteroid.RadiusMeters + moving.Velocity.Magnitude * seconds;
                    if (SpaceSectorCoordinates.Difference(position, moving.Position).SqrMagnitude > reach * reach) continue;
                    near = true; break;
                }
                if (!near) continue;
                BodyState center = GetBody(asteroid.CenterBodyId);
                SpacePhysicsBody body = spacePhysics.Acquire("ore-asteroid:" + asteroid.AsteroidId);
                body.Assembly = null; body.Passenger = null; body.LooseItem = null;
                body.Position = SpaceSectorCoordinates.Normalize(center.Sector,
                    GetUniverse(center.Sector).GetPositionAtTime(center.BodyId, Math.Max(0d, State.SimulationSeconds - seconds)) + asteroid.OffsetMeters);
                body.Velocity = center.VelocityMetersPerSecond;
                body.AngleRadians = body.AngularVelocityRadians = 0d;
                body.MassKg = Math.Max(1d, asteroid.RadiusMeters * asteroid.RadiusMeters * asteroid.RadiusMeters * 2500d);
                ConfigureSpaceCircle(body, asteroid.RadiusMeters, true);
                // 矿带随中心星体平移，碰撞交给同一独立原生场景，不额外积分或施加冲量。
                body.Kinematic = true; body.KinematicTarget = position;
                body.KinematicTargetVelocity = center.VelocityMetersPerSecond;
            }
        }
        #endregion

        #region 矿石外观与资源释放
        private void SynchronizeOreAsteroidPresentation()
        {
            GameRes resources = GameRes.ExistingInstance;
            if (resources == null) { ReleaseOreAsteroidPresentation(); return; }
            if (!ReferenceEquals(oreAsteroidResourceOwner, resources) || oreAsteroidResourceReloadVersion != resources.ResourceReloadVersion)
            {
                // 资源原位更新后释放旧贴图和材质引用，保持矿石表现与正式目录同代。
                ReleaseOreAsteroidPresentation();
                oreAsteroidResourceOwner = resources; oreAsteroidResourceReloadVersion = resources.ResourceReloadVersion;
            }
            Player player = ItemMgr.Instance?.User_Player;
            bool canView = IsSpaceView && player != null && TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer) && observer.InSpace;
            SpaceSectorPosition focus = canView ? GetPassengerPosition(FindPassenger(player.ProfileName)) : default;
            using var removedLease = UnityEngine.Pool.ListPool<string>.Get(out var removed);
            using var idsLease = UnityEngine.Pool.HashSetPool<string>.Get(out var presentIds);
            foreach (SpaceOreAsteroidState asteroid in State.OreAsteroids)
            {
                presentIds.Add(asteroid.AsteroidId);
                bool visible = canView && asteroid.IsAlive;
                SpaceSectorPosition position = visible ? GetOreAsteroidPosition(asteroid) : default;
                if (visible) visible = SpaceSectorCoordinates.Difference(position, focus).SqrMagnitude <= 1200d * 1200d;
                if (!visible)
                {
                    if (oreAsteroidViews.Remove(asteroid.AsteroidId, out SpriteRenderer hidden) && hidden != null)
                        Destroy(hidden.gameObject);
                    continue;
                }
                if (!oreAsteroidViews.TryGetValue(asteroid.AsteroidId, out SpriteRenderer renderer) || renderer == null)
                {
                    SpaceOreAsteroidComposition composition = SpaceOreAsteroidCatalog.LoadDefault().GetComposition(asteroid.CompositionId);
                    if (!oreAsteroidVisuals.TryGetValue(composition.ItemId, out DroppedItemVisual visual))
                    {
                        ItemData sample = GameRes.ExistingInstance.CreateItemData(composition.ItemId);
                        visual = DroppedItemVisual.Resolve(sample, DroppedItemVisual.ResolveSprite(sample));
                        oreAsteroidVisuals[composition.ItemId] = visual;
                    }
                    GameObject root = new("矿石小行星_" + asteroid.CompositionId);
                    SceneManager.MoveGameObjectToScene(root, gameObject.scene);
                    renderer = root.AddComponent<SpriteRenderer>();
                    renderer.sprite = visual.Sprite; renderer.color = visual.Color; renderer.sharedMaterial = visual.SpriteMaterial;
                    WorldSortingManager.GetInstance().ApplyRenderer(renderer, WorldSortingManager.WorldItemCategory, 0);
                    float diameter = Mathf.Max(visual.Sprite.bounds.size.x, visual.Sprite.bounds.size.y);
                    renderer.transform.localScale = Vector3.one * (float)(asteroid.RadiusMeters * 2d / Math.Max(.001d, diameter));
                    renderer.transform.rotation = Quaternion.Euler(0f, 0f,
                        unchecked((uint)UniverseSectorFactory.DeriveSeed(0, asteroid.AsteroidId)) % 360u);
                    oreAsteroidViews[asteroid.AsteroidId] = renderer;
                }
                renderer.transform.position = ProjectPosition(position);
            }
            foreach (string id in oreAsteroidViews.Keys)
                if (!presentIds.Contains(id)) removed.Add(id);
            foreach (string id in removed)
                if (oreAsteroidViews.Remove(id, out SpriteRenderer renderer) && renderer != null) Destroy(renderer.gameObject);
        }

        private void ReleaseOreAsteroidPresentation()
        {
            foreach (SpriteRenderer renderer in oreAsteroidViews.Values) if (renderer != null) Destroy(renderer.gameObject);
            oreAsteroidViews.Clear(); oreAsteroidVisuals.Clear();
            oreAsteroidResourceOwner = null; oreAsteroidResourceReloadVersion = 0;
        }
        #endregion
    }
}
