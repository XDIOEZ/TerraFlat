using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>Exercises one real local-player right-edge wrap before the normal traversal.</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private enum WorldWrapPhase
        {
            None,
            WaitForBoundaryChunk,
            MoveAcrossRightBoundary,
            WaitForWrappedChunk,
            ReadyForVisualCapture,
            WaitForRestoredChunk,
            Completed
        }

        private static WorldWrapPhase _worldWrapPhase;
        private static WorldTopologyBounds _worldWrapBounds;
        private static Vector2 _worldWrapOriginalPosition;
        private static Vector2 _worldWrapOriginalVelocity;
        private static Vector2 _worldWrapBoundaryStart;
        private static Vector2Int _worldWrapExpectedChunk;
        private static float _worldWrapOriginalSpeed;
        private static Mod_ChunkLoader _worldWrapChunkLoader;
        private static Player _worldWrapPlayer;
        private static Mover _worldWrapMover;
        private static WorldWrapEvent _worldWrapObservedEvent;
        private static bool _worldWrapObserved;
        private static float _worldWrapNextDiagnosticTime;
        private static int _worldWrapVisibleReadyCount;
        private static int _worldWrapVisibleRequiredCount;
        private static bool _worldWrapRestored;
        private static bool _worldWrapScenarioCompleted;

        private static void ResetWorldWrapScenario()
        {
            WorldTopologyRuntime.LocalPlayerPositionWrapped -= HandleGoldenPathPlayerWrapped;
            _worldWrapPhase = WorldWrapPhase.None;
            _worldWrapBounds = default;
            _worldWrapOriginalPosition = default;
            _worldWrapOriginalVelocity = default;
            _worldWrapBoundaryStart = default;
            _worldWrapExpectedChunk = default;
            _worldWrapOriginalSpeed = 0f;
            _worldWrapChunkLoader = null;
            _worldWrapPlayer = null;
            _worldWrapMover = null;
            _worldWrapObservedEvent = default;
            _worldWrapObserved = false;
            _worldWrapNextDiagnosticTime = 0f;
            _worldWrapVisibleReadyCount = 0;
            _worldWrapVisibleRequiredCount = 0;
            _worldWrapRestored = false;
            _worldWrapScenarioCompleted = false;
        }

        internal static void BeginWorldWrapScenario(FlatWorldGoldenPathScenarioContext context)
        {
            PlanetData planet = context.SaveDataManager?.Active_PlanetData;
            if (!WorldTopologyBounds.TryCreate(planet, out _worldWrapBounds))
                throw new InvalidOperationException("GoldenPath world is not a valid wrapped world.");
            if (context.Player == null || context.Mover?.rb == null || context.Mover.Speed == null)
                throw new InvalidOperationException("World-wrap scenario requires the real player Rigidbody2D and Mover.");
            if (context.Player.GetComponent<PlayerWorldWrapController>() == null)
                throw new InvalidOperationException("Real player Prefab is missing PlayerWorldWrapController.");

            _worldWrapChunkLoader =
                context.Player.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
            if (_worldWrapChunkLoader == null)
                throw new InvalidOperationException("World-wrap scenario cannot find the real Chunk loader module.");

            _worldWrapPlayer = context.Player;
            _worldWrapMover = context.Mover;
            _worldWrapOriginalPosition = context.Mover.rb.position;
            _worldWrapOriginalVelocity = context.Mover.rb.velocity;
            _worldWrapOriginalSpeed = context.Mover.Speed.BaseValue;
            WorldTopologyRuntime.LocalPlayerPositionWrapped -= HandleGoldenPathPlayerWrapped;
            WorldTopologyRuntime.LocalPlayerPositionWrapped += HandleGoldenPathPlayerWrapped;

            float y = _worldWrapBounds.NormalizePosition(_worldWrapOriginalPosition).y;
            _worldWrapBoundaryStart = new Vector2(_worldWrapBounds.MaxExclusive.x - 0.75f, y);
            SetWorldWrapPlayerPosition(_worldWrapBoundaryStart, Vector2.zero);
            _worldWrapChunkLoader.RefreshChunksAroundPlayer();
            _worldWrapExpectedChunk = ChunkMgr.NormalizeChunkPosition(
                Chunk.GetChunkPosition(_worldWrapBoundaryStart, _worldWrapBounds.ChunkSize));
            _worldWrapPhase = WorldWrapPhase.WaitForBoundaryChunk;
            Debug.Log($"[GoldenPath][WorldWrap] Preparing right boundary at {_worldWrapBoundaryStart}.");
        }

        internal static bool TickWorldWrapScenario(FlatWorldGoldenPathScenarioContext context)
        {
            _ = context;
            switch (_worldWrapPhase)
            {
                case WorldWrapPhase.WaitForBoundaryChunk:
                    if (!IsWorldWrapChunkReady(_worldWrapExpectedChunk))
                        return false;
                    _worldWrapMover.Speed.BaseValue = context.Configuration.player.wrapMoveSpeed;
                    _worldWrapPhase = WorldWrapPhase.MoveAcrossRightBoundary;
                    Debug.Log($"[GoldenPath][WorldWrap] Right boundary Chunk {_worldWrapExpectedChunk} is ready.");
                    return false;

                case WorldWrapPhase.MoveAcrossRightBoundary:
                    Vector2 target = new Vector2(
                        _worldWrapBounds.MaxExclusive.x + 1.25f,
                        _worldWrapBoundaryStart.y);
                    _worldWrapMover.Move(target, Mathf.Max(Time.deltaTime, 0.02f));
                    if (!_worldWrapObserved)
                        return false;

                    Vector2 current = _worldWrapObservedEvent.CurrentPosition;
                    Vector2 wrapDelta = _worldWrapBounds.ShortestDelta(_worldWrapBoundaryStart, current);
                    if (wrapDelta.x <= 0f || wrapDelta.x > 4f || Mathf.Abs(wrapDelta.y) > 0.5f)
                    {
                        throw new InvalidOperationException(
                            $"Right-edge wrap did not preserve its crossing remainder: delta={wrapDelta}.");
                    }

                    AssertWorldWrapPlayerData(current, 0.75f);
                    _worldWrapExpectedChunk = ChunkMgr.NormalizeChunkPosition(
                        Chunk.GetChunkPosition(current, _worldWrapBounds.ChunkSize));
                    _worldWrapPhase = WorldWrapPhase.WaitForWrappedChunk;
                    Debug.Log(
                        $"[GoldenPath][WorldWrap] Crossed to {current}; " +
                        $"waiting for canonical Chunk {_worldWrapExpectedChunk} and seam mirrors.");
                    return false;

                case WorldWrapPhase.WaitForWrappedChunk:
                    if (!IsWorldWrapChunkReady(_worldWrapExpectedChunk))
                    {
                        LogWorldWrapReadiness("target Chunk is not Ready");
                        return false;
                    }
                    if (!IsWorldWrapVisualSeamReady(_worldWrapExpectedChunk))
                    {
                        LogWorldWrapReadiness("visual/collision seam mirrors are not Ready");
                        return false;
                    }
                    ValidateWorldWrapChunkAndSaveCoordinates();
                    AssertWorldWrapPlayerData(_worldWrapMover.rb.position, 0.75f);
                    _worldWrapPhase = WorldWrapPhase.ReadyForVisualCapture;
                    Debug.Log(
                        "[GoldenPath][WorldWrap] Right-edge wrap passed; " +
                        "camera and boundary collision mirrors are ready for the initial screenshot.");
                    return true;

                case WorldWrapPhase.ReadyForVisualCapture:
                    return true;

                case WorldWrapPhase.WaitForRestoredChunk:
                    if (!IsWorldWrapChunkReady(_worldWrapExpectedChunk))
                        return false;
                    AssertWorldWrapPlayerData(_worldWrapOriginalPosition);
                    _worldWrapScenarioCompleted = true;
                    _worldWrapPhase = WorldWrapPhase.Completed;
                    Debug.Log("[GoldenPath][WorldWrap] Right-edge wrap and restoration passed.");
                    return true;

                case WorldWrapPhase.Completed:
                    return true;
                default:
                    throw new InvalidOperationException("World-wrap scenario was ticked before initialization.");
            }
        }

        private static void HandleGoldenPathPlayerWrapped(WorldWrapEvent wrapEvent)
        {
            _worldWrapObservedEvent = wrapEvent;
            _worldWrapObserved = true;
            if (_worldWrapMover?.rb != null)
                _worldWrapMover.rb.velocity = Vector2.zero;
            WorldTopologyRuntime.LocalPlayerPositionWrapped -= HandleGoldenPathPlayerWrapped;
        }

        internal static void BeginWorldWrapRestoration()
        {
            if (_worldWrapPhase != WorldWrapPhase.ReadyForVisualCapture)
            {
                throw new InvalidOperationException(
                    $"World-wrap restoration started from invalid phase {_worldWrapPhase}.");
            }

            RestoreWorldWrapPlayer();
            _worldWrapExpectedChunk = ChunkMgr.NormalizeChunkPosition(
                Chunk.GetChunkPosition(_worldWrapOriginalPosition, _worldWrapBounds.ChunkSize));
            _worldWrapPhase = WorldWrapPhase.WaitForRestoredChunk;
        }

        internal static bool TickWorldWrapRestoration(FlatWorldGoldenPathScenarioContext context)
        {
            return TickWorldWrapScenario(context);
        }

        private static bool IsWorldWrapChunkReady(Vector2Int chunkPosition)
        {
            ChunkMgr manager = ChunkMgr.Instance;
            return manager != null &&
                   manager.TryGetActiveChunkByPos(chunkPosition, out Chunk chunk) &&
                   chunk != null && chunk.IsReady;
        }

        private static bool IsWorldWrapVisualSeamReady(Vector2Int chunkPosition)
        {
            Camera camera = Camera.main;
            WrappedWorldCameraRenderer renderer =
                camera != null ? camera.GetComponent<WrappedWorldCameraRenderer>() : null;
            if (renderer == null || renderer.ActiveReplicaCount <= 0 ||
                !WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
                return false;

            _ = chunkPosition;
            return IsVisibleWorldWrapWindowReady(camera, bounds);
        }

        private static bool IsVisibleWorldWrapWindowReady(
            Camera camera,
            WorldTopologyBounds bounds)
        {
            float halfHeight = camera.orthographic
                ? camera.orthographicSize
                : Mathf.Max(bounds.Span.x, bounds.Span.y);
            float halfWidth = camera.orthographic
                ? halfHeight * Mathf.Max(0.01f, camera.aspect)
                : halfHeight;
            Vector2 cameraPosition = camera.transform.position;
            Vector2Int minChunk = Chunk.GetChunkPosition(
                cameraPosition - new Vector2(halfWidth, halfHeight),
                bounds.ChunkSize);
            Vector2Int maxChunk = Chunk.GetChunkPosition(
                cameraPosition + new Vector2(halfWidth, halfHeight),
                bounds.ChunkSize);
            var required = new HashSet<Vector2Int>();
            for (int x = minChunk.x; x <= maxChunk.x; x += bounds.ChunkSize.x)
            {
                for (int y = minChunk.y; y <= maxChunk.y; y += bounds.ChunkSize.y)
                    required.Add(bounds.NormalizeChunkOrigin(new Vector2Int(x, y)));
            }

            _worldWrapVisibleRequiredCount = required.Count;
            _worldWrapVisibleReadyCount = 0;
            ChunkMgr manager = ChunkMgr.Instance;
            foreach (Vector2Int requiredChunk in required)
            {
                if (manager == null ||
                    !manager.TryGetActiveChunkByPos(requiredChunk, out Chunk chunk) ||
                    chunk == null || !chunk.IsReady || chunk.Map == null ||
                    !chunk.Map.IsTilemapVisualReady)
                {
                    continue;
                }

                WrappedTilemapCollisionProxy proxy =
                    chunk.Map.GetComponent<WrappedTilemapCollisionProxy>();
                if (proxy == null)
                    continue;
                proxy.RefreshNow();
                if (proxy.EligibleSourceColliderCount > 0 && proxy.ActiveProxyCount == 0)
                    continue;
                _worldWrapVisibleReadyCount++;
            }

            return _worldWrapVisibleReadyCount == _worldWrapVisibleRequiredCount;
        }

        private static void LogWorldWrapReadiness(string reason)
        {
            if (Time.realtimeSinceStartup < _worldWrapNextDiagnosticTime)
                return;
            _worldWrapNextDiagnosticTime = Time.realtimeSinceStartup + 5f;

            ChunkMgr manager = ChunkMgr.Instance;
            Chunk chunk = null;
            bool hasChunk = manager != null &&
                            manager.TryGetActiveChunkByPos(_worldWrapExpectedChunk, out chunk);
            Camera camera = Camera.main;
            WrappedWorldCameraRenderer renderer =
                camera != null ? camera.GetComponent<WrappedWorldCameraRenderer>() : null;
            Map map = hasChunk ? chunk?.Map : null;
            WrappedTilemapCollisionProxy proxy =
                map != null ? map.GetComponent<WrappedTilemapCollisionProxy>() : null;
            Debug.Log(
                $"[GoldenPath][WorldWrap] Waiting: {reason}; expected={_worldWrapExpectedChunk}, " +
                $"pending={manager?.HasPendingChunkLoads}, activeChunk={hasChunk}, " +
                $"state={(hasChunk && chunk != null ? chunk.LifecycleState.ToString() : "missing")}, " +
                $"mapVisual={map?.IsTilemapVisualReady}, camera={camera?.transform.position}, " +
                $"cameraReplicas={renderer?.ActiveReplicaCount}, " +
                $"visibleChunks={_worldWrapVisibleReadyCount}/{_worldWrapVisibleRequiredCount}, " +
                $"collisionSources={proxy?.EligibleSourceColliderCount}, " +
                $"collisionProxies={proxy?.ActiveProxyCount}.");
        }

        private static void ValidateWorldWrapChunkAndSaveCoordinates()
        {
            ChunkMgr manager = ChunkMgr.Instance;
            if (manager == null || manager.Chunk_Dic_ByPos.Count == 0)
                throw new InvalidOperationException("World-wrap target has no registered chunks.");

            foreach (KeyValuePair<Vector2Int, Chunk> entry in manager.Chunk_Dic_ByPos)
            {
                if (!_worldWrapBounds.Contains(entry.Key))
                    throw new InvalidOperationException($"Chunk dictionary has out-of-bounds key {entry.Key}.");
                if (entry.Value == null || entry.Value.MapSave == null ||
                    entry.Value.MapSave.MapPosition != entry.Key ||
                    entry.Value.MapSave.Name != entry.Key.ToString())
                {
                    throw new InvalidOperationException($"Chunk save identity is not canonical at {entry.Key}.");
                }
            }

            PlanetData planet = SaveDataMgr.Instance?.Active_PlanetData;
            if (planet?.MapData_Dict == null)
                throw new InvalidOperationException("Wrapped planet save dictionary is unavailable.");
            foreach (KeyValuePair<string, MapSave> entry in planet.MapData_Dict)
            {
                if (entry.Value == null)
                    continue;
                if (!_worldWrapBounds.Contains(entry.Value.MapPosition) ||
                    entry.Key != entry.Value.MapPosition.ToString())
                {
                    throw new InvalidOperationException($"Planet save has non-canonical Chunk key {entry.Key}.");
                }
            }
        }

        private static void AssertWorldWrapPlayerData(Vector2 expected, float tolerance = 0.01f)
        {
            if (_worldWrapPlayer == null || _worldWrapPlayer.Data?.transform == null)
                throw new InvalidOperationException("Wrapped player persistence data is unavailable.");
            Vector2 stored = _worldWrapPlayer.Data.transform.position;
            if (!_worldWrapBounds.Contains(stored))
                throw new InvalidOperationException($"Wrapped player data position {stored} is outside canonical bounds.");
            if ((stored - expected).sqrMagnitude > tolerance * tolerance)
            {
                throw new InvalidOperationException(
                    $"Wrapped player data position {stored} does not match Rigidbody2D {expected}.");
            }
        }

        private static void RestoreWorldWrapPlayer()
        {
            SetWorldWrapPlayerPosition(_worldWrapOriginalPosition, _worldWrapOriginalVelocity);
            _worldWrapMover.Speed.BaseValue = _worldWrapOriginalSpeed;
            _worldWrapChunkLoader.RefreshChunksAroundPlayer();
            _worldWrapRestored = true;
        }

        private static void SetWorldWrapPlayerPosition(Vector2 position, Vector2 velocity)
        {
            if (_worldWrapPlayer == null || _worldWrapMover == null || _worldWrapMover.rb == null)
                return;
            float z = _worldWrapPlayer.transform.position.z;
            _worldWrapMover.rb.position = position;
            _worldWrapMover.rb.velocity = velocity;
            _worldWrapPlayer.transform.position = new Vector3(position.x, position.y, z);
            if (_worldWrapPlayer.Data?.transform != null)
                _worldWrapPlayer.Data.transform.position = _worldWrapPlayer.transform.position;
        }

        private static void AssertWorldWrapScenarioCompleted()
        {
            if (!_worldWrapScenarioCompleted)
                throw new InvalidOperationException("Full golden path ended before the real right-edge wrap completed.");
        }

        private static void CleanupWorldWrapScenario()
        {
            WorldTopologyRuntime.LocalPlayerPositionWrapped -= HandleGoldenPathPlayerWrapped;
            if (_worldWrapPhase != WorldWrapPhase.None && !_worldWrapRestored)
            {
                SetWorldWrapPlayerPosition(_worldWrapOriginalPosition, _worldWrapOriginalVelocity);
                if (_worldWrapMover != null && _worldWrapMover.Speed != null)
                    _worldWrapMover.Speed.BaseValue = _worldWrapOriginalSpeed;
                if (_worldWrapChunkLoader != null)
                    _worldWrapChunkLoader.RefreshChunksAroundPlayer();
            }

            _worldWrapChunkLoader = null;
            _worldWrapPlayer = null;
            _worldWrapMover = null;
        }
    }
}
