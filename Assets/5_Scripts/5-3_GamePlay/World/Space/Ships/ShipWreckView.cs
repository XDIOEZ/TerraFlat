using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace FlatWorld.Spaceflight
{
    public sealed class ShipWreckView : GameItem
    {
        #region 一整块残骸的冷载荷与拼装外观
        public ShipState Composition;
        private SpaceSession session;
        private Rigidbody2D body;
        private CustomCollider2D blocker;
        private readonly ShipColliderGeometry geometry = new();
        private readonly PhysicsShapeGroup2D shapes = new();
        private readonly List<(ShipPieceState Piece, SpriteRenderer Renderer)> fragmentSprites = new();
        private double appliedCellSize, appliedCenterX, appliedCenterY;
        private Vector2 appliedVisualLift;
        private bool layoutApplied;

        public void Initialize(SpaceSession session, ShipState ship)
        {
            this.session = session;
            Composition = ship;
            ItemData payload = GameRes.ExistingInstance.CreateItemData("ShipWreck");
            payload.Guid = ship.WreckGuid;
            payload.Stack.CanBePickedUp = false;
            BindData(payload);
            PersistenceOwner = session;
            int layer = LayerMask.NameToLayer("Collider");
            if (layer >= 0) gameObject.layer = layer;
            body = gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.useFullKinematicContacts = true;
            body.interpolation = RigidbodyInterpolation2D.None;
            blocker = gameObject.AddComponent<CustomCollider2D>();
            blocker.enabled = false;
            RefreshPayload();
        }

        public void Synchronize(ShipState ship, Vector2 position, float degrees)
        {
            Composition = ship;
            bool topologyChanged = geometry.Synchronize(ship);
            bool layoutChanged = !layoutApplied || appliedCellSize != ship.CellSizeMeters ||
                appliedCenterX != ship.CenterOfMassLocalX || appliedCenterY != ship.CenterOfMassLocalY;
            if (topologyChanged) RebuildFragmentSprites(ship);
            if (topologyChanged || layoutChanged) RebuildBlockingShape(ship);

            ShipFlightState flight = session.GetFlight(ship.ShipId);
            double rise = ShipSurfaceState.VisualRise(flight);
            float radians = degrees * Mathf.Deg2Rad;
            Vector2 visualLift = new(Mathf.Sin(radians) * (float)rise, Mathf.Cos(radians) * (float)rise);
            if (topologyChanged || layoutChanged || appliedVisualLift != visualLift)
            {
                foreach (var fragment in fragmentSprites)
                {
                    ShipPieceState piece = fragment.Piece;
                    int width = (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height;
                    int height = (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width;
                    fragment.Renderer.transform.localPosition = new Vector3(
                        (float)((piece.CellX + width * .5d) * ship.CellSizeMeters - ship.CenterOfMassLocalX) + visualLift.x,
                        (float)((piece.CellY + height * .5d) * ship.CellSizeMeters - ship.CenterOfMassLocalY) + visualLift.y, 0f);
                }
                appliedVisualLift = visualLift;
            }
            ShipSurfacePresentation.SetElevated(gameObject, ShipSurfaceState.IsElevated(flight), ShipSurfacePresentation.VehicleCategory);
            ShipSurfacePresentation.SetColliderState(blocker, visualLift, ShipSurfaceState.IsAirborne(flight), true);
            if (body.position != position || Mathf.Abs(Mathf.DeltaAngle(body.rotation, degrees)) > .001f ||
                (Vector2)transform.position != position)
            {
                body.position = position; body.rotation = degrees;
                transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, degrees));
            }
            if (topologyChanged) RefreshPayload();
        }

        private void RebuildFragmentSprites(ShipState ship)
        {
            foreach (var fragment in fragmentSprites)
            {
                fragment.Renderer.enabled = false;
                Destroy(fragment.Renderer.gameObject);
            }
            fragmentSprites.Clear(); Sprite = null;
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive || !GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition definition)) continue;
                GameObject child = new("FragmentSprite_" + piece.ItemId);
                child.transform.SetParent(transform, false);
                child.transform.localRotation = Quaternion.Euler(0, 0, piece.QuarterTurns * 90f);
                child.transform.localScale = definition.Visual?.RendererLocalScale ?? Vector3.one;
                SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
                renderer.sprite = definition.Sprite;
                renderer.color = (definition.Visual?.Color ?? Color.white) * new Color(.8f, .8f, .8f, 1f);
                renderer.sharedMaterial = definition.Material;
                WorldRenderingManager.Instance.ApplyRendererSorting(renderer, piece.Kind == ShipPieceKind.Floor
                    ? WorldSortingManager.VehicleCategory : WorldSortingManager.BuildingCategory, piece.Kind == ShipPieceKind.Floor ? -2 : 0);
                Sprite ??= renderer;
                fragmentSprites.Add((piece, renderer));
            }
            // 整块只挂一个 Item，子对象只有贴图，没有原机器、管网和建筑模块。
        }

        private void RebuildBlockingShape(ShipState ship)
        {
            shapes.Clear();
            foreach (ShipCollisionRect rectangle in geometry.Rectangles)
            {
                if (!rectangle.BlocksPassage) continue;
                Vector2 center = new((float)((rectangle.X + rectangle.Width * .5d) * ship.CellSizeMeters - ship.CenterOfMassLocalX),
                    (float)((rectangle.Y + rectangle.Height * .5d) * ship.CellSizeMeters - ship.CenterOfMassLocalY));
                shapes.AddBox(center, new Vector2((float)(rectangle.Width * ship.CellSizeMeters),
                    (float)(rectangle.Height * ship.CellSizeMeters)));
            }
            // 残骸继续复用整船占地合批，破坏部件和地板洞不会留下旧碰撞。
            if (shapes.shapeCount == 0) { blocker.enabled = false; blocker.ClearCustomShapes(); }
            else { blocker.SetCustomShapes(shapes); blocker.enabled = true; }
            appliedCellSize = ship.CellSizeMeters;
            appliedCenterX = ship.CenterOfMassLocalX; appliedCenterY = ship.CenterOfMassLocalY;
            layoutApplied = true;
        }

        public void RefreshPayload()
        {
            // 外壳不维护共享耐久，真实伤害始终落在组成部件上。
            itemData.Durability = itemData.MaxDurability = 0f;
            itemData.ItemSpecialData = JsonConvert.SerializeObject(Composition);
        }

        public void ReleasePhysics()
        {
            if (body != null) body.simulated = false;
            if (blocker != null) blocker.enabled = false;
        }
        #endregion
    }
}
