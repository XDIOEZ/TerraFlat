#!/usr/bin/env python3
"""PuerTS Unity MCP 本机 HTTP 快通道，不启动 Node/uvx 代理。"""

import argparse
import http.client
import json
import os
import statistics
import sys
import time
from pathlib import Path
from urllib.parse import urlsplit


class UnityMcp:
    def __init__(self, url: str, timeout: float):
        target = urlsplit(url)
        if target.scheme != "http" or target.hostname not in ("127.0.0.1", "localhost"):
            raise ValueError("快通道只允许连接本机 HTTP Unity MCP")
        self.path = target.path or "/mcp"
        self.host = target.hostname
        self.port = target.port or 18996
        self.timeout = timeout
        self._reconnect()
        self.sequence = 0

    def _reconnect(self):
        self.connection = http.client.HTTPConnection(
            self.host, self.port, timeout=self.timeout
        )

    def _send(self, method, body=None):
        # 仅对尚未建连的拒绝进行重试，不重复执行可能已提交的 Unity 动作。
        for attempt in range(3):
            try:
                headers = {"Content-Type": "application/json; charset=utf-8"} if body else {}
                self.connection.request(method, self.path, body=body, headers=headers)
                response = self.connection.getresponse()
                return response.status, response.read()
            except ConnectionRefusedError:
                self.connection.close()
                self._reconnect()
                if attempt == 2:
                    raise
                time.sleep(0.1 * (attempt + 1))

    def request(self, method: str, params: dict | None = None):
        self.sequence += 1
        payload = json.dumps(
            {"jsonrpc": "2.0", "id": self.sequence, "method": method, "params": params or {}},
            ensure_ascii=False,
        ).encode("utf-8")
        start = time.perf_counter()
        status, body = self._send("POST", payload)
        elapsed_ms = (time.perf_counter() - start) * 1000
        if status != 200:
            raise RuntimeError(f"HTTP {status}: {body[:240]!r}")
        value = json.loads(body)
        if "error" in value:
            raise RuntimeError(json.dumps(value["error"], ensure_ascii=False))
        return value.get("result"), elapsed_ms

    def health(self):
        start = time.perf_counter()
        status, body = self._send("GET")
        result = json.loads(body)
        if status != 200:
            raise RuntimeError(f"HTTP {status}: {result}")
        return result, (time.perf_counter() - start) * 1000

    def close(self):
        self.connection.close()


def print_json(data):
    print(json.dumps(data, ensure_ascii=False, separators=(",", ":")))


def default_url():
    config = Path(__file__).resolve().parent.parent / (
        "puerts-unity-mcp-extension/editor-mcp-config.json"
    )
    port = 18996
    try:
        settings = json.loads(config.read_text(encoding="utf-8"))
        port = int(settings.get("editorPort", port))
    except (OSError, ValueError, TypeError, json.JSONDecodeError):
        pass
    return f"http://127.0.0.1:{port}/mcp"


def main():
    parser = argparse.ArgumentParser(description="直连运行中的 PuerTS Unity MCP")
    parser.add_argument(
        "--url", default=os.getenv("PUERTS_MCP_URL", default_url()),
        help="本机 MCP 地址",
    )
    parser.add_argument("--timeout", type=float, default=8, help="单条请求超时秒数")
    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("health", help="检查本机接口")
    subparsers.add_parser("bench", help="只读延迟测试，每项重复三次")
    names = subparsers.add_parser("list", help="列出工具名")
    names.add_argument("--filter", default="", help="名称关键字")
    call = subparsers.add_parser("call", help="调用一个 MCP 工具")
    call.add_argument("name", help="完整工具名，如 editor.playmode.state")
    call.add_argument("--args", default="{}", help="JSON 参数")
    batch = subparsers.add_parser("batch", help="同一连接串行执行多个工具")
    batch.add_argument("file", help="JSON 文件，数组元素为 {name, arguments}")
    args = parser.parse_args()

    client = UnityMcp(args.url, args.timeout)
    try:
        if args.command == "health":
            result, ms = client.health()
            print_json({"latency_ms": round(ms, 1), "result": result})
        elif args.command == "list":
            result, ms = client.request("tools/list")
            tools = result.get("tools", [])
            print_json({
                "latency_ms": round(ms, 1),
                "count": len(tools),
                "names": [tool["name"] for tool in tools if args.filter in tool["name"]],
            })
        elif args.command == "call":
            result, ms = client.request(
                "tools/call", {"name": args.name, "arguments": json.loads(args.args)}
            )
            print_json({"latency_ms": round(ms, 1), "result": result})
        elif args.command == "batch":
            with open(args.file, "r", encoding="utf-8") as f:
                calls = json.load(f)
            if not isinstance(calls, list):
                raise ValueError("batch 文件必须是工具调用数组")
            output = []
            for index, entry in enumerate(calls):
                result, ms = client.request("tools/call", {
                    "name": entry["name"], "arguments": entry.get("arguments", {})
                })
                output.append({
                    "index": index, "name": entry["name"],
                    "latency_ms": round(ms, 1), "result": result,
                })
                if isinstance(result, dict) and result.get("isError"):
                    break
            print_json(output)
        elif args.command == "bench":
            metrics = {}
            for label in ("http_health", "tool_list", "editor_state"):
                samples = []
                for _ in range(3):
                    if label == "http_health":
                        _, ms = client.health()
                    elif label == "tool_list":
                        _, ms = client.request("tools/list")
                    else:
                        _, ms = client.request(
                            "tools/call",
                            {"name": "editor.playmode.state", "arguments": {}},
                        )
                    samples.append(round(ms, 1))
                metrics[label] = {
                    "samples_ms": samples,
                    "median_ms": round(statistics.median(samples), 1),
                }
            print_json(metrics)
    finally:
        client.close()


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError, json.JSONDecodeError) as exc:
        print(f"MCP fast path error: {exc}", file=sys.stderr)
        raise SystemExit(2)
