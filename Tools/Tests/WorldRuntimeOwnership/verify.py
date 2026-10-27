"""编译当前 WorldModel 源码并在独立 .NET 进程验证世界与调度器所有权。"""
from __future__ import annotations

from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / "Temp" / "WorldRuntimeOwnershipValidation"


def run(command: list[str]) -> None:
    subprocess.run(command, cwd=ROOT, check=True)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    response = max((ROOT / "Library/Bee").rglob("FlatWorld.WorldModel.rsp"), key=lambda path: path.stat().st_mtime)
    content = response.read_text(encoding="utf-8-sig")
    references = [Path(path) if Path(path).is_absolute() else ROOT / path
                  for path in re.findall(r'^-r:"([^"]+)"', content, re.M)]
    engine = next(path for path in references if path.name == "UnityEngine.CoreModule.dll")
    editor_data = engine.parents[2]
    content = re.sub(r'^-out:.*$', lambda _: f'-out:"{OUTPUT / "FlatWorld.WorldModel.dll"}"', content, flags=re.M)
    content = re.sub(r'^-refout:.*$', lambda _: f'-refout:"{OUTPUT / "FlatWorld.WorldModel.ref.dll"}"', content, flags=re.M)
    response_copy = OUTPUT / "WorldModel.rsp"
    response_copy.write_text(content, encoding="utf-8")
    run([str(editor_data / "NetCoreRuntime/dotnet.exe"), str(editor_data / "DotNetSdkRoslyn/csc.dll"), "@" + str(response_copy)])

    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0",
                       "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(properties, key).text = value
    items = ET.SubElement(project, "ItemGroup")
    ET.SubElement(items, "Compile", Include=str(Path(__file__).with_name("Program.cs")))
    memory_pack = next(path for path in references if path.name == "MemoryPack.Core.dll")
    for path in [OUTPUT / "FlatWorld.WorldModel.dll", engine, memory_pack,
                 *(ROOT / "Library/ScriptAssemblies" / f"{name}.dll" for name in
                   ("FlatWorld.WorldTopology", "Unity.Mathematics", "Unity.Burst", "Unity.Collections"))]:
        if not path.exists():
            raise FileNotFoundError(path)
        ET.SubElement(ET.SubElement(items, "Reference", Include=path.stem), "HintPath").text = str(path)
    target = OUTPUT / "WorldRuntimeOwnership.Tests.csproj"
    ET.ElementTree(project).write(target, encoding="utf-8", xml_declaration=True)
    run(["dotnet", "run", "--project", str(target), "--configuration", "Release"])


if __name__ == "__main__":
    main()
