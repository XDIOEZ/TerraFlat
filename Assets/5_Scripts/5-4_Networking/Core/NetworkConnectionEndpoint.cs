using System;

namespace FlatWorld.Networking
{
    /// <summary>
    /// 联机连接端点：支持域名、IPv4、IPv6，以及带端口的 KCP/UDP 穿透地址。
    /// </summary>
    public readonly struct NetworkConnectionEndpoint
    {
        public string Host { get; }
        public ushort Port { get; }

        private NetworkConnectionEndpoint(string host, ushort port)
        {
            Host = host;
            Port = port;
        }

        #region 地址解析

        public static bool TryParse(
            string value,
            ushort fallbackPort,
            out NetworkConnectionEndpoint endpoint,
            out string error)
        {
            endpoint = default;
            string input = value?.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "请输入主机地址或内网穿透地址。";
                return false;
            }

            if (fallbackPort == 0)
                fallbackPort = 7777;

            string uriValue = input.Contains("://", StringComparison.Ordinal)
                ? input
                : $"kcp://{input}";
            if (!Uri.TryCreate(uriValue, UriKind.Absolute, out Uri uri))
            {
                error = "连接地址格式无效，请输入 域名、IP、域名:端口 或 kcp://域名:端口。";
                return false;
            }

            if (!string.Equals(uri.Scheme, "kcp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "udp", StringComparison.OrdinalIgnoreCase))
            {
                error = "当前联机使用 KCP/UDP，请使用 UDP 穿透地址，不能使用 TCP/HTTP 地址。";
                return false;
            }

            if (!string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.AbsolutePath.Trim('/')) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                error = "连接地址只能包含主机和端口，不能包含账号、路径、参数或片段。";
                return false;
            }

            string host = uri.DnsSafeHost?.Trim();
            if (string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                error = "主机地址无效，请检查穿透服务提供的域名或 IP。";
                return false;
            }

            int resolvedPort = uri.IsDefaultPort ? fallbackPort : uri.Port;
            if (resolvedPort <= 0 || resolvedPort > ushort.MaxValue)
            {
                error = "端口必须在 1 到 65535 之间。";
                return false;
            }

            endpoint = new NetworkConnectionEndpoint(host, (ushort)resolvedPort);
            error = string.Empty;
            return true;
        }

        public override string ToString()
        {
            string displayHost = Host != null && Host.Contains(":", StringComparison.Ordinal)
                ? $"[{Host}]"
                : Host;
            return $"{displayHost}:{Port}";
        }

        #endregion
    }
}