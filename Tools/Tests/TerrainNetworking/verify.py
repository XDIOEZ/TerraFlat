"""编译真实源码并运行地形复制规则；所有输出进入 Temp，不操作 Unity PlayMode。"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Temp" / "TerrainNetworkingValidation"


def current_sources() -> Path:
    project = ET.Element("Project")
    source_root = ROOT / "Assets" / "5_Scripts"
    owners = {}
    for definition in source_root.rglob("*.asmdef"):
        owners[definition.parent] = json.loads(definition.read_text(encoding="utf-8-sig"))["name"]
    groups = {}
    for source in source_root.rglob("*.cs"):
        owner = next((owners[parent] for parent in source.parents if parent in owners), None)
        if owner is None:
            continue
        if owner not in groups:
            groups[owner] = ET.SubElement(project, "ItemGroup", Condition=f"'$(MSBuildProjectName)' == '{owner}'")
        ET.SubElement(groups[owner], "Compile", Include=str(source), Exclude="@(Compile)")
    # PlayMode 中生成的项目清单也可能还保留已删除/移动的脚本，只移除确认不存在的Assets源码。
    for owner, group in groups.items():
        source_project = ROOT / (owner + '.csproj')
        if not source_project.exists():
            continue
        for item in ET.parse(source_project).iter():
            include = item.get('Include', '')
            if item.tag.split('}')[-1] != 'Compile' or not include.replace('\\', '/').startswith('Assets/5_Scripts/'):
                continue
            if not (ROOT / include).exists():
                ET.SubElement(group, 'Compile', Remove=include + ';' + str(ROOT / include))
    target = OUT / "CurrentSources.targets"
    ET.ElementTree(project).write(target, encoding="utf-8", xml_declaration=True)
    return target


def run(command: list[str], log_name: str) -> None:
    log = OUT / log_name
    with log.open("w", encoding="utf-8") as output:
        completed = subprocess.run(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT,
                                   env={**os.environ, 'DOTNET_CLI_UI_LANGUAGE': 'en-US'})
    print(log.read_text(encoding="utf-8-sig", errors="replace")[-16000:])
    if completed.returncode:
        raise SystemExit(completed.returncode)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--assembly-dir", type=Path, default=ROOT / "Temp/bin/Debug")
    options = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    if not options.skip_build:
        run(["dotnet", "build", "FlatWorld.Networking.Gameplay.csproj", "-nologo", "-v:q", "-clp:ErrorsOnly",
             "--ignore-failed-sources", f"-p:CustomAfterMicrosoftCommonTargets={current_sources()}"], "build.log")
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    props = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "ImplicitUsings": "enable",
                       "EnableDefaultCompileItems": "false", "DefineConstants": "TERRAIN_STANDALONE_TESTS"}.items():
        ET.SubElement(props, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    sources = [Path(__file__).with_name("Program.cs"),
               ROOT / "Assets/5_Scripts/5-4_Networking/Gameplay/NetworkTerrainChunkCursor.cs",
               ROOT / "Assets/5_Scripts/5-4_Networking/Gameplay/Testing/GameplayTestNetworkTerrainRules.cs"]
    for source in sources:
        ET.SubElement(group, "Compile", Include=str(source))
    references = [options.assembly_dir / "FlatWorld.WorldModel.dll",
                  options.assembly_dir / "FlatWorld.WorldTopology.dll",
                  ROOT / "Library/ScriptAssemblies/Unity.Mathematics.dll",
                  ROOT / "Library/ScriptAssemblies/Unity.Burst.dll"]
    for item in ET.parse(ROOT / "FlatWorld.WorldModel.csproj").iter():
        if item.tag.split("}")[-1] == "Reference" and item.get("Include") == "UnityEngine.CoreModule":
            references.append(ROOT / next(child.text for child in item if child.tag.split("}")[-1] == "HintPath"))
    for path in references:
        if not path.is_file():
            raise FileNotFoundError(path)
        reference = ET.SubElement(group, "Reference", Include=path.stem)
        ET.SubElement(reference, "HintPath").text = str(path)
    target = OUT / "TerrainNetworking.Tests.csproj"
    ET.ElementTree(project).write(target, encoding="utf-8", xml_declaration=True)
    run(["dotnet", "run", "--project", str(target), "--configuration", "Release"], "tests.log")


if __name__ == "__main__":
    main()
