"""通过本机 LMMS MCP 加载、保存并渲染原创游戏反馈音效工程。"""

import argparse
import asyncio
import json
import os
import sys
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


ROOT = Path(__file__).resolve().parent
LMMS = os.environ.get("LMMS_EXECUTABLE", r"G:\LMMS\lmms.exe")


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--list-tools", action="store_true")
    parser.add_argument("--render", action="store_true")
    args = parser.parse_args()

    env = dict(os.environ, LMMS_EXECUTABLE=LMMS, LMMS_PROJECTS_DIR=str(ROOT))
    server = StdioServerParameters(command=sys.executable, args=["-m", "lmms_mcp"], env=env)
    async with stdio_client(server) as (reader, writer):
        async with ClientSession(reader, writer) as session:
            await session.initialize()
            tools = await session.list_tools()
            if args.list_tools:
                for tool in tools.tools:
                    print(tool.name, json.dumps(tool.input_schema, ensure_ascii=False))
                return
            if not args.render:
                parser.error("Specify --list-tools or --render")
            project = ROOT / "Gameplay_Feedback.mmp"
            output = ROOT / "renders" / "Gameplay_Feedback_raw.wav"
            if not project.is_file():
                raise FileNotFoundError(project)
            output.parent.mkdir(exist_ok=True)
            # 只使用 LMMS MCP 的真实工程加载和 WAV 渲染，不生成替代声音。
            for action, kwargs in [
                ("load_project", {"path": str(project)}),
                ("save_project", {"path": str(project), "compressed": False}),
                ("render_project", {"output_path": str(output),
                                    "file_format": "wav", "samplerate": 48000}),
            ]:
                result = await session.call_tool(action, kwargs)
                messages = [getattr(part, "text", "") for part in result.content]
                print(action, " | ".join(messages)[:350])
                if result.is_error:
                    raise RuntimeError(f"LMMS MCP {action} failed")
            if not output.is_file() or output.stat().st_size < 1024:
                raise RuntimeError("LMMS MCP did not render a valid WAV")
            print(f"Rendered {output} ({output.stat().st_size} bytes)")


if __name__ == "__main__":
    asyncio.run(main())
