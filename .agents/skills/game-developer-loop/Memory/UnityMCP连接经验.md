# Unity MCP 连接经验

核实日期：2026-10-06。

## 当前 TerraFlat 实际入口

- 本机 Codex 配置 `C:/Users/CatStudio/.codex/config.toml` 的 `[mcp_servers.unityMCP]` 当前指向 `http://127.0.0.1:8082/mcp`。
- 不要把“某个端口正在监听”直接当成 Unity MCP。此次 `127.0.0.1:8080` 实际由 `steamwebhelper.exe` 占用，访问 `/mcp` 返回 404；真实 Unity MCP 在 8082。
- 官方 MCP for Unity 的 HTTP 默认端口通常是 8080，但本机项目可覆盖端口；开发循环应优先读取当前客户端配置，再核对监听进程和实际 MCP 握手结果。

## 最短可靠检查顺序

1. 用 PCC 进入 `C:/Users/CatStudio/Desktop/TerraFlat-master`，先确认当前 Shell。此次 PCC 默认是 `cmd.exe`；需要 PowerShell 语法时显式调用 `powershell -NoProfile -Command ...`。
2. 查看 `C:/Users/CatStudio/.codex/config.toml` 的 `mcp_servers.unityMCP.url`，不要猜端口。
3. 用 `netstat -ano` / 进程信息确认该端口确实在监听；如果端口被别的进程占用，不要继续把它当 Unity MCP。
4. 直接对 `<url>` 做 MCP `initialize`。本次 `http://127.0.0.1:8082/mcp` 返回 HTTP 200、`text/event-stream`，服务端为 `mcp-for-unity-server 3.4.8`，协议版本为 `2025-06-18`。
5. 从响应头保存 `mcp-session-id`，随后发送 `notifications/initialized`；后续请求都带同一个 `Mcp-Session-Id`。
6. 读取 `mcpforunity://instances`。有多个 Unity 时必须调用 `set_active_instance` 锁定目标；此次同时发现 `TerraFlat-master` 与 `Project3`，实际锁定 `TerraFlat-master@6b218ed8c11e4831`。
7. 读取 `mcpforunity://editor/state` 和 `mcpforunity://custom-tools` 后再开始开发循环。`editor/state` 若只报 `stale_status`，按 `recommended_retry_after_ms` 重试，不要误判为服务器掉线。
8. `tools/list` 已直接暴露 `gameplay_session`、`gameplay_control`、`gameplay_observe`、`gameplay_act`、`gameplay_gm`、`gameplay_spawner_debug` 等 FlatWorld 工具时，直接使用这些工具；只有当前版本未直出某项目工具时才回退 `execute_custom_tool`。
9. `manage_editor(action=play)` 返回成功后仍可能处于 Play/Domain 切换窗口。本轮紧接着读取 `editor/state` 一度返回 `no_unity_session`，但 `gameplay_session(status)` 已报告 `playing=true`、协议 `0.10.4`、`world_not_ready`；此时应继续轮询会话/Editor 状态，不要把短暂重载当作连接永久失败。

## 本轮踩坑

- `127.0.0.1:8080` 是 Steam CEF 调试端口，不是 Unity MCP；仅凭端口存在会产生假阳性。
- 通过本机 `codex exec` 再间接调用 Unity MCP，本轮长时间没有返回有效 Unity 状态；直接按 MCP HTTP 协议访问 8082 更短、更稳定，也更容易诊断是哪一层失败。
- 多 Unity 实例共享一个 HTTP MCP 服务时，未先 `set_active_instance` 容易把操作路由到错误项目；开发循环启动阶段必须显式锁定 TerraFlat 实例。
- `custom-tools` 是项目能力清单，不代表所有项目工具都只能经 `execute_custom_tool`；应先看 `tools/list` 是否已有直连工具。
- 进入 Play Mode 的瞬间，Unity bridge 可能短暂不可用；只要 MCP Server 本身仍在且 Gameplay/Editor 状态随后恢复，就属于重载窗口而不是必须重启服务器的故障。

## HTTP 握手要点

初始化请求至少带：

```text
Content-Type: application/json
Accept: application/json, text/event-stream
```

初始化成功后，从响应头取得：

```text
mcp-session-id: <session-id>
```

之后发送 `notifications/initialized`，并在后续 `resources/read`、`tools/list`、`tools/call` 请求中带：

```text
Mcp-Session-Id: <session-id>
```

## 外部参考结论

- MCP for Unity 官方仓库说明 HTTP transport 支持多个 MCP 客户端共享 Unity，并通过 session/client 维持实例路由状态。
- 官方默认 HTTP 地址常见为 `http://localhost:8080/mcp`，但服务端支持通过 URL、参数或环境变量覆盖端口。因此 TerraFlat 的实际连接地址必须以本机当前配置和真实握手为准，而不是照搬默认值。
