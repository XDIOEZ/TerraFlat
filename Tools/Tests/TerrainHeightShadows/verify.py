"""Compile and exercise the production per-flush terrain-shadow sample cache without Unity."""
from __future__ import annotations

import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / "Temp" / "TerrainHeightShadowsValidation"


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for key, value in {
        "OutputType": "Exe",
        "TargetFramework": "net8.0",
        "ImplicitUsings": "enable",
        "EnableDefaultCompileItems": "false",
    }.items():
        ET.SubElement(properties, key).text = value

    compile_group = ET.SubElement(project, "ItemGroup")
    for source in (
        Path(__file__).with_name("Program.cs"),
        ROOT / "Assets/5_Scripts/5-3_GamePlay/World/WorldModel/Presentation/TerrainShadowSurfaceSampleCache.cs",
    ):
        ET.SubElement(compile_group, "Compile", Include=str(source))

    project_path = OUTPUT / "TerrainHeightShadows.Tests.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    subprocess.run(
        ["dotnet", "run", "--project", str(project_path), "--configuration", "Release"],
        cwd=ROOT,
        check=True,
    )


if __name__ == "__main__":
    main()
