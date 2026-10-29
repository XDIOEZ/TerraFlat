"""在独立.NET进程编译并验证正式储罐矩形合并器，不连接或改动Unity。"""
from __future__ import annotations

import os
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Temp" / "CombinedTankColliderValidation"


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "ImplicitUsings": "enable",
                       "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(properties, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    for source in [Path(__file__).with_name("Program.cs"),
                   ROOT / "Assets/5_Scripts/5-3_GamePlay/Physics2D/WorldPhysicsRectangleMerger2D.cs"]:
        ET.SubElement(group, "Compile", Include=str(source))
    engine_path = None
    for item in ET.parse(ROOT / "GamePlay.csproj").iter():
        if item.tag.split("}")[-1] == "Reference" and item.get("Include") == "UnityEngine.CoreModule":
            engine_path = next(child.text for child in item if child.tag.split("}")[-1] == "HintPath")
    if engine_path is None or not Path(engine_path).is_file():
        raise FileNotFoundError("GamePlay.csproj没有可用的UnityEngine.CoreModule引用。")
    reference = ET.SubElement(group, "Reference", Include="UnityEngine.CoreModule")
    ET.SubElement(reference, "HintPath").text = engine_path
    target = OUT / "CombinedTankColliders.Tests.csproj"
    ET.ElementTree(project).write(target, encoding="utf-8", xml_declaration=True)
    log_path = OUT / "tests.log"
    with log_path.open("w", encoding="utf-8") as output:
        completed = subprocess.run(["dotnet", "run", "--project", str(target), "--configuration", "Release"],
                                   cwd=ROOT, stdout=output, stderr=subprocess.STDOUT,
                                   env={**os.environ, "DOTNET_CLI_UI_LANGUAGE": "en-US"})
    print(log_path.read_text(encoding="utf-8-sig", errors="replace"))
    raise SystemExit(completed.returncode)


if __name__ == "__main__":
    main()
