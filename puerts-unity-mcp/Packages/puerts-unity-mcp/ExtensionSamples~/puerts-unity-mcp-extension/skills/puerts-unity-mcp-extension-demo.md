---
name: puerts-unity-mcp-extension-demo
description: Demo rules for project JavaScript and C# MCP extensions.
---

This extension folder contains demo tools for PuerTS Unity MCP.

Use `editor.scriptTools.list` or `agent.extension.scriptTools.list` to discover Editor JavaScript tools under `js/editor`.

Use `runtime.scriptTools.list`, `runtime.tool.call`, or `agent.extension.scriptTools.list` to discover Runtime JavaScript tools under `js/runtime`.

Project C# tools should live in a local package under `Packages/<project-extension-package>`, implement `IUnityMcpToolProvider`, and register unique tool names such as `game.*` or `demo.*`.

JavaScript tools export `execute(argsJson, contextJson)`, parse `argsJson`, and return a JSON string. Prefer `CS.UnityEngine` or `CS.UnityEditor`; use `__unity_mcp.invokeStatic`, `getStatic`, `getStaticPath`, `setStatic`, and `typeExists` when reflection is safer or generated wraps are missing.
