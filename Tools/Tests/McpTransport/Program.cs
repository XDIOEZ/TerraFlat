using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Services.Transport.Transports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

internal static class Program
{
    #region 生产传输回归

    private static async Task<int> Main()
    {
        (string Name, Func<Task> Run)[] cases =
        {
            ("长命令未完成时仍回复心跳", VerifyHeartbeatContinuesDuringPendingCommand),
            ("工具注册等待主线程时仍回复心跳", VerifyHeartbeatContinuesDuringToolRegistration),
            ("命令保持顺序且结果不会串号", VerifyQueuedCommandsRemainSequential),
            ("断连取消后不执行排队命令", VerifyDisconnectedConnectionCancelsQueuedCommands),
            ("单个命令超时后连接与后续命令可用", VerifyCommandTimeoutDoesNotPoisonConnection)
        };
        foreach (var test in cases)
        {
            try
            {
                await test.Run();
                Console.WriteLine("[PASS] " + test.Name);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("[FAIL] " + test.Name + ": " + error);
                return 1;
            }
        }
        Console.WriteLine("5 transport regressions passed.");
        return 0;
    }

    private static TaskCompletionSource<T> Completion<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string SuccessfulResult(string label)
        => new JObject { ["status"] = "success", ["result"] = label }.ToString();

    private static async Task VerifyHeartbeatContinuesDuringPendingCommand()
    {
        var entered = Completion<bool>();
        var finish = Completion<string>();
        TransportCommandDispatcher.CommandHandler = (_, _) =>
        {
            entered.TrySetResult(true);
            return finish.Task;
        };
        await using var connection = await LoopbackConnection.CreateAsync();
        await connection.SendExecute("slow");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await connection.SendPing();
        JObject pong = await connection.ReadMessage();
        Check((string)pong["type"] == "pong", "执行命令阻塞了接收循环。");
        Check(!finish.Task.IsCompleted, "心跳必须在长命令结束前返回。");
        finish.SetResult(SuccessfulResult("slow"));
        JObject result = await connection.ReadMessage();
        Check((string)result["id"] == "slow", "命令结果标识错误。");
    }

    private static async Task VerifyQueuedCommandsRemainSequential()
    {
        var entered = Completion<bool>();
        var finish = Completion<string>();
        var executionOrder = new List<string>();
        TransportCommandDispatcher.CommandHandler = (json, _) =>
        {
            string label = (string)JObject.Parse(json)["params"]["label"];
            executionOrder.Add(label);
            if (label == "first")
            {
                entered.TrySetResult(true);
                return finish.Task;
            }
            return Task.FromResult(SuccessfulResult(label));
        };
        await using var connection = await LoopbackConnection.CreateAsync();
        await connection.SendExecute("first");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await connection.SendExecute("second");
        await connection.SendPing();
        Check((string)(await connection.ReadMessage())["type"] == "pong", "排队时心跳被阻塞。");
        Check(executionOrder.SequenceEqual(new[] { "first" }), "两个命令发生了重叠执行。");
        finish.SetResult(SuccessfulResult("first"));
        JObject first = await connection.ReadMessage();
        JObject second = await connection.ReadMessage();
        Check((string)first["id"] == "first" && (string)second["id"] == "second", "结果顺序或请求标识错误。");
        Check(executionOrder.SequenceEqual(new[] { "first", "second" }), "命令执行顺序错误。");
    }

    private static async Task VerifyHeartbeatContinuesDuringToolRegistration()
    {
        var entered = Completion<bool>();
        var tools = Completion<List<ToolMetadata>>();
        TransportCommandDispatcher.PendingToolDiscovery = tools.Task;
        TransportCommandDispatcher.ToolDiscoveryRequested = () => entered.TrySetResult(true);
        try
        {
            await using var connection = await LoopbackConnection.CreateAsync(new EmptyToolDiscovery());
            await connection.SendRegistered();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await connection.SendPing();
            Check((string)(await connection.ReadMessage())["type"] == "pong", "工具注册阻塞了接收循环。");
            tools.SetResult(new List<ToolMetadata>());
            Check((string)(await connection.ReadMessage())["type"] == "register_tools", "注册完成后未发送工具列表。");
        }
        finally
        {
            TransportCommandDispatcher.PendingToolDiscovery = null;
            TransportCommandDispatcher.ToolDiscoveryRequested = null;
        }
    }

    private sealed class EmptyToolDiscovery : IToolDiscoveryService
    {
        public List<ToolMetadata> GetEnabledTools() => new();
    }

    private static async Task VerifyDisconnectedConnectionCancelsQueuedCommands()
    {
        var entered = Completion<bool>();
        var finish = Completion<string>();
        int executions = 0;
        TransportCommandDispatcher.CommandHandler = (_, _) =>
        {
            Interlocked.Increment(ref executions);
            entered.TrySetResult(true);
            return finish.Task;
        };
        await using var connection = await LoopbackConnection.CreateAsync();
        await connection.SendExecute("old-running");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await connection.SendExecute("old-queued");
        await connection.SendPing();
        Check((string)(await connection.ReadMessage())["type"] == "pong", "未读到排队命令后的心跳。");
        await connection.StopClientAndDrainCommands();
        finish.SetResult(SuccessfulResult("late-result"));
        Check(executions == 1, "已断开连接的排队命令仍被执行。");
        Check(!connection.Messages.TryRead(out _), "已取消连接仍发送了结果。");
    }

    private static async Task VerifyCommandTimeoutDoesNotPoisonConnection()
    {
        var finish = Completion<string>();
        TransportCommandDispatcher.CommandHandler = (json, _) =>
        {
            string label = (string)JObject.Parse(json)["params"]["label"];
            return label == "timeout" ? finish.Task : Task.FromResult(SuccessfulResult(label));
        };
        await using var connection = await LoopbackConnection.CreateAsync();
        await connection.SendExecute("timeout", timeout: 1);
        await connection.SendExecute("next");
        JObject timedOut = await connection.ReadMessage();
        JObject next = await connection.ReadMessage();
        Check((string)timedOut["id"] == "timeout" &&
              ((string)timedOut["result"]["error"]).Contains("timed out"), "命令超时没有返回明确错误。");
        Check((string)next["id"] == "next" && (string)next["result"]["status"] == "success", "超时污染了后续命令。");
        await connection.SendPing();
        Check((string)(await connection.ReadMessage())["type"] == "pong", "超时导致连接失效。");
    }

    #endregion

    #region 真实回环 WebSocket 夹具

    // 服务端只提供真实 WebSocket 收发，客户端直接运行项目中的生产传输类。
    private sealed class LoopbackConnection : IAsyncDisposable
    {
        private readonly WebApplication server;
        private readonly WebSocket serverSocket;
        private readonly WebSocketTransportClient client;
        private readonly TaskCompletionSource<bool> serverLifetime;
        private readonly CancellationTokenSource readerCancellation = new();
        private readonly Channel<JObject> messages = Channel.CreateUnbounded<JObject>();
        private readonly Task clientReceiver;
        private readonly Task serverReceiver;
        private bool stopped;
        public ChannelReader<JObject> Messages => messages.Reader;

        private LoopbackConnection(WebApplication server, WebSocket serverSocket,
            ClientWebSocket clientSocket, Uri uri, TaskCompletionSource<bool> serverLifetime,
            IToolDiscoveryService discovery)
        {
            this.server = server;
            this.serverSocket = serverSocket;
            this.serverLifetime = serverLifetime;
            client = new WebSocketTransportClient(discovery);
            var lifecycle = new CancellationTokenSource();
            var connection = CancellationTokenSource.CreateLinkedTokenSource(lifecycle.Token);
            SetField("_socket", clientSocket);
            SetField("_lifecycleCts", lifecycle);
            SetField("_connectionCts", connection);
            SetField("_endpointUri", uri);
            SetField("_sessionId", "loopback");
            clientReceiver = (Task)typeof(WebSocketTransportClient)
                .GetMethod("ReceiveLoopAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(client, new object[] { connection.Token });
            SetField("_receiveTask", clientReceiver);
            serverReceiver = ReadServerMessages();
        }

        public static async Task<LoopbackConnection> CreateAsync(IToolDiscoveryService discovery = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var server = builder.Build();
            var accepted = Completion<WebSocket>();
            var lifetime = Completion<bool>();
            server.UseWebSockets();
            server.Run(async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
                WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
                accepted.TrySetResult(socket);
                await lifetime.Task;
            });
            await server.StartAsync();
            var uri = new Uri(server.Urls.Single().Replace("http://", "ws://") + "/ws");
            var client = new ClientWebSocket();
            await client.ConnectAsync(uri, CancellationToken.None);
            return new LoopbackConnection(server, await accepted.Task, client, uri, lifetime, discovery);
        }

        private void SetField(string name, object value)
            => typeof(WebSocketTransportClient).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(client, value);

        public Task SendExecute(string id, int timeout = 5)
            => Send(new JObject { ["type"] = "execute", ["id"] = id, ["name"] = "test",
                ["timeout"] = timeout, ["params"] = new JObject { ["label"] = id } });

        public Task SendPing() => Send(new JObject { ["type"] = "ping" });
        public Task SendRegistered() => Send(new JObject { ["type"] = "registered", ["session_id"] = "loopback" });

        private Task Send(JObject payload)
            => serverSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload.ToString())),
                WebSocketMessageType.Text, true, CancellationToken.None);

        public async Task<JObject> ReadMessage()
            => await messages.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

        private async Task ReadServerMessages()
        {
            var buffer = new byte[8192];
            try
            {
                while (!readerCancellation.IsCancellationRequested)
                {
                    using var text = new MemoryStream();
                    WebSocketReceiveResult frame;
                    do
                    {
                        frame = await serverSocket.ReceiveAsync(new ArraySegment<byte>(buffer), readerCancellation.Token);
                        if (frame.MessageType == WebSocketMessageType.Close) return;
                        text.Write(buffer, 0, frame.Count);
                    } while (!frame.EndOfMessage);
                    await messages.Writer.WriteAsync(JObject.Parse(Encoding.UTF8.GetString(text.ToArray())));
                }
            }
            catch (Exception error) when (error is OperationCanceledException || error is WebSocketException)
            {
                if (!stopped) messages.Writer.TryComplete(error);
            }
        }

        public async Task StopClientAndDrainCommands()
        {
            if (stopped) return;
            stopped = true;
            var pending = (Task)typeof(WebSocketTransportClient)
                .GetField("_pendingCommandTask", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(client);
            client.ForceStop();
            await clientReceiver.WaitAsync(TimeSpan.FromSeconds(3));
            if (pending != null) await pending.WaitAsync(TimeSpan.FromSeconds(3));
        }

        public async ValueTask DisposeAsync()
        {
            await StopClientAndDrainCommands();
            readerCancellation.Cancel();
            serverSocket.Abort();
            await serverReceiver;
            serverLifetime.TrySetResult(true);
            await server.StopAsync();
            await server.DisposeAsync();
            client.Dispose();
            serverSocket.Dispose();
            readerCancellation.Dispose();
        }
    }

    #endregion
}
