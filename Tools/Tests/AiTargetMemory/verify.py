"""在编辑器外执行正式狼记忆和集火入口，不暂停或重载 Unity。"""
from pathlib import Path
import re
import subprocess
import sys


def extract_method(source: str, signature: str) -> str:
    if source.count(signature) != 1:
        raise ValueError(f"生产方法签名不唯一或已变化：{signature}")
    start = source.index(signature)
    cursor = source.index("{", start) + 1
    depth = 1
    while depth and cursor < len(source):
        depth += (source[cursor] == "{") - (source[cursor] == "}")
        cursor += 1
    if depth:
        raise ValueError(f"生产方法缺少闭括号：{signature}")
    return source[start:cursor]


def main() -> None:
    root = Path(__file__).resolve().parents[3]
    source_dir = root / "Assets/5_Scripts/5-3_GamePlay/Entities/AI"
    output = root / ".codex-tmp/ai-target-memory-tests"
    output.mkdir(parents=True, exist_ok=True)
    source = (source_dir / "AI_Wolf.cs").read_text(encoding="utf-8-sig")
    methods = [extract_method(source, signature) for signature in (
        "public void TriggerPackAttack(Item threatSource)",
        "public void ReceivePackCall(Item threatSource, AI_Wolf caller)",
        "private void RefreshThreatTarget()",
    )]
    generated = "using System;\nusing UnityEngine;\npublic partial class AI_Wolf\n{\n"
    generated += "\n".join(methods) + "\n}\n"
    (output / "ProductionEntryPoints.cs").write_text(generated, encoding="utf-8")
    memory = (source_dir / "AI_Wolf.TargetMemory.cs").read_text(encoding="utf-8-sig")
    # 仅移除 Inspector 注解，字段和方法原样编译；世界、导航和感知由替身提供输入。
    memory = memory.replace("using Sirenix.OdinInspector;", "")
    memory = re.sub(r"^\s*\[TabGroup\([^\n]+\n", "\n", memory, flags=re.MULTILINE)
    (output / "ProductionMemory.cs").write_text(memory, encoding="utf-8")
    (output / "Harness.cs").write_text(Path(__file__).with_name("Harness.cs").read_text(encoding="utf-8"), encoding="utf-8")
    (output / "MemoryTests.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
        '<ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable>'
        '</PropertyGroup></Project>', encoding="utf-8")
    subprocess.run(["dotnet", "run", "--project", str(output / "MemoryTests.csproj")], cwd=root, check=True)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
