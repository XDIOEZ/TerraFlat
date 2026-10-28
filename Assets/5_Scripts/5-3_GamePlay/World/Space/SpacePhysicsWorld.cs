using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    internal sealed class SpacePhysicsBody
    {
        #region 业务状态与物理代理
        public readonly string Id;
        public SpaceSectorPosition Position;
        public SpaceVector2 Velocity;
        public double AngleRadians, AngularVelocityRadians, MassKg, InertiaKgM2, RadiusMeters, InputRetention;
        public float LinearDamping, AngularDamping;
        public bool FreeCircle, FreezeRotation, Active;
        public bool Kinematic;
        public SpaceSectorPosition KinematicTarget;
        public SpaceVector2 KinematicTargetVelocity;
        public ShipAssemblyState Assembly;
        public SpacePassengerState Passenger;
        public SpaceLooseItemState LooseItem;
        public string MassSnapshot;
        public readonly List<SpacePhysicsShape> Shapes = new();
        public readonly List<Vector2> ContactNormals = new();
        internal GameObject Root;
        internal Rigidbody2D Rigidbody;
        internal readonly List<Collider2D> Colliders = new();
        internal readonly List<SpacePhysicsShape> AppliedShapes = new();
        internal readonly PhysicsShapeGroup2D BlockingShapes = new();
        internal readonly PhysicsShapeGroup2D NonBlockingShapes = new();
        internal readonly Dictionary<ShipState, ShipColliderGeometry> ShipGeometries = new();
        internal readonly HashSet<ShipState> ShapeShips = new();
        internal readonly List<ShipState> RemovedShapeShips = new();
        internal bool ShapesApplied;
        internal SpacePhysicsIsland Island;
        internal Vector2 StartLocalPosition;
        internal float StartLocalAngle;
        public SpacePhysicsBody(string id) { Id = id; }
        #endregion
    }

    internal readonly struct SpacePhysicsShape
    {
        #region 原生碰撞形状
        public readonly Vector2 Offset, Size;
        public readonly float AngleDegrees;
        public readonly bool Circle, IgnoreFreeCircles;
        public SpacePhysicsShape(Vector2 offset, Vector2 size, float angle = 0f, bool circle = false, bool ignoreFreeCircles = false)
        { Offset = offset; Size = size; AngleDegrees = angle; Circle = circle; IgnoreFreeCircles = ignoreFreeCircles; }
        #endregion
    }

    internal sealed class SpacePhysicsIsland
    {
        #region 独立物理场景与局部参考系
        public WorldPhysicsScene2D Context;
        public Scene Scene => Context?.Scene ?? default;
        public PhysicsScene2D Physics => Context?.PhysicsScene ?? default;
        public SpaceSectorPosition Origin;
        public SpaceVector2 OriginDelta, StartVelocity, EndVelocity;
        public SpaceGravitySource? Frame;
        public readonly List<SpacePhysicsBody> Bodies = new();
        #endregion
    }

    internal readonly struct SpacePhysicsImpact
    {
        #region 原生接触的损伤载荷
        public readonly SpacePhysicsBody Left, Right;
        public readonly double EnergyJoules;
        public SpacePhysicsImpact(SpacePhysicsBody left, SpacePhysicsBody right, double energy)
        { Left = left; Right = right; EnergyJoules = energy; }
        #endregion
    }

    internal sealed class SpacePhysicsWorld : IDisposable
    {
        #region 物理代理与岛生命周期
        private readonly Dictionary<string, SpacePhysicsBody> bodies = new(StringComparer.Ordinal);
        private readonly List<SpacePhysicsBody> active = new();
        private readonly List<string> removed = new();
        private readonly List<SpacePhysicsIsland> islands = new();
        private readonly Dictionary<int, SpacePhysicsIsland> groups = new();
        private readonly HashSet<SpacePhysicsIsland> claimed = new();
        private readonly List<int> parents = new();
        private readonly Dictionary<PhysicsCell, int> cells = new();
        private readonly List<GravityFrame> gravityFrames = new();
        private readonly Dictionary<string, GravityFrame> gravityById = new(StringComparer.Ordinal);
        private readonly PhysicsMaterial2D material = new("SpacePhysics") { friction = 0f, bounciness = 0f };
        public readonly List<SpacePhysicsImpact> Impacts = new();
        private int sceneNumber;
        private bool filtersDirty;
        private int collisionLayer = -1;
        private readonly int ownerId;
        public IReadOnlyList<SpacePhysicsBody> ActiveBodies => active;
        public SpacePhysicsWorld(int ownerId) { this.ownerId = ownerId; }

        public void BeginFrame()
        {
            active.Clear(); Impacts.Clear();
            foreach (SpacePhysicsBody body in bodies.Values) body.Active = false;
        }

        public SpacePhysicsBody Acquire(string id)
        {
            if (!bodies.TryGetValue(id, out SpacePhysicsBody body)) bodies.Add(id, body = new SpacePhysicsBody(id));
            if (!body.Active) { body.Active = true; active.Add(body); }
            body.Shapes.Clear(); body.InputRetention = 1d;
            body.Kinematic = false;
            body.ContactNormals.Clear();
            return body;
        }

        public void Dispose()
        {
            foreach (SpacePhysicsBody body in bodies.Values) DestroyBody(body);
            foreach (SpacePhysicsIsland island in islands) island.Context?.Dispose();
            UnityEngine.Object.Destroy(material);
            bodies.Clear(); active.Clear(); islands.Clear(); groups.Clear(); claimed.Clear(); cells.Clear();
            Impacts.Clear(); gravityFrames.Clear(); gravityById.Clear();
        }

        private static void DestroyBody(SpacePhysicsBody body)
        {
            if (body.Rigidbody != null) body.Rigidbody.simulated = false;
            if (body.Root != null)
            {
                body.Root.SetActive(false);
                UnityEngine.Object.Destroy(body.Root);
            }
            body.Root = null; body.Rigidbody = null; body.Colliders.Clear();
            body.AppliedShapes.Clear(); body.BlockingShapes.Clear(); body.NonBlockingShapes.Clear();
            body.ShapesApplied = false;
        }

        private void EnsureIslandScene(SpacePhysicsIsland island)
        {
            if (island.Context?.IsValid == true) return;
            // 会话能跨场景保留，已卸载的计算场景及代理必须从权威业务状态重新建立。
            foreach (SpacePhysicsBody body in bodies.Values)
                if (body.Island == island) DestroyBody(body);
            island.Context?.Dispose();
            island.Context = WorldPhysicsScene2D.CreateIsolated($"SpacePhysics_{ownerId}_{++sceneNumber}");
            filtersDirty = true;
        }
        #endregion

        #region 空间分组与精度边界
        private int FindRoot(int index)
        {
            while (parents[index] != index) { parents[index] = parents[parents[index]]; index = parents[index]; }
            return index;
        }

        private readonly struct PhysicsCell : IEquatable<PhysicsCell>
        {
            private readonly SpaceSectorId sector;
            private readonly int x, y;
            public PhysicsCell(SpaceSectorPosition point, double size)
            {
                sector = point.Sector;
                x = (int)Math.Floor((point.LocalMeters.X + SpaceSectorCoordinates.SizeMeters * .5d) / size);
                y = (int)Math.Floor((point.LocalMeters.Y + SpaceSectorCoordinates.SizeMeters * .5d) / size);
            }
            public bool Equals(PhysicsCell other) => sector == other.sector && x == other.x && y == other.y;
            public override bool Equals(object obj) => obj is PhysicsCell other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(sector, x, y);
        }

        private void BuildIslands(double seconds, double groupingDistance)
        {
            removed.Clear();
            foreach (var pair in bodies) if (!pair.Value.Active) { DestroyBody(pair.Value); removed.Add(pair.Key); }
            foreach (string id in removed) bodies.Remove(id);
            while (parents.Count < active.Count) parents.Add(0);
            for (int i = 0; i < active.Count; i++) parents[i] = i;
            cells.Clear();
            double halfSector = SpaceSectorCoordinates.SizeMeters * .5d;
            double cellSize = SpaceSectorCoordinates.SizeMeters / Math.Max(1d, Math.Floor(SpaceSectorCoordinates.SizeMeters / groupingDistance));
            for (int i = 0; i < active.Count; i++)
            {
                SpacePhysicsBody body = active[i];
                double reach = groupingDistance * .5d + body.RadiusMeters + body.Velocity.Magnitude * seconds;
                int minX = (int)Math.Floor((body.Position.LocalMeters.X - reach + halfSector) / cellSize);
                int maxX = (int)Math.Floor((body.Position.LocalMeters.X + reach + halfSector) / cellSize);
                int minY = (int)Math.Floor((body.Position.LocalMeters.Y - reach + halfSector) / cellSize);
                int maxY = (int)Math.Floor((body.Position.LocalMeters.Y + reach + halfSector) / cellSize);
                if ((long)(maxX - minX + 1) * (maxY - minY + 1) > 4096)
                {
                    for (int j = 0; j < active.Count; j++)
                    {
                        SpaceVector2 delta = SpaceSectorCoordinates.Relative(active[j].Position.Sector, active[j].Position.LocalMeters, body.Position.Sector) - body.Position.LocalMeters;
                        double radius = reach + active[j].RadiusMeters + active[j].Velocity.Magnitude * seconds;
                        if (delta.SqrMagnitude <= radius * radius) parents[FindRoot(j)] = FindRoot(i);
                    }
                    continue;
                }
                // 网格只决定共用哪个局部物理场景，真实碰撞仍全部交给 Unity。
                for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                {
                    SpaceSectorPosition center = SpaceSectorCoordinates.Normalize(body.Position.Sector,
                        new SpaceVector2((x + .5d) * cellSize - halfSector, (y + .5d) * cellSize - halfSector));
                    PhysicsCell cell = new(center, cellSize);
                    if (cells.TryGetValue(cell, out int previous)) parents[FindRoot(i)] = FindRoot(previous);
                    else cells.Add(cell, i);
                }
            }
            groups.Clear(); claimed.Clear();
            foreach (SpacePhysicsIsland island in islands) island.Bodies.Clear();
            for (int i = 0; i < active.Count; i++)
            {
                SpacePhysicsBody body = active[i];
                int root = FindRoot(i);
                if (!groups.TryGetValue(root, out SpacePhysicsIsland island))
                {
                    island = body.Island != null && !claimed.Contains(body.Island) ? body.Island : null;
                    if (island == null)
                        foreach (SpacePhysicsIsland candidate in islands)
                            if (!claimed.Contains(candidate)) { island = candidate; break; }
                    if (island == null)
                    {
                        island = new SpacePhysicsIsland();
                        islands.Add(island);
                    }
                    EnsureIslandScene(island);
                    groups.Add(root, island); claimed.Add(island);
                }
                if (body.Island != island) { body.Island = island; filtersDirty = true; }
                island.Bodies.Add(body);
            }
        }

        private sealed class GravityFrame
        {
            public SpaceGravitySource Source;
            public SpaceVector2 Position, Velocity, NextPosition, NextVelocity;
        }

        private void SampleGravity(IReadOnlyList<SpaceGravitySource> sources, double clock, double seconds)
        {
            gravityById.Clear();
            while (gravityFrames.Count < sources.Count) gravityFrames.Add(new GravityFrame());
            for (int i = 0; i < sources.Count; i++)
            {
                GravityFrame frame = gravityFrames[i]; frame.Source = sources[i];
                frame.Position = frame.Source.Universe.GetPositionAtTime(frame.Source.Body.BodyId, clock);
                frame.Velocity = frame.Source.Universe.GetVelocityAtTime(frame.Source.Body.BodyId, clock);
                frame.NextPosition = frame.Source.Universe.GetPositionAtTime(frame.Source.Body.BodyId, clock + seconds);
                frame.NextVelocity = frame.Source.Universe.GetVelocityAtTime(frame.Source.Body.BodyId, clock + seconds);
                gravityById.Add(frame.Source.Body.BodyId, frame);
            }
        }

        private GravityFrame FindGravity(SpaceSectorPosition point, int count)
        {
            GravityFrame selected = null;
            for (int i = 0; i < count; i++)
            {
                GravityFrame frame = gravityFrames[i];
                SpaceVector2 relative = SpaceSectorCoordinates.Relative(point.Sector, point.LocalMeters, frame.Source.Body.Sector) - frame.Position;
                if (relative.SqrMagnitude > frame.Source.RadiusSquared) continue;
                if (selected == null || frame.Source.Body.MassKg > selected.Source.Body.MassKg ||
                    frame.Source.Body.MassKg == selected.Source.Body.MassKg &&
                    string.CompareOrdinal(frame.Source.Body.BodyId, selected.Source.Body.BodyId) < 0) selected = frame;
            }
            return selected;
        }
        #endregion

        #region 原生刚体与碰撞形状
        private void PrepareBody(SpacePhysicsBody body)
        {
            if (body.Root == null)
            {
                DestroyBody(body);
                body.Root = new GameObject(body.Id) { hideFlags = HideFlags.HideInHierarchy };
                body.Rigidbody = body.Root.AddComponent<Rigidbody2D>();
                body.Rigidbody.bodyType = RigidbodyType2D.Dynamic;
                body.Rigidbody.gravityScale = 0f;
                body.Rigidbody.drag = body.Rigidbody.angularDrag = 0f;
                body.Rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.Rigidbody.interpolation = RigidbodyInterpolation2D.None;
                SpacePhysicsContactRelay relay = body.Root.AddComponent<SpacePhysicsContactRelay>();
                relay.World = this; relay.Body = body;
                body.Colliders.Add(body.Root.AddComponent<CustomCollider2D>());
                body.Colliders.Add(body.Root.AddComponent<CustomCollider2D>());
                foreach (Collider2D collider in body.Colliders) collider.sharedMaterial = material;
                filtersDirty = true;
            }
            if (body.Root.scene != body.Island.Scene)
            {
                body.Rigidbody.simulated = false;
                SceneManager.MoveGameObjectToScene(body.Root, body.Island.Scene);
                body.Rigidbody.simulated = true;
                filtersDirty = true;
            }
            body.Root.layer = collisionLayer;
            SynchronizeBodyShapes(body);
            Rigidbody2D rigidbody = body.Rigidbody;
            RigidbodyType2D bodyType = body.Kinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            if (rigidbody.bodyType != bodyType) rigidbody.bodyType = bodyType;
            rigidbody.constraints = body.FreezeRotation ? RigidbodyConstraints2D.FreezeRotation : RigidbodyConstraints2D.None;
            rigidbody.drag = body.LinearDamping;
            rigidbody.useAutoMass = false;
            rigidbody.mass = (float)Math.Max(.001d, body.MassKg);
            rigidbody.centerOfMass = Vector2.zero;
            rigidbody.inertia = (float)Math.Max(.00001d, body.InertiaKgM2);
            Vector2 point = (SpaceSectorCoordinates.Relative(body.Position.Sector, body.Position.LocalMeters, body.Island.Origin.Sector) -
                body.Island.Origin.LocalMeters).ToVector2();
            float rotation = (float)(Math.IEEERemainder(body.AngleRadians, Math.PI * 2d) * Mathf.Rad2Deg);
            if ((rigidbody.position - point).sqrMagnitude > 1e-12f) rigidbody.position = point;
            if (Mathf.Abs(Mathf.DeltaAngle(rigidbody.rotation, rotation)) > .00001f) rigidbody.rotation = rotation;
            rigidbody.velocity = (body.Velocity - body.Island.StartVelocity).ToVector2();
            rigidbody.angularVelocity = (float)(body.AngularVelocityRadians * Mathf.Rad2Deg);
            body.StartLocalPosition = rigidbody.position; body.StartLocalAngle = rigidbody.rotation;
        }

        private void SynchronizeBodyShapes(SpacePhysicsBody body)
        {
            bool changed = !body.ShapesApplied || body.Shapes.Count != body.AppliedShapes.Count;
            for (int i = 0; !changed && i < body.Shapes.Count; i++)
            {
                SpacePhysicsShape current = body.Shapes[i], previous = body.AppliedShapes[i];
                changed = current.Circle != previous.Circle || current.IgnoreFreeCircles != previous.IgnoreFreeCircles ||
                    (current.Offset - previous.Offset).sqrMagnitude > 1e-10f ||
                    (current.Size - previous.Size).sqrMagnitude > 1e-10f ||
                    Mathf.Abs(Mathf.DeltaAngle(current.AngleDegrees, previous.AngleDegrees)) > .0001f;
            }
            if (!changed) return;
            body.BlockingShapes.Clear(); body.NonBlockingShapes.Clear();
            // 连片船件先合成矩形，再按是否阻挡自由物分装，原生组件数量不随部件增长。
            foreach (SpacePhysicsShape shape in body.Shapes)
            {
                PhysicsShapeGroup2D group = shape.IgnoreFreeCircles ? body.NonBlockingShapes : body.BlockingShapes;
                if (shape.Circle) group.AddCircle(shape.Offset, shape.Size.x);
                else group.AddBox(shape.Offset, shape.Size, shape.AngleDegrees);
            }
            for (int i = 0; i < body.Colliders.Count; i++)
            {
                CustomCollider2D collider = (CustomCollider2D)body.Colliders[i];
                PhysicsShapeGroup2D group = i == 0 ? body.BlockingShapes : body.NonBlockingShapes;
                bool enabled = group.shapeCount > 0;
                if (enabled) collider.SetCustomShapes(group);
                else collider.ClearCustomShapes();
                if (collider.enabled != enabled) { collider.enabled = enabled; filtersDirty = true; }
            }
            body.AppliedShapes.Clear(); body.AppliedShapes.AddRange(body.Shapes);
            body.ShapesApplied = true;
        }

        private void UpdateCollisionFilters()
        {
            if (!filtersDirty) return;
            foreach (SpacePhysicsBody hull in active)
            {
                if (hull.FreeCircle) continue;
                for (int i = 0; i < hull.Colliders.Count; i++)
                foreach (SpacePhysicsBody circle in active)
                    if (hull.Colliders[i].enabled && circle.FreeCircle && circle.Island == hull.Island &&
                        circle.Colliders.Count > 0 && circle.Colliders[0].enabled)
                        Physics2D.IgnoreCollision(hull.Colliders[i], circle.Colliders[0], i == 1);
            }
            filtersDirty = false;
        }
        #endregion

        #region Unity 固定步模拟与结果读取
        // 局部物理场景独立推进，业务坐标只读取原生刚体位移，不再次积分或解算冲量。
        public void Step(IReadOnlyList<SpaceGravitySource> sources, double startSeconds, double seconds, SpaceGameplaySettings settings)
        {
            BuildIslands(seconds, settings.SpacePhysicsIslandDistanceMeters);
            if (active.Count == 0) return;
            if (collisionLayer < 0 || Physics2D.GetIgnoreLayerCollision(collisionLayer, collisionLayer))
            {
                collisionLayer = -1;
                for (int layer = 0; layer < 32; layer++)
                    if (!Physics2D.GetIgnoreLayerCollision(layer, layer)) { collisionLayer = layer; break; }
                if (collisionLayer < 0) throw new InvalidOperationException("项目没有可用于独立太空物理场景的自碰撞层。");
                filtersDirty = true;
            }
            SampleGravity(sources, startSeconds, seconds);
            foreach (SpacePhysicsIsland island in claimed)
            {
                GravityFrame frame = null;
                bool sharedFrame = true;
                foreach (SpacePhysicsBody body in island.Bodies)
                {
                    GravityFrame candidate = FindGravity(body.Position, sources.Count);
                    if (candidate == null || frame != null && candidate.Source.Body != frame.Source.Body) { sharedFrame = false; break; }
                    frame = candidate;
                }
                island.Frame = sharedFrame && frame != null ? frame.Source : null;
                island.Origin = island.Frame.HasValue ? new SpaceSectorPosition(frame.Source.Body.Sector, frame.Position) : island.Bodies[0].Position;
                island.OriginDelta = island.Frame.HasValue ? frame.NextPosition - frame.Position : default;
                island.StartVelocity = island.Frame.HasValue ? frame.Velocity : default;
                island.EndVelocity = island.Frame.HasValue ? frame.NextVelocity : default;
            }
            foreach (SpacePhysicsBody body in active) PrepareBody(body);
            foreach (SpacePhysicsBody body in active)
                if (body.Kinematic)
                {
                    // 固定站台通过原生运动学速度抵达锚点，接触仍由同一物理场景解算。
                    SpaceVector2 delta = SpaceSectorCoordinates.Relative(body.KinematicTarget, body.Position.Sector) - body.Position.LocalMeters;
                    body.Rigidbody.velocity = ((delta - body.Island.OriginDelta) / seconds).ToVector2();
                }
            UpdateCollisionFilters();
            Physics2D.SyncTransforms();
            float previousSpeed = Physics2D.maxTranslationSpeed, previousRotation = Physics2D.maxRotationSpeed;
            try
            {
                double maximumSpeed = 50000d;
                foreach (SpacePhysicsBody body in active) maximumSpeed = Math.Max(maximumSpeed, body.Velocity.Magnitude * 4d);
                Physics2D.maxTranslationSpeed = (float)Math.Max(previousSpeed, maximumSpeed);
                Physics2D.maxRotationSpeed = Math.Max(previousRotation, 360000f);
                int steps = Math.Max(settings.SpacePhysicsSubsteps, (int)Math.Ceiling(seconds / .005d));
                double step = seconds / steps;
                for (int i = 0; i < steps; i++)
                {
                    double clock = startSeconds + step * i;
                    SampleGravity(sources, clock, step);
                    foreach (SpacePhysicsBody body in active) ApplyForces(body, sources.Count, clock, step, settings);
                    foreach (SpacePhysicsIsland island in claimed)
                        if (!island.Context.Simulate((float)step)) throw new InvalidOperationException("独立太空 2D 物理场景未能推进。");
                }
            }
            finally
            {
                Physics2D.maxTranslationSpeed = previousSpeed; Physics2D.maxRotationSpeed = previousRotation;
            }
            foreach (SpacePhysicsBody body in active)
            {
                if (body.Kinematic)
                {
                    body.Position = body.KinematicTarget;
                    body.Velocity = body.KinematicTargetVelocity;
                    body.AngleRadians = body.AngularVelocityRadians = 0d;
                    continue;
                }
                SpaceVector2 delta = SpaceVector2.FromVector2(body.Rigidbody.position - body.StartLocalPosition) + body.Island.OriginDelta;
                body.Position = SpaceSectorCoordinates.Normalize(body.Position.Sector, body.Position.LocalMeters + delta);
                body.Velocity = SpaceVector2.FromVector2(body.Rigidbody.velocity) + body.Island.EndVelocity;
                body.AngleRadians += Mathf.DeltaAngle(body.StartLocalAngle, body.Rigidbody.rotation) * Mathf.Deg2Rad;
                body.AngularVelocityRadians = body.Rigidbody.angularVelocity * Mathf.Deg2Rad;
            }
        }

        private void ApplyForces(SpacePhysicsBody body, int sourceCount, double clock, double seconds, SpaceGameplaySettings settings)
        {
            if (body.Kinematic) return;
            SpacePhysicsIsland island = body.Island;
            SpaceVector2 framePosition = island.Origin.LocalMeters, frameVelocity = default, frameAcceleration = default;
            if (island.Frame is SpaceGravitySource frame)
            {
                GravityFrame sample = gravityById[frame.Body.BodyId];
                framePosition = sample.Position;
                frameVelocity = sample.Velocity;
                frameAcceleration = (sample.NextVelocity - frameVelocity) / seconds;
            }
            SpaceVector2 delta = SpaceVector2.FromVector2(body.Rigidbody.position - body.StartLocalPosition) + framePosition - island.Origin.LocalMeters;
            var point = new SpaceSectorPosition(body.Position.Sector, body.Position.LocalMeters + delta);
            GravityFrame gravity = FindGravity(point, sourceCount);
            SpaceVector2 acceleration = -frameAcceleration;
            body.Rigidbody.angularDrag = body.AngularDamping + (gravity == null ? 0f : (float)gravity.Source.DampingPerSecond);
            if (gravity != null)
            {
                SpaceVector2 relativePosition = SpaceSectorCoordinates.Relative(point.Sector, point.LocalMeters, gravity.Source.Body.Sector) - gravity.Position;
                SpaceVector2 relativeVelocity = SpaceVector2.FromVector2(body.Rigidbody.velocity) + frameVelocity - gravity.Velocity;
                acceleration += (gravity.NextVelocity - gravity.Velocity) / seconds;
                bool rest = gravity.Source.DampingPerSecond > 0d && relativePosition.SqrMagnitude <= settings.GravityRestDistanceMeters * settings.GravityRestDistanceMeters &&
                    relativeVelocity.SqrMagnitude <= settings.GravityRestSpeedMetersPerSecond * settings.GravityRestSpeedMetersPerSecond;
                if (rest) body.Rigidbody.velocity = (gravity.Velocity - frameVelocity).ToVector2();
                else acceleration += SpaceGravity.Acceleration(gravity.Source, relativePosition, relativeVelocity);
                body.InputRetention *= Math.Exp(-gravity.Source.DampingPerSecond * seconds);
            }
            if (acceleration.SqrMagnitude > 0d)
                body.Rigidbody.AddForce(acceleration.ToVector2() * body.Rigidbody.mass, ForceMode2D.Force);
        }

        internal void RecordImpact(SpacePhysicsBody left, Collision2D collision)
        {
            SpacePhysicsContactRelay other = collision.rigidbody == null ? null : collision.rigidbody.GetComponent<SpacePhysicsContactRelay>();
            if (other == null || other.World != this || !left.Active || !other.Body.Active) return;
            if (left.Passenger != null)
                for (int i = 0; i < collision.contactCount; i++) left.ContactNormals.Add(collision.GetContact(i).normal);
            if (left.Assembly == null || other.Body.Assembly == null || string.CompareOrdinal(left.Id, other.Body.Id) >= 0) return;
            double impulse = 0d;
            for (int i = 0; i < collision.contactCount; i++) impulse += Math.Max(0d, collision.GetContact(i).normalImpulse);
            double energy = .5d * impulse * collision.relativeVelocity.magnitude;
            if (energy > 0d) Impacts.Add(new SpacePhysicsImpact(left, other.Body, energy));
        }
        #endregion
    }
}
