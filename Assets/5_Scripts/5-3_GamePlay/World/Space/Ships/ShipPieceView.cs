using System;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class ShipPieceView : MonoBehaviour, IGridInteractionTarget, IWorldInteractionPreview, ISpatialInteractionShape
    {
        #region 船体表现与正式交互
        public Item Item { get; private set; }
        private SpaceSession session;
        private string shipId, pieceId;
        private string machineScopeKey;
        private int targetGuid;
        private MachineInteractionTarget machineTarget;
        private MachineDefinition machineDefinition;
        private SpriteRenderer visual;
        private Vector3 baseVisualPosition;
        private Rect interactionBounds;
        private Vector3 interactionLift;
        private SpriteRenderer[] tankParts;
        private Sprite tankSource;
        private int tankMask = -1;
        private double tankCellSize;
        private int pipeMask = -1;
        private Sprite pipeSource;
        private ShipState boundShip;
        private ShipPieceState boundPiece;
        private int boundWidth, boundHeight, boundQuarterTurns, boundCellX, boundCellY;
        private double boundCellSize;
        private bool pendingMachineBinding;
        private bool registeredForInteraction;
        private ShipEngineExhaustPresenter engineExhaust;
        public Vector3 WorldPosition => transform.position + transform.TransformVector(interactionLift);
        public int TargetGuid => targetGuid;
        internal string BoundMachineScopeKey => machineScopeKey;
        public bool IsValid => isActiveAndEnabled && session != null && session.FindPiece(pieceId, out _, out _);
        public bool IsInteractionHighlighted { get; private set; }
        public event Action<bool> InteractionHighlightChanged;
        // 船上线路和设备参与同格分区，地板、墙和纯能力部件不占交互份数。
        private bool HasInteractionAction => boundPiece != null &&
            boundPiece.Kind is not (ShipPieceKind.Floor or ShipPieceKind.Wall or ShipPieceKind.Navigation) &&
            ((boundPiece.Kind is ShipPieceKind.Console or ShipPieceKind.DockingPort or
                ShipPieceKind.Engine or ShipPieceKind.Door) ||
                machineTarget != null);
        public bool CanInteract(Item actor)
        {
            if (actor == null || !HasInteractionAction || !IsValid) return false;
            float distance = session.IsSpaceView
                ? Vector2.Distance(actor.transform.position, WorldPosition)
                : WorldTopologyRuntime.Distance(actor.transform.position, WorldPosition);
            return distance <= SpaceSession.ConsoleInteractionDistance;
        }
        // 船上设备和舱门统一由交互键操作，鼠标左键不打开面板。
        public bool CanPointerInteract(Item actor) => false;
        public bool ContainsInteractionPoint(Vector2 point) => interactionBounds.Contains(
            (Vector2)transform.InverseTransformPoint(point - (Vector2)transform.TransformVector(interactionLift)));

        // 使用船体本地格分区，转向、升降和多格设备仍共享同一套网格身份。
        public bool TryGetInteractionGridHit(Vector2 pointer, WorldTopologyDomain topology, out GridInteractionHit hit)
        {
            hit = default;
            if (boundPiece == null || boundCellSize <= 0d) return false;
            Vector2 projected = session.IsSpaceView ? pointer :
                (Vector2)transform.position + (Vector2)topology.ShortestDelta((Vector2)transform.position, pointer);
            Vector3 local = transform.InverseTransformPoint(projected - (Vector2)transform.TransformVector(interactionLift));
            float x = local.x / (float)boundCellSize + .5f;
            float y = local.y / (float)boundCellSize + .5f;
            int cellX = Mathf.FloorToInt(x), cellY = Mathf.FloorToInt(y);
            if (cellX < 0 || cellX >= boundWidth || cellY < 0 || cellY >= boundHeight) return false;
            hit = new GridInteractionHit(machineScopeKey, new Vector2Int(boundCellX + cellX, boundCellY + cellY),
                machineDefinition?.VisualLayer ?? 0, y - cellY);
            return true;
        }
        public void SetInteractionHighlighted(bool highlighted)
        {
            IsInteractionHighlighted = highlighted; InteractionHighlightChanged?.Invoke(highlighted);
        }
        public void Initialize(SpaceSession owner, ShipState ship, ShipPieceState piece, Item instance)
        {
            session = owner; shipId = ship.ShipId; pieceId = piece.PieceId; Item = instance;
            // 船件身份在绑定时解析，逐帧表现与交互复用稳定的机器作用域和 GUID。
            machineScopeKey = MachineWorld.GetShipScopeKey(shipId);
            targetGuid = int.TryParse(pieceId, out int id) ? id : 0;
            visual = GetComponentInChildren<SpriteRenderer>();
            baseVisualPosition = visual != null ? visual.transform.localPosition : Vector3.zero;
            Rebind(ship, piece);
        }
        // 休眠只保留表现对象和稳定身份，不保留旧世界的节点、交互与权威对象引用。
        internal void SuspendView()
        {
            SetInteractionHighlighted(false);
            InteractionHighlightChanged = null;
            SpatialInteractionRegistry.Unregister(this);
            registeredForInteraction = false;
            gameObject.SetActive(false);
            ReleaseDoorVisual();
            ReleaseCultivatorVisual();
            cultivatorPlots.Refresh(null);
            ReleaseBatteryChargeVisual();
            ReleaseElectricLightVisual();
            engineExhaust?.Bind(null, null, null);
            session = null; boundShip = null; boundPiece = null;
            machineTarget = null; machineDefinition = null;
            machineScopeKey = null;
            surfaceCollider = null; surfaceSortingBound = false;
            pendingMachineBinding = true;
        }

        internal void ResumeView(SpaceSession owner, ShipState ship, ShipPieceState piece, ItemData data)
        {
            session = owner; shipId = ship.ShipId; pieceId = piece.PieceId;
            machineScopeKey = MachineWorld.GetShipScopeKey(shipId);
            targetGuid = int.TryParse(pieceId, out int id) ? id : 0;
            tankMask = pipeMask = -1;
            surfaceCollider = null; surfaceSortingBound = false;
            RefreshDefinitionVisual(piece, data);
            gameObject.SetActive(true);
            Rebind(ship, piece, true);
        }

        private void RefreshDefinitionVisual(ShipPieceState piece, ItemData data)
        {
            if (visual == null) return;
            MachineDefinition machine = MachineCatalog.Get(piece.ItemId);
            bool connectionLine = machine?.IsConnectionLine == true;
            WorldSortingManager.GetInstance().ApplyRenderer(visual,
                piece.Kind == ShipPieceKind.Floor || connectionLine
                    ? WorldSortingManager.VehicleCategory : WorldSortingManager.BuildingCategory,
                piece.Kind == ShipPieceKind.Floor ? -2 : machine?.Fluid?.Kind == "pipe" ? -1 : 0);
            if (GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition definition))
            {
                visual.sprite = definition.Sprite;
                visual.color = definition.Visual?.Color ?? Color.white;
                visual.transform.localScale = definition.Visual?.RendererLocalScale ?? Vector3.one;
                if (definition.Material != null) visual.sharedMaterial = definition.Material;
            }
            if (Mod_ShipPart.Read(data) == null &&
                Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building) &&
                !string.IsNullOrWhiteSpace(building.TileBlockId) &&
                GameRes.ExistingInstance.GetTileBlock(building.TileBlockId)?.TileBase is UnityEngine.Tilemaps.Tile tile)
            {
                visual.sprite = tile.sprite; visual.color = tile.color;
                visual.transform.localScale = tile.transform.lossyScale;
            }
        }
        public void Rebind(ShipState ship, ShipPieceState piece, bool force = false)
        {
            int width = piece.QuarterTurns % 2 == 0 ? piece.Width : piece.Height;
            int height = piece.QuarterTurns % 2 == 0 ? piece.Height : piece.Width;
            if (!force && !pendingMachineBinding && ReferenceEquals(boundShip, ship) && ReferenceEquals(boundPiece, piece) &&
                boundWidth == width && boundHeight == height && boundQuarterTurns == piece.QuarterTurns &&
                boundCellX == piece.CellX && boundCellY == piece.CellY &&
                boundCellSize == ship.CellSizeMeters)
                return;

            boundShip = ship; boundPiece = piece; boundWidth = width; boundHeight = height;
            boundQuarterTurns = piece.QuarterTurns; boundCellSize = ship.CellSizeMeters;
            boundCellX = piece.CellX; boundCellY = piece.CellY;
            if (machineScopeKey == null || shipId != ship.ShipId)
                machineScopeKey = MachineWorld.GetShipScopeKey(ship.ShipId);
            shipId = ship.ShipId;
            Vector2 size = new((float)Math.Max(.001d, width * ship.CellSizeMeters - .05d),
                (float)Math.Max(.001d, height * ship.CellSizeMeters - .05d));
            Vector2 offset = new((float)((width - 1) * ship.CellSizeMeters * .5d), (float)((height - 1) * ship.CellSizeMeters * .5d));
            interactionBounds = new Rect(offset - size * .5f, size);
            transform.localPosition = new Vector3((float)((piece.CellX + .5d) * ship.CellSizeMeters),
                (float)((piece.CellY + .5d) * ship.CellSizeMeters), 0f);
            transform.localRotation = Quaternion.identity;
            if (visual != null)
            {
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, piece.QuarterTurns * 90f);
                baseVisualPosition.x = offset.x; baseVisualPosition.y = offset.y;
                if (piece.Kind == ShipPieceKind.Engine)
                {
                    if (engineExhaust == null) engineExhaust = visual.gameObject.AddComponent<ShipEngineExhaustPresenter>();
                    engineExhaust.Bind(session, piece, visual);
                }
            }
            machineTarget = null;
            machineDefinition = MachineCatalog.Get(piece.ItemId);
            pendingMachineBinding = false;
            if (machineDefinition != null)
                using (MachineWorld.UseScope(machineScopeKey))
                {
                    MachineEntity node = MachineWorld.GetById(TargetGuid);
                    pendingMachineBinding = node == null;
                    machineTarget = MachineWorld.GetOrCreateInteractionTarget(node);
                    session.TrackShipMachineVisualTopology(node);
                    SynchronizeTankVisual(ship, node);
                    BindCultivatorVisual(node);
                    BindBatteryChargeVisual(node);
                    BindElectricLightVisual(node);
                    SynchronizePipeVisual(node);
                }
            else
            {
                BindCultivatorVisual(null);
                BindBatteryChargeVisual(null);
                BindElectricLightVisual(null);
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

        // 船上管线只读本船端口连通，形状随船旋转而不随管格摆放旋转。
        internal void SynchronizePipeVisual(MachineEntity node)
        {
            if (visual == null || node?.Definition.Fluid?.Kind != "pipe" || boundPiece?.IsAlive != true ||
                node.ScopeKey != machineScopeKey) return;
            using var nodeScope = MachineWorld.UseNodeScope(node);
            int mask = MachineWorld.GetFluidPipeConnectionMask(node);
            GameRes resources = GameRes.ExistingInstance;
            if (resources == null || !resources.TryGetItemDefinition(node.Definition.Id, out RuntimeItemDefinition definition))
                throw new InvalidOperationException("船上管道物品定义缺失：" + node.Definition.Id);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = new Vector3((float)boundShip.CellSizeMeters, (float)boundShip.CellSizeMeters, 1f);
            if (pipeMask == mask && pipeSource == definition.Sprite) return;
            if (!definition.TryGetVisualStateSprite("pipe" + mask, out Sprite connected))
                throw new InvalidOperationException("船上管道连接贴图缺失：" + node.Definition.Id + "/pipe" + mask);
            visual.sprite = connected;
            pipeMask = mask;
            pipeSource = definition.Sprite;
        }
        public void SetVisualLift(Vector3 visualLiftOffset)
        {
            interactionLift = visualLiftOffset;
            if (visual != null)
            {
                Vector3 local = baseVisualPosition + visualLiftOffset;
                if (visual.transform.localPosition != local) visual.transform.localPosition = local;
            }
            SynchronizeDoorVisual();
            SynchronizeSurfacePresentation(visualLiftOffset);
            SynchronizeAirSupplyIndicator();
        }
        public void OnInteractStart(Item actor)
        {
            if (!CanInteract(actor) || actor is not Player player) return;
            session.FindPiece(pieceId, out ShipState ship, out ShipPieceState piece);
            if (piece.Kind == ShipPieceKind.Console || piece.Kind == ShipPieceKind.DockingPort || piece.Kind == ShipPieceKind.Engine)
            { ShipPanelSession.Open(session, player, shipId, pieceId); return; }
            if (piece.Kind == ShipPieceKind.Door)
            { session.TryToggleDoor(pieceId); return; }
            using (MachineWorld.UseScope(machineScopeKey))
            {
                // 休眠会释放旧交互对象，实际操作时从本船节点重新取得当前对象。
                MachineEntity node = MachineWorld.GetById(TargetGuid);
                machineTarget = MachineWorld.GetOrCreateInteractionTarget(node);
                machineTarget?.OnInteractStart(actor);
            }
        }
        public void OnInteractUpdate(Item actor)
        {
            if (machineTarget == null) return;
            using (MachineWorld.UseScope(machineScopeKey))
                if (machineTarget.IsValid) machineTarget.OnInteractUpdate(actor);
        }
        public void OnInteractEnd(Item actor)
        {
            if (machineTarget == null) return;
            using (MachineWorld.UseScope(machineScopeKey))
                if (machineTarget.IsValid) machineTarget.OnInteractEnd(actor);
        }
        public void OnInteractCancel(Item actor) { if (machineTarget != null) using (MachineWorld.UseScope(machineScopeKey)) machineTarget.OnInteractCancel(actor); }
        private void OnDestroy()
        {
            SpatialInteractionRegistry.Unregister(this); InteractionHighlightChanged = null;
            ReleaseDoorVisual();
            ReleaseCultivatorVisual();
            ReleaseBatteryChargeVisual();
            ReleaseElectricLightVisual();
        }
        #endregion
    }
    public sealed partial class SpaceSession
    {
        #region 船上管线事件刷新
        private readonly System.Collections.Generic.Dictionary<int,
            (string Scope, Vector2Int Cell, int Turns, bool Enabled, bool Ruptured)> shipMachineVisualTopology = new();
        private bool shipMachineVisualEventsBound;

        private void BindShipMachineVisualEvents()
        {
            if (shipMachineVisualEventsBound) return;
            MachineWorld.NodeStateChanged += HandleShipMachineVisualTopologyChanged;
            MachineWorld.NodeRemoved += HandleShipMachineVisualNodeRemoved;
            shipMachineVisualEventsBound = true;
            viewBindingsDirty = true;
        }

        private void UnbindShipMachineVisualEvents()
        {
            MachineWorld.NodeStateChanged -= HandleShipMachineVisualTopologyChanged;
            MachineWorld.NodeRemoved -= HandleShipMachineVisualNodeRemoved;
            shipMachineVisualEventsBound = false;
            shipMachineVisualTopology.Clear();
        }

        private void OnDisable()
        {
            ResetPresentationHistory();
            UnbindShipMachineVisualEvents();
            ReleaseSurfaceShadows();
        }

        internal void TrackShipMachineVisualTopology(MachineEntity node)
        {
            if (node?.Definition.Fluid == null) return;
            using var nodeScope = MachineWorld.UseNodeScope(node);
            shipMachineVisualTopology[node.Id] = (node.ScopeKey, node.Cell, node.RotationQuarterTurns,
                MachineWorld.GetFluidDeviceBehavior(node).PortsEnabled(node), MachineWorld.GetFluidState(node).Ruptured);
        }

        // 库存读数变化不刷新邻格，只有端口拓扑签名变化才重查连接形状。
        private void HandleShipMachineVisualTopologyChanged(MachineEntity node)
        {
            if (State == null || node?.Definition.Fluid == null ||
                node.ScopeKey?.StartsWith("ship:", StringComparison.Ordinal) != true) return;
            bool known = shipMachineVisualTopology.TryGetValue(node.Id, out var previous);
            TrackShipMachineVisualTopology(node);
            var current = shipMachineVisualTopology[node.Id];
            if (known && previous.Equals(current)) return;
            if (known && (previous.Scope != current.Scope || previous.Cell != current.Cell))
                RefreshShipFluidPipeNeighbours(previous.Scope, previous.Cell);
            RefreshShipFluidPipeNeighbours(current.Scope, current.Cell);
        }

        private void HandleShipMachineVisualNodeRemoved(int id)
        {
            if (!shipMachineVisualTopology.Remove(id, out var previous)) return;
            RefreshShipFluidPipeNeighbours(previous.Scope, previous.Cell);
        }

        private void RefreshShipFluidPipeNeighbours(string scopeKey, Vector2Int cell)
        {
            if (State == null || string.IsNullOrWhiteSpace(scopeKey)) return;
            using var shipScope = MachineWorld.UseScope(scopeKey);
            RefreshShipFluidPipeVisual(cell);
            foreach (Vector2Int direction in MechanicalNetworkGraph.Directions)
                RefreshShipFluidPipeVisual(cell + direction);
        }

        private void RefreshShipFluidPipeVisual(Vector2Int cell)
        {
            MachineEntity pipe = MachineWorld.GetFluidPipeAt(cell);
            if (pipe?.Definition.Fluid?.Kind == "pipe" &&
                views.TryGetValue(pipe.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), out ShipPieceView view) && view != null)
                view.SynchronizePipeVisual(pipe);
        }
        #endregion

        #region 动态本地格投影
        private bool viewBindingsDirty;
        public Vector3 ProjectMachinePosition(MachineEntity node)
        {
            if (node?.ScopeKey == null || !node.ScopeKey.StartsWith("ship:", StringComparison.Ordinal)) return node?.Snapshot.transform.position ?? default;
            ShipState ship = GetShip(node.ScopeKey.Substring(5));
            return ship == null ? node.Snapshot.transform.position : DisplayPresentationPoint(ship, (node.Cell.x + .5d) * ship.CellSizeMeters, (node.Cell.y + .5d) * ship.CellSizeMeters);
        }
        private void SynchronizeViews(bool refreshBindings = false)
        {
            if (transitionRunning || DimensionManager.ExistingInstance?.IsTransitioning == true ||
                GameManager.Instance?.IsWorldEntryInProgress == true) return;
            EnsureShipViewResourceGeneration();
            if (FlatWorld.Networking.GameNetwork.IsOnline) ClearShipViewPool();
            BindShipMachineVisualEvents();
            bool forceBindings = refreshBindings || viewBindingsDirty;
            viewBindingsDirty = false;
            visibleSurfaceShadows.Clear();
            visibleShipColliderPresenters.Clear();
            visibleShipPieceIds.Clear();
            foreach (ShipState ship in State.Ships)
            {
                if (ship.IsWreck)
                {
                    foreach (ShipPieceState piece in ship.Pieces) RemovePieceView(piece.PieceId);
                    SynchronizeWreckView(ship); continue;
                }
                bool visible = IsVisible(ship);
                ShipFlightState flight = visible ? GetFlight(ship.ShipId) : null;
                float angle = (float)(GetShipPresentationPose(ship).AngleRadians * 180d / Math.PI);
                ShipColliderPresenter presenter = visible ? SynchronizeShipColliderPresenter(ship, angle) : null;
                double height = flight != null && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending
                    ? flight.HeightMeters : 0d;
                SynchronizeSurfaceShadow(ship, visible, height, forceBindings);
                // 同一船体所有部件共用一次视觉升高偏移，避免逐部件计算变换矩阵。
                float rise = (float)ShipLandingGuidance.SurfaceVisualRise(height);
                float radians = angle * Mathf.Deg2Rad;
                Vector3 visualLiftOffset = new(Mathf.Sin(radians) * rise, Mathf.Cos(radians) * rise, 0f);
                presenter?.SynchronizeSurfaceState(flight, visualLiftOffset);
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive) { RemovePieceView(piece.PieceId); continue; }
                    if (!visible) { SuspendPieceView(piece.PieceId); continue; }
                    visibleShipPieceIds.Add(piece.PieceId);
                    if (!views.TryGetValue(piece.PieceId, out ShipPieceView view) || view == null)
                    {
                        if (TryRestorePieceView(ship, piece, presenter, out view))
                        {
                            views[piece.PieceId] = view;
                        }
                        else
                        {
                            ItemData data = GetPieceData(piece);
                            if (data == null) continue;
                            // 固定部件只投影权威快照，避免普通建筑模块重新登记地表和重复产生库存。
                            GameObject root = new("ShipPiece_" + piece.ItemId);
                            root.transform.SetParent(presenter.Root.transform, false);
                            GameObject visual = new("Render"); visual.transform.SetParent(root.transform, false);
                            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
                            // 船体承载层依次绘制地板、管道、电线，统一压在机器和乘员下面。
                            MachineDefinition machineDefinition = MachineCatalog.Get(piece.ItemId);
                            bool connectionLine = machineDefinition?.IsConnectionLine == true;
                            WorldSortingManager.GetInstance().ApplyRenderer(renderer,
                                piece.Kind == ShipPieceKind.Floor || connectionLine
                                    ? WorldSortingManager.VehicleCategory : WorldSortingManager.BuildingCategory,
                                piece.Kind == ShipPieceKind.Floor ? -2 : machineDefinition?.Fluid?.Kind == "pipe" ? -1 : 0);
                            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                            if (GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition definition))
                            {
                                renderer.sprite = definition.Sprite; renderer.color = definition.Visual?.Color ?? Color.white;
                                // 船上设备复用物品定义的缩放，让放置预览与船体实体保持相同尺寸。
                                visual.transform.localScale = definition.Visual?.RendererLocalScale ?? Vector3.one;
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
                            // 专用船件保留定义外观，普通格子建筑才使用地块贴图覆盖。
                            if (Mod_ShipPart.Read(data) == null &&
                                Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building) && !string.IsNullOrWhiteSpace(building.TileBlockId) &&
                                GameRes.ExistingInstance.GetTileBlock(building.TileBlockId)?.TileBase is UnityEngine.Tilemaps.Tile tile)
                            { renderer.sprite = tile.sprite; renderer.color = tile.color; visual.transform.localScale = tile.transform.lossyScale; visual.transform.localPosition = tile.transform.GetColumn(3); }
                            view = root.AddComponent<ShipPieceView>(); view.Initialize(this, ship, piece, null);
                            views[piece.PieceId] = view;
                            shipPieceViewsCreated++;
                        }
                    }
                    if (view.transform.parent != presenter.Root.transform) view.transform.SetParent(presenter.Root.transform, false);
                    view.Rebind(ship, piece, forceBindings);
                    view.SetVisualLift(visualLiftOffset);
                }
            }
            RemoveExpiredPieceViews();
            RemoveExpiredShipColliderPresenters();
            RemoveExpiredSurfaceShadows();
            SynchronizeRemotePassengerSurfaceVisuals();
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null) return;
            SpacePassengerState passenger = GetPassenger(player);
            SetTerrainSuppression(player, IsSpaceView || passenger.Supported || passenger.SurfaceDescending);
            ShipState carrier = GetShip(passenger.ShipId);
            // 支撑乘员始终跟随当前世界的船体，不能因美术裁剪改读旧自由漂浮坐标。
            if (passenger.Supported && IsInPresentationWorld(carrier)) PlacePassenger(player, passenger, carrier, true);
            else if (IsPassengerSurfaceDescendingHere(passenger)) PlaceDescendingPassenger(player, passenger);
            else if (!passenger.Supported && IsSpaceView && passenger.IsInSpace && !passenger.LandingResolved)
            {
                RestorePassengerSurface(player);
                Vector2 position = GetPassengerPresentationPosition(passenger, null);
                player.transform.position = new Vector3(position.x, position.y, 0f);
                Rigidbody2D playerBody = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.rb;
                if (playerBody != null) playerBody.position = position;
            }
            else RestorePassengerSurface(player);
            UpdateDrivingCamera(player, passenger, carrier);
        }
        private void RemovePieceView(string id)
        {
            RemovePooledPieceView(id);
            if (!views.Remove(id, out ShipPieceView view) || view == null) return;
            DestroyPieceView(view);
        }
        private void ReleaseViews()
        {
            ReleasePassengerSurfacePresentation();
            ReleaseSurfaceShadows();
            foreach (string id in new System.Collections.Generic.List<string>(wreckViews.Keys)) RemoveWreckView(id);
            ReleaseDrivingCamera();
            UnbindShipMachineVisualEvents();
            if (State != null) ReleaseLooseViews();
            foreach (string id in new System.Collections.Generic.List<string>(views.Keys)) RemovePieceView(id);
            ReleaseShipColliderPresenters();
        }

        public void CollectAssemblyPresentationRenderers(string shipId, System.Collections.Generic.List<SpriteRenderer> renderers)
        {
            foreach (var pair in views)
                if (pair.Value != null && FindPiece(pair.Key, out ShipState ship, out _) && SameAssembly(ship.ShipId, shipId))
                    renderers.AddRange(pair.Value.GetComponentsInChildren<SpriteRenderer>());
        }
        #endregion
    }
}
