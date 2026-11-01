---
name: flatworld-unity-startup
description: "Unity MCP Startup：FlatWorld 调用 Unity MCP / PuerTS MCP 前的双服务检查与启动。"
---

# Unity MCP Startup

## 触发

需要通过 MCP 操作 Unity 时，**任务开始、首次调用前检查一次**；失联再检查。纯静态修改不启动 Unity。命令通过 HomePC2 在项目目录的 CMD 执行。

## 1. 先只读检查

```bat
cd /d D:\_Unity\_UnityProject\FlatWorld
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\PC\.codex\skills\flatworld-unity-startup\scripts\Start-FlatWorld.ps1" -StatusOnly
python Tools\UnityMcpFast.py health
python Tools\UnityMcpFast.py call mcp.info --args "{}"
python Tools\UnityMcpFast.py call editor.playmode.state --args "{}"
```

- **Unity MCP**：Codex `unityMCP` 配置为 `uvx --from mcpforunityserver==10.2.0 mcp-for-unity --transport stdio`，由**客户端**启动，勿另开重复服务。桥通常在 6400～6415；核对 `%USERPROFILE%\.unity-mcp\unity-mcp-status-*.json` 中 FlatWorld 路径、心跳及端口。必须通过 `mcpforunity://instances` 选择 FlatWorld 并读取 `mcpforunity://editor/state`，不能只看端口。
- **PuerTS MCP**：快通道自动读取 `puerts-unity-mcp-extension/editor-mcp-config.json` 的 `editorPort`（当前 **19004**）；`health` 后再用 `editor.playmode.state` 验证。不混用 Player 的 `runtimePort`。

## 2. 未启动时恢复

仅当 **Unity Editor 未运行**且任务需要启动时：

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\PC\.codex\skills\flatworld-unity-startup\scripts\Start-FlatWorld.ps1" -NoWait
```

脚本按 `ProjectVersion.txt` 启动 Editor。PuerTS 的 `editorAutoStart=true` 和 Unity MCP 的 stdio 桥自动启动；之后重新检查。

- **Unity MCP 断开、PuerTS 可用**：排除编译/域重载后，用 PuerTS `editor.js.eval` 执行 `__unity_mcp.invokeStatic('MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost','Start')`，重连 Unity MCP 并确认状态。
- **PuerTS 断开、Unity MCP 可用**：用 `execute_menu_item` 执行 `PuerTS Unity MCP/Start Editor MCP`，重做 `health`。
- **两者都断开**：检查是否编译、项目是否正确；不强杀 Editor，也不盲目重试写操作。

## 3. 使用规则

用户在 PlayMode 时**不暂停、退出、切场景或强制编译**。两套 MCP 必须锁定本项目 Editor。编辑器操作优先 Unity MCP；高频查询/Profiler 用 PuerTS。玩法另读 `D:\_Unity\_UnityProject\FlatWorld\.agents\skills\flatworld-gameplay-mcp\SKILL.md`。写操作超时不可自动重放。
