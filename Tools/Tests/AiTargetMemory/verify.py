"""在编辑器外执行正式目标记忆、搜索动作和群体情报入口。"""
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


def extract_block(source: str, signature: str) -> str:
    if source.count(signature) != 1:
        raise ValueError(f"生产代码签名不唯一或已变化：{signature}")
    start = source.index(signature)
    cursor = source.index("{", start) + 1
    depth = 1
    while depth and cursor < len(source):
        depth += (source[cursor] == "{") - (source[cursor] == "}")
        cursor += 1
    if depth:
        raise ValueError(f"生产代码缺少闭括号：{signature}")
    return source[start:cursor]


def main() -> None:
    root = Path(__file__).resolve().parents[3]
    source_dir = root / "Assets/5_Scripts/5-3_GamePlay/Entities/AI"
    output = root / ".codex-tmp/ai-target-memory-tests"
    output.mkdir(parents=True, exist_ok=True)
    rules = (source_dir / "AnimalLandTargetCapabilities.cs").read_text(encoding="utf-8-sig")
    nodes = (source_dir / "AnimalLandBehaviorNodes.cs").read_text(encoding="utf-8-sig")
    pack = (source_dir / "AnimalPackCapability.cs").read_text(encoding="utf-8-sig")
    generated = "using System;\nusing System.Collections.Generic;\nusing Newtonsoft.Json.Linq;\nusing UnityEngine;\n"
    generated += extract_block(rules, "internal static class AnimalLandTargetRules") + "\n"
    generated += extract_block(rules, "public sealed class AnimalTargetMemoryCapability") + "\n"
    generated += extract_block(nodes, "internal sealed class AnimalSearchAction") + "\n"
    generated += "public partial class AnimalPackCapability\n{\n"
    generated += "\n".join(extract_block(pack, signature) for signature in (
        "public void TriggerGroupAttack(Item target)",
        "public void ReceiveGroupCall(Item target, AnimalPackCapability caller)",
        "private void CallNearbyAllies(bool immediate)",
    )) + "\n}\n"
    production = output / "ProductionTargetMemory.cs"
    production.write_text(generated, encoding="utf-8")

    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "ImplicitUsings": "enable",
                       "Nullable": "disable", "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(properties, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    for path in (source_dir / "AnimalBehaviorTreeRuntime.cs", production, Path(__file__).with_name("Harness.cs")):
        ET.SubElement(group, "Compile", Include=str(path))
    reference = ET.SubElement(group, "Reference", Include="Newtonsoft.Json")
    ET.SubElement(reference, "HintPath").text = str(root / "Library/PackageCache/com.unity.nuget.newtonsoft-json@3.2.1/Runtime/Newtonsoft.Json.dll")
    project_path = output / "MemoryTests.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    subprocess.run(["dotnet", "run", "--project", str(project_path)], cwd=root, check=True)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
