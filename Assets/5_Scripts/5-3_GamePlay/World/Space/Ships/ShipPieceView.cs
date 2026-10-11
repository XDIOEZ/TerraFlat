using System;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class ShipPieceView : MonoBehaviour, IWorldInteractionTarget, IWorldInteractionPreview
    {
        #region 船体表现与正式交互
        public Item Item { get; private set; }
        private SpaceSession session;
        private string shipId, pieceId;
        private MachineInteractionTarget machineTarget;
        private SpriteRenderer visual;
        private Rigidbody2D body;
        private Vector3 baseVisualPosition;
        private BoxCollider2D hitBox;
        private SpriteRenderer[] tankParts;
        private Sprite tankSource;
        private int tankMask = -1;
        private double tankCellSize;
        private ShipState boundShip;
        private ShipPieceState boundPiece;
        private int boundWidth, boundHeight, boundQuarterTurns;
        private double boundCellSize;
        private bool pendingMachineBinding;
        private bool registeredForInteraction;
        public Vector3 WorldPosition => transform.position;
        public int TargetGuid => int.TryParse(pieceId, out int id) ? id : 0;
        public bool IsValid => session != null && session.FindPiece(pieceId, out _, out _);
        public bool IsInteractionHighlighted { get; private set; }
        public event Action<bool> InteractionHighlightChanged;
        private bool HasInteractionAction => boundPiece != null &&
            boundPiece.Kind is not (ShipPieceKind.Floor or ShipPieceKind.Wall) &&
            ((boundPiece.Kind is ShipPieceKind.Console or ShipPieceKind.DockingPort or ShipPieceKind.Navigation or
                ShipPieceKind.Engine or ShipPieceKind.Door) || machineTarget != null);
        public bool CanInteract(Item actor)
        {
            if (actor == null || !HasInteractionAction || !IsValid) return false;
            float distance = session.IsSpaceView
                ? Vector2.Distance(actor.transform.position, WorldPosition)
                : WorldTopologyRuntime.Distance(actor.transform.position, WorldPosition);
            return distance <= SpaceSession.ConsoleInteractionDistance;
        }
        public bool CanPointerInteract(Item actor) => true;
        public void SetInteractionHighlighted(bool highlighted)
        {
            IsInteractionHighlighted = highlighted; InteractionHighlightChanged?.Invoke(highlighted);
        }
        public void Initialize(SpaceSession owner, ShipState ship, ShipPieceState piece, Item instance)
        {
            session = owner; shipId = ship.ShipId; pieceId = piece.PieceId; Item = instance;
            visual = GetComponentInChildren<SpriteRenderer>(); body = GetComponent<Rigidbody2D>(); hitBox = GetComponent<BoxCollider2D>();
            baseVisualPosition = visual != null ? visual.transform.localPosition : Vector3.zero;
            Rebind(ship, piece);
        }
        public void Rebind(ShipState ship, ShipPieceState piece, bool force = false)
        {
            int width = piece.QuarterTurns % 2 == 0 ? piece.Width : piece.Height;
            int height = piece.QuarterTurns % 2 == 0 ? piece.Height : piece.Width;
            if (!force && !pendingMachineBinding && ReferenceEquals(boundShip, ship) && ReferenceEquals(boundPiece, piece) &&
                boundWidth == width && boundHeight == height && boundQuarterTurns == piece.QuarterTurns &&
                boundCellSize == ship.CellSizeMeters)
                return;

            boundShip = ship; boundPiece = piece; boundWidth = width; boundHeight = height;
            boundQuarterTurns = piece.QuarterTurns; boundCellSize = ship.CellSizeMeters;
            shipId = ship.ShipId;
            hitBox.size = new Vector2((float)(width * ship.CellSizeMeters - .05d), (float)(height * ship.CellSizeMeters - .05d));
            hitBox.offset = new Vector2((float)((width - 1) * ship.CellSizeMeters * .5d), (float)((height - 1) * ship.CellSizeMeters * .5d));
            if (visual != null)
            {
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, piece.QuarterTurns * 90f);
                baseVisualPosition.x = hitBox.offset.x; baseVisualPosition.y = hitBox.offset.y;
            }
            machineTarget = null;
            pendingMachineBinding = false;
            if (MachineCatalog.Get(piece.ItemId) != null)
                using (MachineWorld.UseScope("ship:" + shipId))
                {
                    MachineEntity node = MachineWorld.GetById(TargetGuid);
                    pendingMachineBinding = node == null;
                    machineTarget = MachineWorld.GetOrCreateInteractionTarget(node);
                    SynchronizeTankVisual(ship, node);
                }
            // 只登记真正能交互的船体设备，避免地板和墙壁进入每帧目标查询。
            if (HasInteractionAction)
            {
                SpatialInteractionRegistry.Register(this, Mathf.Max(width, height) * (float)ship.CellSizeMeters * .5f + .2f, this);
                registeredForInteraction = true;
            }
            else if (registeredForInteraction)
            {
                SpatialInteractionRegistry.Unregister(this);
                registeredForInteraction = false;
            }
        }

        // 船上储罐使用同一权威本地格连接，只在外壳变化时重选九宫格子图。
        private void SynchronizeTankVisual(ShipState ship, MachineEntity node)
        {
            if (visual == null) return;
            if (!MachineWorld.IsFluidTankBlock(node))
            {
                if (tankParts != null)
                    foreach (SpriteRenderer part in tankParts) part.enabled = false;
                visual.enabled = true;
                tankMask = -1;
                return;
            }
            visual.enabled = false;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            int mask = MachineWorld.GetFluidTankConnectionMask(node);
            if (tankParts != null && tankMask == mask && tankSource == visual.sprite &&
                tankCellSize == ship.CellSizeMeters && tankParts[0].sprite != null) return;
            tankParts ??= new SpriteRenderer[9];
            for (int slice = 0; slice < tankParts.Length; slice++)
            {
                SpriteRenderer part = tankParts[slice];
                if (part == null)
                {
                    GameObject child = new("TankPart_" + slice);
                    child.transform.SetParent(visual.transform, false);
                    part = tankParts[slice] = child.AddComponent<SpriteRenderer>();
                    part.spriteSortPoint = SpriteSortPoint.Pivot;
                }
                FluidTankBlockVisual.GetPart(visual.sprite, slice, mask,
                    out Sprite sprite, out Vector3 offset, out Vector3 scale);
                part.sprite = sprite;
                part.color = visual.color;
                part.sharedMaterial = visual.sharedMaterial;
                part.sortingLayerID = visual.sortingLayerID;
                part.sortingOrder = visual.sortingOrder;
                part.transform.localPosition = offset * (float)ship.CellSizeMeters;
                part.transform.localScale = new Vector3(scale.x * (float)ship.CellSizeMeters,
                    scale.y * (float)ship.CellSizeMeters, 1f);
                part.enabled = true;
            }
            tankMask = mask;
            tankSource = visual.sprite;
            tankCellSize = ship.CellSizeMeters;
        }
        public void SetPose(Vector2 position, float degrees, Vector3 visualLiftOffset)
        {
            // 静止船体不重复写入刚体与 Transform，避免无意义的物理同步。
            if (body.position != position || Mathf.Abs(Mathf.DeltaAngle(body.rotation, degrees)) > .001f ||
                (Vector2)transform.position != position)
            {
                body.position = position; body.rotation = degrees;
                transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, degrees));
            }
            if (visual != null)
            {
                Vector3 local = baseVisualPosition + visualLiftOffset;
                if (visual.transform.localPosition != local) visual.transform.localPosition = local;
            }
            SynchronizeDoorVisual();
            SynchronizePassageCollider();
        }
        public void OnInteractStart(Item actor)
        {
            if (!CanInteract(actor) || actor is not Player player) return;
            session.FindPiece(pieceId, out ShipState ship, out ShipPieceState piece);
            if (piece.Kind == ShipPieceKind.Console || piece.Kind == ShipPieceKind.DockingPort || piece.Kind == ShipPieceKind.Navigation || piece.Kind == ShipPieceKind.Engine)
            { ShipPanelSession.Open(session, player, shipId, pieceId); return; }
            if (piece.Kind == ShipPieceKind.Door)
            { session.TryToggleDoor(pieceId); return; }
            if (machineTarget != null) using (MachineWorld.UseScope("ship:" + shipId)) machineTarget.OnInteractStart(actor);
        }
        public void OnInteractUpdate(Item actor) { if (machineTarget != null) using (MachineWorld.UseScope("ship:" + shipId)) machineTarget.OnInteractUpdate(actor); }
        public void OnInteractEnd(Item actor) { if (machineTarget != null) using (MachineWorld.UseScope("ship:" + shipId)) machineTarget.OnInteractEnd(actor); }
        public void OnInteractCancel(Item actor) { if (machineTarget != null) using (MachineWorld.UseScope("ship:" + shipId)) machineTarget.OnInteractCancel(actor); }
        private void OnDestroy()
        {
            SpatialInteractionRegistry.Unregister(this); InteractionHighlightChanged = null;
            ReleaseDoorVisual();
        }
        #endregion
    }
    public sealed partial class SpaceSession
    {
        #region 动态本地格投影
        private bool viewBindingsDirty;
        public Vector3 ProjectMachinePosition(MachineEntity node)
        {
            if (node?.ScopeKey == null || !node.ScopeKey.StartsWith("ship:", StringComparison.Ordinal)) return node?.Snapshot.transform.position ?? default;
            ShipState ship = GetShip(node.ScopeKey.Substring(5));
            return ship == null ? node.Snapshot.transform.position : DisplayPoint(ship, (node.Cell.x + .5d) * ship.CellSizeMeters, (node.Cell.y + .5d) * ship.CellSizeMeters);
        }
        private void SynchronizeViews(bool refreshBindings = false)
        {
            bool forceBindings = refreshBindings || viewBindingsDirty;
            viewBindingsDirty = false;
            foreach (ShipState ship in State.Ships)
            {
                bool visible = IsVisible(ship);
                ShipFlightState flight = visible ? GetFlight(ship.ShipId) : null;
                float angle = (float)(ship.AngleRadians * 180d / Math.PI);
                double height = flight != null && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending
                    ? flight.HeightMeters : 0d;
                // 同一船体所有部件共用一次视觉升高偏移，避免逐部件计算变换矩阵。
                float rise = (float)Math.Min(4d, Math.Max(0d, height) * .04d);
                float radians = angle * Mathf.Deg2Rad;
                Vector3 visualLiftOffset = new(Mathf.Sin(radians) * rise, Mathf.Cos(radians) * rise, 0f);
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive || !visible) { RemovePieceView(piece.PieceId); continue; }
                    if (!views.TryGetValue(piece.PieceId, out ShipPieceView view) || view == null)
                    {
                        ItemData data = GetPieceData(piece);
                        if (data == null) continue;
                        // 固定部件只投影权威快照，避免普通建筑模块重新登记地表和重复产生库存。
                        GameObject root = new("ShipPiece_" + piece.ItemId);
                        GameObject visual = new("Render"); visual.transform.SetParent(root.transform, false);
                        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
                        // 船体承载层复用交通工具排序，地板覆盖地表且始终低于乘员和设备。
                        WorldSortingManager.GetInstance().ApplyRenderer(renderer, piece.Kind == ShipPieceKind.Floor
                            ? WorldSortingManager.VehicleCategory : WorldSortingManager.BuildingCategory);
                        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                        if (GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition definition))
                        {
                            renderer.sprite = definition.Sprite; renderer.color = definition.Visual?.Color ?? Color.white;
                            if (definition.Material != null) renderer.sharedMaterial = definition.Material;
                        }
                        if (piece.Kind == ShipPieceKind.Engine)
                        {
                            GameObject marker = new("推力方向"); marker.transform.SetParent(visual.transform, false);
                            LineRenderer arrow = marker.AddComponent<LineRenderer>(); arrow.useWorldSpace = false;
                            arrow.sharedMaterial = renderer.sharedMaterial; arrow.startWidth = arrow.endWidth = .05f;
                            arrow.startColor = arrow.endColor = Color.cyan;
                            arrow.sortingLayerID = renderer.sortingLayerID; arrow.sortingOrder = renderer.sortingOrder + 2; arrow.positionCount = 5;
                            arrow.SetPositions(new[] { new Vector3(0, -.3f), new Vector3(0, .4f), new Vector3(-.13f, .23f), new Vector3(0, .4f), new Vector3(.13f, .23f) });
                        }
                        if (Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building) && !string.IsNullOrWhiteSpace(building.TileBlockId) &&
                            GameRes.ExistingInstance.GetTileBlock(building.TileBlockId)?.TileBase is UnityEngine.Tilemaps.Tile tile)
                        { renderer.sprite = tile.sprite; renderer.color = tile.color; visual.transform.localScale = tile.transform.lossyScale; visual.transform.localPosition = tile.transform.GetColumn(3); }
                        BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
                        collider.size = new Vector2((float)(piece.Width * ship.CellSizeMeters - .05d), (float)(piece.Height * ship.CellSizeMeters - .05d));
                        collider.offset = new Vector2((float)((piece.Width - 1) * ship.CellSizeMeters * .5d), (float)((piece.Height - 1) * ship.CellSizeMeters * .5d));
                        collider.isTrigger = !piece.BlocksPassage;
                        Rigidbody2D body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
                        body.gravityScale = 0; body.useFullKinematicContacts = true; body.interpolation = RigidbodyInterpolation2D.Interpolate;
                        int layer = LayerMask.NameToLayer("Collider"); if (layer >= 0) root.layer = layer;
                        view = root.AddComponent<ShipPieceView>(); view.Initialize(this, ship, piece, null);
                        views[piece.PieceId] = view;
                    }
                    Vector2 position = DisplayPoint(ship, (piece.CellX + .5d) * ship.CellSizeMeters, (piece.CellY + .5d) * ship.CellSizeMeters);
                    view.Rebind(ship, piece, forceBindings);
                    view.SetPose(position, angle, visualLiftOffset);
                }
            }
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null) return;
            SpacePassengerState passenger = GetPassenger(player);
            ShipState carrier = GetShip(passenger.ShipId);
            if (passenger.Supported && carrier != null && IsVisible(carrier)) PlacePassenger(player, passenger, carrier);
            else if (IsSpaceView && passenger.IsInSpace && !passenger.LandingResolved)
            {
                Vector2 position = ProjectPosition(passenger.FreeMotion.PositionMeters);
                player.transform.position = new Vector3(position.x, position.y, 0f);
                Rigidbody2D playerBody = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.rb;
                if (playerBody != null) playerBody.position = position;
            }
            ShipFlightState carryingFlight = carrier != null ? GetFlight(carrier.ShipId) : null;
            double visualHeight = passenger.SurfaceDescending ? passenger.SurfaceHeight :
                carryingFlight != null && carryingFlight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending ? carryingFlight.HeightMeters : 0d;
            ApplyPassengerHeightVisual(player, visualHeight);
        }
        private void RemovePieceView(string id)
        {
            if (!views.Remove(id, out ShipPieceView view) || view == null) return;
            if (view.Item != null && ItemMgr.Instance != null) ItemMgr.Instance.DespawnItem(view.Item, false);
            else Destroy(view.gameObject);
        }
        private void ReleaseViews()
        {
            if (State != null) ReleaseLooseViews();
            foreach (string id in new System.Collections.Generic.List<string>(views.Keys)) RemovePieceView(id);
            if (passengerVisual != null) passengerVisual.transform.localPosition = passengerVisualPosition;
            passengerVisual = null;
        }
        private SpriteRenderer passengerVisual;
        private Vector3 passengerVisualPosition;
        private void ApplyPassengerHeightVisual(Player player, double height)
        {
            if (passengerVisual != player.Sprite)
            { passengerVisual = player.Sprite; if (passengerVisual != null) passengerVisualPosition = passengerVisual.transform.localPosition; }
            if (passengerVisual != null)
                passengerVisual.transform.localPosition = passengerVisualPosition + passengerVisual.transform.parent.InverseTransformVector(Vector3.up * (float)Math.Min(4d, Math.Max(0d, height) * .04d));
        }
        #endregion
    }
}
