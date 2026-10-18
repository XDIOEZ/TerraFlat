"""Compile the production hydrology assembly into Temp and compare saved raster output.

This runs managed code only and never touches the running Unity editor.
"""
from pathlib import Path
import argparse
import json
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--label", default="current")
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--revision", help="Compile C# sources from a Git revision without changing the checkout")
    args = parser.parse_args()
    output = ROOT / "Temp/HydrologyRasterValidation" / args.label
    output.mkdir(parents=True, exist_ok=True)
    response = max((ROOT / "Library/Bee").rglob("FlatWorld.WorldModel.rsp"), key=lambda p: p.stat().st_mtime)
    text = response.read_text(encoding="utf-8-sig")
    snapshots = {}
    if args.revision:
        sources = re.findall(r'^"([^"\r\n]+\.cs)"\r?$', text, re.M)
        source_requests = "".join(f"{args.revision}:{source}\n" for source in sources).encode()
        objects = subprocess.run(["git", "cat-file", "--batch"], input=source_requests,
                                 cwd=ROOT, stdout=subprocess.PIPE, check=True).stdout
        cursor = 0
        for source in sources:
            header_end = objects.index(b"\n", cursor)
            header = objects[cursor:header_end].split()
            if len(header) != 3 or header[1] != b"blob":
                raise ValueError(f"Source unavailable at {args.revision}: {source}")
            size = int(header[2])
            snapshot = output / "Sources" / source
            snapshot.parent.mkdir(parents=True, exist_ok=True)
            snapshot.write_bytes(objects[header_end + 1:header_end + 1 + size])
            cursor = header_end + 1 + size + 1
            snapshots[source] = snapshot
            text = text.replace(f'"{source}"', f'"{snapshot.as_posix()}"')
    references = [Path(p) if Path(p).is_absolute() else ROOT / p for p in re.findall(r'^-r:"([^"]+)"', text, re.M)]
    engine = next(p for p in references if p.name == "UnityEngine.CoreModule.dll")
    editor_data = engine.parents[2]
    text = re.sub(r'^-out:.*$', f'-out:"{(output / "FlatWorld.WorldModel.dll").as_posix()}"', text, flags=re.M)
    text = re.sub(r'^-refout:.*$', f'-refout:"{(output / "FlatWorld.WorldModel.ref.dll").as_posix()}"', text, flags=re.M)
    # 独立 CLR 没有 Unity 原生 Profiler，只替换四个 Marker 初始化，不改被测算法。
    source = "Assets/5_Scripts/5-0_WorldModel/Generation/DeterministicChunkGenerator.MacroHydrology.cs"
    managed_source = output / "DeterministicChunkGenerator.MacroHydrology.cs"
    contents = snapshots.get(source, ROOT / source).read_text(encoding="utf-8-sig")
    contents, marker_count = re.subn(r'(ProfilerMarker \w+ =) new\("[^"]+"\);', r'\1 default;', contents)
    assert marker_count == 4
    managed_source.write_text(contents, encoding="utf-8")
    text = text.replace(snapshots.get(source, Path(source)).as_posix(), managed_source.as_posix())
    rsp = output / "WorldModel.rsp"
    rsp.write_text(text, encoding="utf-8")
    subprocess.run([str(editor_data / "NetCoreRuntime/dotnet.exe"), str(editor_data / "DotNetSdkRoslyn/csc.dll"), "@" + str(rsp)], cwd=ROOT, check=True)
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(properties, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    ET.SubElement(group, "Compile", Include=str(Path(__file__).with_name("Program.cs")))
    paths = [output / "FlatWorld.WorldModel.dll", engine]
    paths += [ROOT / "Library/ScriptAssemblies" / f"{name}.dll" for name in
              ("FlatWorld.WorldTopology", "Unity.Mathematics", "Unity.Burst", "Unity.Collections")]
    for path in paths:
        ET.SubElement(ET.SubElement(group, "Reference", Include=path.stem), "HintPath").text = str(path)
    project_path = output / "HydrologyRaster.Tests.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    result_path = output / "results.json"
    subprocess.run(["dotnet", "run", "--project", str(project_path), "--configuration", "Release", "--", str(result_path)], cwd=ROOT, check=True)
    baseline = args.baseline or Path(__file__).with_name("expected.json")
    if baseline.exists():
        old = json.loads(baseline.read_text(encoding="utf-8"))
        new = json.loads(result_path.read_text(encoding="utf-8"))
        assert len(old) == len(new)
        for before, after in zip(old, new):
            for key in ("name", "waterCount", "floodplainCount", "waterHash", "floodplainHash"):
                assert before[key] == after[key], f"{after['name']} differs in {key}: {before[key]} / {after[key]}"
            detail = f"MATCH {after['name']}: water={after['waterCount']} floodplain={after['floodplainCount']}"
            if "milliseconds" in before:
                detail += (f" median={before['milliseconds']:.2f} -> {after['milliseconds']:.2f}ms "
                           f"allocated={before['allocatedBytes']} -> {after['allocatedBytes']} "
                           f"heightLookups={before['heightLookups']} -> {after['heightLookups']}")
            print(detail)
    else:
        raise FileNotFoundError(f"Missing baseline: {baseline}")


if __name__ == "__main__":
    main()
