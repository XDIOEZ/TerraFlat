#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PuertsUnityMcp
{
    public sealed class UnityMcpHttpServer : IDisposable
    {
        private readonly IUnityMcpEndpoint endpoint;
        private readonly UnityMcpJsonRpc jsonRpc;
        private HttpListener listener;
        private Thread listenerThread;
        private volatile bool running;

        public UnityMcpHttpServer(IUnityMcpEndpoint endpoint, string bindAddress, int port)
        {
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            jsonRpc = new UnityMcpJsonRpc(endpoint);
            BindAddress = string.IsNullOrEmpty(bindAddress) ? "127.0.0.1" : bindAddress;
            Port = port;
        }

        public string BindAddress { get; }
        public int Port { get; }
        public bool IsRunning => running;
        public string Url => "http://" + DisplayHost + ":" + Port;

        private string PrefixHost
        {
            get
            {
                if (BindAddress == "0.0.0.0" || BindAddress == "*" || BindAddress == "+")
                {
                    return "*";
                }

                return BindAddress;
            }
        }

        private string DisplayHost => PrefixHost == "*" ? "0.0.0.0" : PrefixHost;

        #region HTTP 监听生命周期

        public void Start()
        {
            if (running)
            {
                return;
            }

            listener = new HttpListener();
            listener.Prefixes.Add("http://" + PrefixHost + ":" + Port + "/");
            listener.Start();
            running = true;
            HttpListener activeListener = listener;
            listenerThread = new Thread(() => ListenLoop(activeListener))
            {
                IsBackground = true,
                Name = "PuertsUnityMcpHttpServer-" + endpoint.EndpointKind
            };
            listenerThread.Start();
            Debug.Log("[UnityMCP] " + endpoint.EndpointKind + " listening on " + Url
                + " prefix=http://" + PrefixHost + ":" + Port + "/"
                + " bindAddress=" + BindAddress);
        }

        public void Stop()
        {
            running = false;
            if (listener != null)
            {
                try { listener.Stop(); } catch { }
                try { listener.Close(); } catch { }
                listener = null;
            }
            // 关闭监听解除 GetContext 阻塞后，有限等待线程退出，避免重载时才被强制中止。
            Thread stoppingThread = listenerThread;
            listenerThread = null;
            if (stoppingThread != null && stoppingThread != Thread.CurrentThread && stoppingThread.IsAlive)
                stoppingThread.Join(1000);
        }

        public void Dispose()
        {
            Stop();
        }

        private void ListenLoop(HttpListener activeListener)
        {
            while (running && ReferenceEquals(listener, activeListener))
            {
                try
                {
                    var context = activeListener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleContext(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (ThreadAbortException)
                {
                    // 域卸载必须继续中止线程，不把预期退出当作 HTTP 故障，也不调用 ResetAbort。
                    throw;
                }
                catch (Exception ex)
                {
                    if (running && ReferenceEquals(listener, activeListener))
                    {
                        Debug.LogWarning("[UnityMCP] HTTP listener error: " + ex.Message);
                    }
                }
            }
        }

        #endregion

        private void HandleContext(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                AddCorsHeaders(response);
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }

                var path = request.Url.AbsolutePath;
                if ((path == "/health" || path == "/api/ping") && request.HttpMethod == "GET")
                {
                    var health = UnityMcpMainThread.InvokeAsync(() => endpoint.BuildHealthJson()).GetAwaiter().GetResult();
                    WriteText(response, 200, "application/json", health);
                    return;
                }

                if (path == "/mcp" && request.HttpMethod == "POST")
                {
                    HandleMcpPost(request, response);
                    return;
                }

                if (path == "/mcp" && request.HttpMethod == "GET")
                {
                    WriteJson(response, 200, new InfoBody
                    {
                        status = "ok",
                        transport = "sync-json-rpc",
                        message = "Use POST /mcp for synchronous JSON-RPC calls."
                    });
                    return;
                }

                WriteJson(response, 404, new ErrorBody { error = "not_found" });
            }
            catch (Exception ex)
            {
                try
                {
                    WriteJson(response, 500, new ErrorBody { error = "internal_error", message = ex.Message });
                }
                catch
                {
                    try { response.Close(); } catch { }
                }
            }
        }

        private void HandleMcpPost(HttpListenerRequest request, HttpListenerResponse response)
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            var task = jsonRpc.HandleAsync(body);
            var result = task.GetAwaiter().GetResult();
            if (string.IsNullOrEmpty(result))
            {
                response.StatusCode = 202;
                response.Close();
                return;
            }

            WriteText(response, 200, "application/json", result);
        }

        private static void AddCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Set("Access-Control-Allow-Origin", "*");
            response.Headers.Set("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Set("Access-Control-Allow-Headers", "Content-Type, mcp-session-id");
            response.Headers.Set("Access-Control-Expose-Headers", "mcp-session-id");
        }

        private static void WriteJson<T>(HttpListenerResponse response, int statusCode, T value)
        {
            WriteText(response, statusCode, "application/json", UnityJson.ToJson(value));
        }

        private static void WriteText(HttpListenerResponse response, int statusCode, string contentType, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        [Serializable]
        private sealed class InfoBody
        {
            public string status;
            public string transport;
            public string message;
        }

        [Serializable]
        private sealed class ErrorBody
        {
            public string error;
            public string message;
        }
    }
}
#else
using System;

namespace PuertsUnityMcp
{
    public sealed class UnityMcpHttpServer : IDisposable
    {
        public UnityMcpHttpServer(IUnityMcpEndpoint endpoint, string bindAddress, int port)
        {
            BindAddress = bindAddress;
            Port = port;
        }

        public string BindAddress { get; }
        public int Port { get; }
        public bool IsRunning => false;
        public string Url => null;

        public void Start()
        {
            throw new NotSupportedException("Unity MCP HTTP server is not supported on WebGL.");
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
#endif
