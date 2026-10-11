using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 不随船固定的散落物
        private readonly Dictionary<string, Item> looseViews = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemData> looseData = new(StringComparer.Ordinal);
        private readonly Dictionary<Item, (SpriteRenderer Renderer, Vector3 LocalPosition)> looseHeightVisuals = new(ItemReferenceComparer.Instance);
        private bool releasingLooseViews;
        private void OnLooseItemDespawning(Item item)
        {
            RestoreLooseItemHeightVisual(item);
            if (releasingLooseViews || State == null || !ReferenceEquals(item.PersistenceOwner, this)) return;
            string key = null;
            foreach (var pair in looseViews) if (ReferenceEquals(pair.Value, item)) { key = pair.Key; break; }
            if (key == null) return;
            looseViews.Remove(key); looseData.Remove(key); State.LooseItems.RemoveAll(value => value.Id == key);
        }
        private void DespawnLooseView(Item item)
        {
            RestoreLooseItemHeightVisual(item);
            releasingLooseViews = true;
            try { if (item != null) ItemMgr.Instance?.DespawnItem(item, false); }
            finally { releasingLooseViews = false; }
        }
        private void QueueDrop(ItemData data, ShipState ship, ShipPieceState piece)
        {
            ShipGeometry.LocalToWorld(ship, (piece.CellX + .5d) * ship.CellSizeMeters,
                (piece.CellY + .5d) * ship.CellSizeMeters, out double x, out double y);
            ShipGeometry.PointVelocity(ship, x, y, out double vx, out double vy);
            data.inHand = false; data.Stack.CanBePickedUp = true;
            var flight = GetFlight(ship.ShipId);
            bool inSpace = flight.Phase is ShipFlightPhase.Cruising;
            State.LooseItems.Add(new SpaceLooseItemState
            {
                Snapshot = Encode(data), WorldKey = flight.SurfaceWorldKey,
                InSpace = inSpace,
                Motion = new SpaceMotionState { PositionMeters = new SpaceVector2(x, y), VelocityMetersPerSecond = new SpaceVector2(vx, vy),
                    RadiusMeters = .15d },
                RotationDegrees = ship.AngleRadians * 180d / Math.PI + piece.QuarterTurns * 90d,
                // 太空掉落不能继承船体残留的地表升空高度。
                SurfaceHeight = inSpace ? 0d : Math.Max(0d, flight.HeightMeters),
                SurfaceVerticalSpeed = inSpace ? 0d : flight.VerticalSpeed
            });
        }
        private void StepLooseItems(double seconds)
        {
            foreach (SpaceLooseItemState state in State.LooseItems)
            {
                if (!state.InSpace)
                {
                    if (state.SurfaceHeight > 0d && FindSurfaceBody(state.WorldKey) is BodyState body)
                    {
                        state.SurfaceVerticalSpeed -= body.SurfaceGravity * seconds;
                        state.SurfaceHeight = Math.Max(0d, state.SurfaceHeight + state.SurfaceVerticalSpeed * seconds);
                        SpaceVector2 previous = state.Motion.PositionMeters;
                        state.Motion.PositionMeters += state.Motion.VelocityMetersPerSecond * seconds;
                        ResolveLooseItemContact(state, previous, seconds);
                        if (state.SurfaceHeight == 0d) state.Motion.VelocityMetersPerSecond = default;
                    }
                    continue;
                }
                SpaceVector2 previousPosition = state.Motion.PositionMeters;
                // 漂浮物没有星体引力，只做有上限的直线移动与碰撞。
                double speed = state.Motion.VelocityMetersPerSecond.Magnitude;
                double maxSpeed = SpaceGameplaySettings.Current.CruiseMaxSpeedMetersPerSecond;
                if (speed > maxSpeed) state.Motion.VelocityMetersPerSecond *= maxSpeed / speed;
                bool touched = Universe.StepMotion(state.Motion, default, seconds, out SurfaceContact contact);
                bool hitShip = ResolveLooseItemContact(state, previousPosition, seconds, out _, touched ? contact.Fraction : 1d);
                if (hitShip || !touched) continue;
                state.InSpace = false; state.WorldKey = Universe.GetSurfaceAddress(contact.BodyId).WorldKey;
                state.Motion.PositionMeters = SurfaceCenter(contact.BodyId, contact.SurfaceAngleRadians);
                state.Motion.VelocityMetersPerSecond = default;
                state.SurfaceHeight = state.SurfaceVerticalSpeed = 0d;
            }
        }
        private void SynchronizeLooseItems()
        {
            foreach (SpaceLooseItemState state in State.LooseItems.ToArray())
            {
                if (looseViews.TryGetValue(state.Id, out Item existing) && existing != null &&
                    (existing.Owner != null || existing.InHand || existing.itemData.Stack.Amount <= 0f))
                {
                    RestoreLooseItemHeightVisual(existing);
                    looseViews.Remove(state.Id); looseData.Remove(state.Id); State.LooseItems.Remove(state); continue;
                }
                bool visible = state.InSpace ? IsSpaceView && (state.Motion.PositionMeters - ViewOrigin).Magnitude < 1200d
                    : !IsSpaceView && state.WorldKey == SceneManager.GetActiveScene().name;
                if (!visible)
                {
                    if (existing != null) { existing.Save(); state.Snapshot = Encode(existing.itemData); DespawnLooseView(existing); }
                    looseViews.Remove(state.Id); continue;
                }
                Vector2 position = state.InSpace ? ProjectPosition(state.Motion.PositionMeters) : WorldLocalPresentation.ProjectPosition(state.Motion.PositionMeters.ToVector2());
                if (existing == null)
                {
                    ItemData data = Decode(state.Snapshot);
                    existing = ItemMgr.Instance.InstantiateItem(data, position, Quaternion.Euler(0f, 0f, (float)state.RotationDegrees), state.Scale);
                    existing.PersistenceOwner = this; existing.Load(); looseViews[state.Id] = existing;
                    looseData[state.Id] = data;
                }
                if (state.InSpace || state.SurfaceHeight > 0d)
                {
                    existing.transform.position = position;
                    Rigidbody2D body = existing.GetComponent<Rigidbody2D>();
                    if (body != null) { body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0; body.position = position; }
                }
                else
                {
                    Rigidbody2D body = existing.GetComponent<Rigidbody2D>();
                    if (body != null) body.bodyType = RigidbodyType2D.Dynamic;
                    state.Motion.PositionMeters = SpaceVector2.FromVector2(WorldLocalPresentation.ToLogical(existing.transform.position));
                }
                ApplyLooseItemHeightVisual(existing, state.InSpace ? 0d : state.SurfaceHeight);
            }
        }
        private void ApplyLooseItemHeightVisual(Item item, double height)
        {
            float rise = (float)Math.Min(4d, Math.Max(0d, height) * .04d);
            SpriteRenderer renderer = item.Sprite;
            if (rise <= 0f || renderer == null || renderer.transform == item.transform)
            { RestoreLooseItemHeightVisual(item); return; }
            if (!looseHeightVisuals.TryGetValue(item, out var visual) || visual.Renderer != renderer)
            {
                RestoreLooseItemHeightVisual(item);
                visual = (renderer, renderer.transform.localPosition);
                looseHeightVisuals[item] = visual;
            }
            // 升高只叠加显示偏移，保留贴图原来的局部位置和旋转缩放关系。
            Vector3 offset = renderer.transform.parent.InverseTransformVector(Vector3.up * rise);
            renderer.transform.localPosition = visual.LocalPosition + offset;
        }
        private void RestoreLooseItemHeightVisual(Item item)
        {
            if (ReferenceEquals(item, null) || !looseHeightVisuals.Remove(item, out var visual)) return;
            if (visual.Renderer != null) visual.Renderer.transform.localPosition = visual.LocalPosition;
        }
        private void CaptureLooseItems()
        {
            foreach (SpaceLooseItemState state in State.LooseItems.ToArray())
                if (looseViews.TryGetValue(state.Id, out Item item) && item != null)
                {
                    if (item.Owner != null || item.InHand)
                    {
                        RestoreLooseItemHeightVisual(item);
                        State.LooseItems.Remove(state); looseViews.Remove(state.Id); looseData.Remove(state.Id); continue;
                    }
                    item.Save(); state.Snapshot = Encode(item.itemData);
                }
        }
        private void ReleaseLooseViews()
        {
            CaptureLooseItems();
            foreach (Item item in looseViews.Values) DespawnLooseView(item);
            looseViews.Clear(); looseData.Clear(); looseHeightVisuals.Clear();
        }
        private void AddLooseCargoMass(ShipState ship, ShipMassReader reader)
        {
            ShipFlightState flight = GetFlight(ship.ShipId);
            bool inSpace = flight.Phase is ShipFlightPhase.Cruising;
            double radius = CalculateShipContactRadius(ship);
            foreach (SpaceLooseItemState cargo in State.LooseItems)
            {
                if (cargo.InSpace != inSpace || !inSpace && cargo.WorldKey != ship.WorldAddress ||
                    !inSpace && Math.Abs(cargo.SurfaceHeight - flight.HeightMeters) > 1d) continue;
                double dx = cargo.Motion.PositionMeters.X - ship.PositionX, dy = cargo.Motion.PositionMeters.Y - ship.PositionY;
                if (dx * dx + dy * dy > radius * radius) continue;
                ShipGeometry.WorldToLocal(ship, cargo.Motion.PositionMeters.X, cargo.Motion.PositionMeters.Y, out double x, out double y);
                int cx = (int)Math.Floor(x / ship.CellSizeMeters), cy = (int)Math.Floor(y / ship.CellSizeMeters);
                if (!ShipAtmosphereService.HasFloor(ship, cx, cy)) continue;
                looseViews.TryGetValue(cargo.Id, out Item live);
                if (live != null && (live.Owner != null || live.InHand)) continue;
                if (!looseData.TryGetValue(cargo.Id, out ItemData data)) looseData[cargo.Id] = data = Decode(cargo.Snapshot);
                double mass = reader.ReadItem(live != null ? live.itemData : data, live, 0);
                if (mass > 0d)
                {
                    ShipMassContribution value = AddMassContribution(ship, "loose:", cargo.Id);
                    value.LocalX = x; value.LocalY = y; value.MassKg = mass;
                }
            }
        }
        public static bool TryGetMachineScene(MachineEntity node, out Scene scene)
        {
            scene = default;
            if (node?.ScopeKey == null) return false;
            if (!node.ScopeKey.StartsWith("ship:", StringComparison.Ordinal))
            { scene = SceneManager.GetSceneByName(node.ScopeKey); return scene.IsValid() && scene.isLoaded; }
            ShipState ship = Current?.GetShip(node.ScopeKey.Substring(5));
            if (ship == null || !Current.IsVisible(ship)) return false;
            scene = SceneManager.GetActiveScene(); return scene.IsValid() && scene.isLoaded;
        }
        #endregion
    }
}
