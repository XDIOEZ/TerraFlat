using System;
using System.Collections.Generic;

namespace FlatWorld.Networking.Gameplay
{
    #region 建造请求回执账本

    internal enum NetworkBuildingRequestKind { Place, Dismantle }

    // 两类事务共用令牌和到期队列，回调前先移除记录以允许回执触发新的请求。
    internal sealed class NetworkBuildingRequestLedger
    {
        internal readonly struct Request
        {
            internal Request(NetworkBuildingRequestKind kind, Mod_Building building, int guid, float expiresAt)
            { Kind = kind; Building = building; ItemGuid = guid; ExpiresAt = expiresAt; }

            internal NetworkBuildingRequestKind Kind { get; }
            internal Mod_Building Building { get; }
            internal int ItemGuid { get; }
            internal float ExpiresAt { get; }
        }

        private const float TimeoutSeconds = 8f;
        private readonly Dictionary<uint, Request> requests = new();
        private readonly List<uint> expiredTokens = new();
        private readonly Queue<(Request Request, string Reason)> rejections = new();
        private readonly Action<Request, string> reject;
        private uint nextToken;
        private int placementCount;
        private float nextExpiry = float.PositiveInfinity;

        internal NetworkBuildingRequestLedger(Action<Request, string> reject) => this.reject = reject;
        internal int Count => requests.Count;

        internal bool HasPending(NetworkBuildingRequestKind kind) =>
            kind == NetworkBuildingRequestKind.Place ? placementCount > 0 : requests.Count > placementCount;

        internal uint Add(NetworkBuildingRequestKind kind, Mod_Building building, int guid, float now)
        {
            if (++nextToken == 0) nextToken++;
            var request = new Request(kind, building, guid, now + TimeoutSeconds);
            requests.Add(nextToken, request);
            if (kind == NetworkBuildingRequestKind.Place) placementCount++;
            nextExpiry = Math.Min(nextExpiry, request.ExpiresAt);
            return nextToken;
        }

        internal bool Contains(Mod_Building building, NetworkBuildingRequestKind kind)
        {
            foreach (Request request in requests.Values)
                if (request.Kind == kind && request.Building == building) return true;
            return false;
        }

        internal bool TryTake(uint token, NetworkBuildingRequestKind kind, out Request request)
        {
            if (!requests.TryGetValue(token, out request) || request.Kind != kind) return false;
            requests.Remove(token);
            if (kind == NetworkBuildingRequestKind.Place) placementCount--;
            return true;
        }

        internal void Expire(float now)
        {
            if (now < nextExpiry) return;
            nextExpiry = float.PositiveInfinity;
            foreach (var pair in requests)
            {
                if (pair.Value.ExpiresAt <= now) expiredTokens.Add(pair.Key);
                else nextExpiry = Math.Min(nextExpiry, pair.Value.ExpiresAt);
            }
            foreach (uint token in expiredTokens)
            {
                Request request = requests[token];
                requests.Remove(token);
                if (request.Kind == NetworkBuildingRequestKind.Place) placementCount--;
                rejections.Enqueue((request, request.Kind == NetworkBuildingRequestKind.Place
                    ? "服务器响应超时" : "服务端响应超时"));
            }
            expiredTokens.Clear();
            DispatchRejections();
        }

        internal void RejectAll(string reason)
        {
            foreach (Request request in requests.Values) rejections.Enqueue((request, reason));
            requests.Clear();
            placementCount = 0;
            nextExpiry = float.PositiveInfinity;
            DispatchRejections();
        }

        private void DispatchRejections()
        {
            while (rejections.Count > 0)
            {
                var notification = rejections.Dequeue();
                reject(notification.Request, notification.Reason);
            }
        }
    }

    #endregion
}
