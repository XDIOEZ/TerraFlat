using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkSpaceStateCoordinator
    {
        #region 授权地表选址预览

        private sealed class LandingPreviewPending
        {
            public NetworkLandingPreviewRequest Request;
            public Action<byte[], bool> Completed;
            public SpaceSession Session;
            public GameSaveData Save;
            public float RetryAt;
            public uint Transfer, Hash;
            public int Count, PartBytes, TotalBytes, Received;
            public byte[][] Parts;
        }

        private readonly Dictionary<NetworkConnectionToClient, NetworkLandingPreviewRequest> landingPreviewServerRequests = new();
        private LandingPreviewPending landingPreviewPending;
        private static long nextLandingPreviewEpoch;
        private long landingPreviewEpoch;
        private uint landingPreviewToken, landingPreviewTransfer;

        private void StartLandingPreviewClient()
        {
            landingPreviewEpoch = ++nextLandingPreviewEpoch;
            SpaceNetworkBridge.LandingPreviewRequester = RequestLandingPreview;
            SpaceNetworkBridge.LandingPreviewCancel = CancelLandingPreview;
        }

        private void StopLandingPreviewClient()
        {
            CancelLandingPreview();
            if (SpaceNetworkBridge.LandingPreviewRequester == RequestLandingPreview) SpaceNetworkBridge.LandingPreviewRequester = null;
            if (SpaceNetworkBridge.LandingPreviewCancel == CancelLandingPreview) SpaceNetworkBridge.LandingPreviewCancel = null;
            landingPreviewEpoch = ++nextLandingPreviewEpoch;
        }

        private bool RequestLandingPreview(string body, Vector2 origin, Vector2 selected, Action<byte[], bool> completed)
        {
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (!clientStarted || !NetworkClient.ready || player == null || player.IsWorldTransferPending ||
                SpaceSession.Current == null || SaveDataMgr.Instance?.SaveData == null) return false;
            CancelLandingPreview();
            landingPreviewPending = new LandingPreviewPending
            {
                Request = new NetworkLandingPreviewRequest
                {
                    BodyId = body, Origin = origin, Selected = selected, Epoch = landingPreviewEpoch,
                    RealmRevision = player.RealmRevision, RequestToken = NextToken(ref landingPreviewToken)
                },
                Completed = completed, Session = SpaceSession.Current, Save = SaveDataMgr.Instance.SaveData
            };
            TickLandingPreviewClient();
            return true;
        }

        private void CancelLandingPreview()
        {
            LandingPreviewPending pending = landingPreviewPending;
            landingPreviewPending = null;
            if (pending == null) return;
            if (NetworkClient.ready)
            {
                NetworkLandingPreviewRequest cancel = pending.Request;
                cancel.Cancel = true;
                NetworkClient.Send(cancel, Channels.Reliable);
            }
            pending.Completed?.Invoke(null, false);
        }

        private void TickLandingPreviewClient()
        {
            LandingPreviewPending pending = landingPreviewPending;
            if (pending == null) return;
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (!clientStarted || !NetworkClient.ready || player == null || player.IsWorldTransferPending ||
                player.RealmRevision != pending.Request.RealmRevision || !ReferenceEquals(SpaceSession.Current, pending.Session) ||
                !ReferenceEquals(SaveDataMgr.Instance?.SaveData, pending.Save))
            { CancelLandingPreview(); return; }
            if (Time.unscaledTime < pending.RetryAt) return;
            pending.RetryAt = Time.unscaledTime + 2f;
            NetworkClient.Send(pending.Request, Channels.Reliable);
        }

        // 目标星体和地图范围只取连接绑定乘员的正式下落状态。
        private bool TryAuthorizeLandingPreview(NetworkConnectionToClient connection, NetworkLandingPreviewRequest request,
            out SpaceSession session, out BodyState body)
        {
            session = SpaceSession.Current; body = null;
            if (!ResolveConnection(connection, out ConnectionState state, out NetworkWorldPlayer player) ||
                player.IsWorldTransferPending || player.RealmRevision != request.RealmRevision || request.Epoch <= 0 ||
                request.RequestToken == 0 || session?.State?.Passengers == null ||
                !float.IsFinite(request.Origin.x) || !float.IsFinite(request.Origin.y) ||
                !float.IsFinite(request.Selected.x) || !float.IsFinite(request.Selected.y)) return false;
            SpacePassengerState passenger = session.State.Passengers.Find(value => value != null && value.ProfileId == state.Profile);
            if (passenger == null || !passenger.SurfaceDescending || session.GetShip(passenger.ShipId) != null ||
                passenger.LandingBodyId != request.BodyId ||
                (new Vector2((float)passenger.LandingOriginX, (float)passenger.LandingOriginY) - request.Origin).sqrMagnitude > .0001f ||
                (request.Selected - request.Origin).sqrMagnitude >
                    (float)(SpaceGameplaySettings.Current.LandingChoiceRadiusMeters * SpaceGameplaySettings.Current.LandingChoiceRadiusMeters) + .001f)
                return false;
            body = session.Universe.GetBody(request.BodyId);
            return body != null && !string.IsNullOrWhiteSpace(body.PlanetId);
        }

        private void OnLandingPreviewRequest(NetworkConnectionToClient connection, NetworkLandingPreviewRequest request)
        {
            if (connection == null || !connection.isAuthenticated) return;
            if (request.Cancel)
            {
                if (landingPreviewServerRequests.TryGetValue(connection, out var current) &&
                    current.Epoch == request.Epoch && current.RequestToken == request.RequestToken)
                    landingPreviewServerRequests.Remove(connection);
                return;
            }
            if (!TryAuthorizeLandingPreview(connection, request, out _, out _)) return;
            if (landingPreviewServerRequests.TryGetValue(connection, out var old))
            {
                if (old.Epoch > request.Epoch || old.Epoch == request.Epoch &&
                    (old.RequestToken == request.RequestToken || !IsNewer(request.RequestToken, old.RequestToken))) return;
            }
            landingPreviewServerRequests[connection] = request;
            StartCoroutine(CaptureLandingPreview(connection, request));
        }

        private bool IsCurrentLandingPreview(NetworkConnectionToClient connection, NetworkLandingPreviewRequest request,
            SpaceSession expectedSession, GameSaveData expectedSave, out BodyState body)
        {
            body = null;
            return serverStarted && NetworkServer.active && landingPreviewServerRequests.TryGetValue(connection, out var active) &&
                active.Epoch == request.Epoch && active.RequestToken == request.RequestToken &&
                ReferenceEquals(SpaceSession.Current, expectedSession) && ReferenceEquals(SaveDataMgr.Instance?.SaveData, expectedSave) &&
                TryAuthorizeLandingPreview(connection, request, out _, out body);
        }

        private IEnumerator CaptureLandingPreview(NetworkConnectionToClient connection, NetworkLandingPreviewRequest request)
        {
            SpaceSession session = SpaceSession.Current;
            GameSaveData save = SaveDataMgr.Instance?.SaveData;
            try
            {
                if (!IsCurrentLandingPreview(connection, request, session, save, out BodyState body)) yield break;
                float radius = (float)SpaceGameplaySettings.Current.LandingChoiceRadiusMeters;
                SurfaceQueryStatus areaStatus;
                while ((areaStatus = SpaceSurfaceQuery.EnsureSurfaceAreaReady(body.PlanetId, request.Origin, radius)) == SurfaceQueryStatus.Pending)
                {
                    if (!IsCurrentLandingPreview(connection, request, session, save, out body)) yield break;
                    yield return null;
                }
                if (areaStatus != SurfaceQueryStatus.Ready) yield break;
                const int size = SpaceNetworkBridge.LandingPreviewGridSize;
                var bytes = new byte[size * size + 1];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        Vector2 point = request.Origin + new Vector2((float)x / (size - 1) - .5f,
                            (float)y / (size - 1) - .5f) * (radius * 2f);
                        SurfaceQueryStatus status;
                        LandingSurfaceSample sample;
                        while ((status = SpaceSurfaceQuery.QueryLandingSurface(body.PlanetId, point, out sample)) != SurfaceQueryStatus.Ready)
                        {
                            if (status == SurfaceQueryStatus.Invalid) yield break;
                            if (!IsCurrentLandingPreview(connection, request, session, save, out body)) yield break;
                            yield return null;
                        }
                        bytes[y * size + x] = sample.IsWater ? (byte)1 : sample.BlockingTileId >= 0 ? (byte)2 : (byte)3;
                    }
                    if ((y & 3) == 3)
                    {
                        if (!IsCurrentLandingPreview(connection, request, session, save, out body)) yield break;
                        yield return null;
                    }
                }
                LandingSurfaceSample selected;
                SurfaceQueryStatus selectedStatus;
                while ((selectedStatus = SpaceSurfaceQuery.QueryLandingSurface(body.PlanetId, request.Selected, out selected)) != SurfaceQueryStatus.Ready)
                {
                    if (selectedStatus == SurfaceQueryStatus.Invalid) yield break;
                    if (!IsCurrentLandingPreview(connection, request, session, save, out body)) yield break;
                    yield return null;
                }
                if (!IsCurrentLandingPreview(connection, request, session, save, out body)) yield break;
                bytes[bytes.Length - 1] = selected.IsWater ? (byte)1 : (byte)0;
                int transportLimit = Transport.active?.GetMaxPacketSize(Channels.Reliable) ?? 32768;
                int partBytes = Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes, Math.Max(512, transportLimit - 1024));
                int count = (bytes.Length + partBytes - 1) / partBytes;
                uint transfer = NextToken(ref landingPreviewTransfer), hash = NetworkGameplayProtocol.CalculatePayloadHash(bytes);
                for (int i = 0; i < count; i++)
                {
                    int offset = i * partBytes;
                    var part = new byte[Math.Min(partBytes, bytes.Length - offset)];
                    Buffer.BlockCopy(bytes, offset, part, 0, part.Length);
                    connection.Send(new NetworkLandingPreviewPart
                    {
                        Epoch = request.Epoch, RealmRevision = request.RealmRevision, RequestToken = request.RequestToken,
                        TransferId = transfer, Hash = hash, Index = i, Count = count, PartBytes = partBytes,
                        TotalBytes = bytes.Length, Data = part
                    }, Channels.Reliable);
                }
            }
            finally
            {
                if (landingPreviewServerRequests.TryGetValue(connection, out var active) &&
                    active.Epoch == request.Epoch && active.RequestToken == request.RequestToken)
                    landingPreviewServerRequests.Remove(connection);
            }
        }

        private void OnLandingPreviewPart(NetworkLandingPreviewPart part)
        {
            LandingPreviewPending pending = landingPreviewPending;
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (pending == null || player == null || player.IsWorldTransferPending ||
                player.RealmRevision != part.RealmRevision || part.RealmRevision != pending.Request.RealmRevision ||
                part.Epoch != pending.Request.Epoch || part.RequestToken != pending.Request.RequestToken ||
                !ReferenceEquals(SpaceSession.Current, pending.Session) || !ReferenceEquals(SaveDataMgr.Instance?.SaveData, pending.Save)) return;
            int length = SpaceNetworkBridge.LandingPreviewGridSize * SpaceNetworkBridge.LandingPreviewGridSize + 1;
            if (part.TransferId == 0 || part.TotalBytes != length || part.PartBytes < 1 ||
                part.PartBytes > NetworkGameplayProtocol.SnapshotChunkBytes ||
                part.Count != (length + part.PartBytes - 1) / part.PartBytes || (uint)part.Index >= (uint)part.Count ||
                part.Data == null || part.Data.Length != Math.Min(part.PartBytes, length - part.Index * part.PartBytes)) return;
            if (pending.Transfer != part.TransferId)
            {
                if (pending.Transfer != 0 && !IsNewer(part.TransferId, pending.Transfer)) return;
                pending.Transfer = part.TransferId; pending.Hash = part.Hash; pending.Count = part.Count;
                pending.PartBytes = part.PartBytes; pending.TotalBytes = part.TotalBytes;
                pending.Parts = new byte[part.Count][]; pending.Received = 0;
            }
            if (pending.Hash != part.Hash || pending.Count != part.Count || pending.PartBytes != part.PartBytes ||
                pending.TotalBytes != part.TotalBytes || pending.Parts[part.Index] != null) return;
            pending.Parts[part.Index] = part.Data;
            if (++pending.Received != pending.Count) return;
            var payload = new byte[length];
            for (int i = 0; i < pending.Count; i++) Buffer.BlockCopy(pending.Parts[i], 0, payload, i * pending.PartBytes, pending.Parts[i].Length);
            if (NetworkGameplayProtocol.CalculatePayloadHash(payload) != pending.Hash)
            { pending.Parts = null; pending.Transfer = 0; pending.RetryAt = 0f; return; }
            var grid = new byte[length - 1];
            Buffer.BlockCopy(payload, 0, grid, 0, grid.Length);
            landingPreviewPending = null;
            pending.Completed?.Invoke(grid, payload[length - 1] != 0);
        }

        #endregion
    }
}
