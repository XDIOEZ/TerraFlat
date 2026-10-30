"""Compare production macro hydrology against fixed pre-optimization signatures."""
from pathlib import Path
import argparse
import json
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent


def compile_world_model(output, response):
    text = response.read_text(encoding="utf-8-sig")
    for path in (ROOT / "Assets/5_Scripts/5-0_WorldModel").rglob("*.cs"):
        relative = path.relative_to(ROOT).as_posix()
        if f'"{relative}"' not in text:
            text += f'\n"{relative}"\n'
    references = [Path(p) if Path(p).is_absolute() else ROOT / p
                  for p in re.findall(r'^-r:"([^"]+)"', text, re.M)]
    engine = next(path for path in references if path.name == "UnityEngine.CoreModule.dll")
    editor = engine.parents[2]
    text = re.sub(r'^-out:.*$', f'-out:"{(output / "FlatWorld.WorldModel.dll").as_posix()}"', text, flags=re.M)
    text = re.sub(r'^-refout:.*$', f'-refout:"{(output / "FlatWorld.WorldModel.ref.dll").as_posix()}"', text, flags=re.M)
    source = "Assets/5_Scripts/5-0_WorldModel/Generation/DeterministicChunkGenerator.MacroHydrology.cs"
    managed = output / Path(source).name
    contents = (ROOT / source).read_text(encoding="utf-8-sig")
    contents, markers = re.subn(r'(ProfilerMarker \w+ =) new\("[^"]+"\);', r'\1 default;', contents)
    contents, scopes = re.subn(r'River\w+Marker\.Auto\(\)', 'default(ChunkGenerationTiming.StageMeasurement)', contents)
    assert markers == scopes == 4
    managed.write_text(contents, encoding="utf-8")
    text = text.replace(f'"{source}"', f'"{managed.as_posix()}"')
    source = "Assets/5_Scripts/5-0_WorldModel/Generation/SurfaceClimateBurstKernel.cs"
    managed = output / Path(source).name
    contents, count = re.subn(r'Type.GetType\("UnityEngine.Application, UnityEngine.CoreModule"\) != null',
                              'false', (ROOT / source).read_text(encoding="utf-8-sig"))
    assert count == 1
    managed.write_text(contents, encoding="utf-8")
    text = text.replace(f'"{source}"', f'"{managed.as_posix()}"')
    rsp = output / "WorldModel.rsp"
    rsp.write_text(text, encoding="utf-8")
    subprocess.run([str(editor / "NetCoreRuntime/dotnet.exe"), str(editor / "DotNetSdkRoslyn/csc.dll"),
                    "@" + str(rsp)], cwd=ROOT, check=True)
    return output / "FlatWorld.WorldModel.dll"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", type=Path, help="Use an already compiled WorldModel assembly")
    parser.add_argument("--label", default="current")
    parser.add_argument("--write-baseline", action="store_true", help="Save signatures from the specified old assembly")
    args = parser.parse_args()
    if args.write_baseline and args.assembly is None:
        parser.error("--write-baseline requires the old --assembly")
    output = ROOT / "Temp/SharedHydrologyHeightSampling" / args.label
    output.mkdir(parents=True, exist_ok=True)
    response = max((ROOT / "Library/Bee").rglob("FlatWorld.WorldModel.rsp"), key=lambda p: p.stat().st_mtime)
    assembly = (args.assembly or compile_world_model(output, response)).resolve()
    text = response.read_text(encoding="utf-8-sig")
    references = [Path(p) if Path(p).is_absolute() else ROOT / p
                  for p in re.findall(r'^-r:"([^"]+)"', text, re.M)]
    engine = next(path for path in references if path.name == "UnityEngine.CoreModule.dll")
    paths = [assembly, engine]
    paths += [ROOT / "Library/ScriptAssemblies" / f"{name}.dll" for name in
              ("FlatWorld.WorldTopology", "Unity.Mathematics", "Unity.Burst", "Unity.Collections")]
    paths += [path for path in references if path.name == "MemoryPack.Core.dll"]
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0",
                       "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(properties, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    ET.SubElement(group, "Compile", Include=str(HERE / "Program.cs"))
    for path in paths:
        ET.SubElement(ET.SubElement(group, "Reference", Include=path.stem), "HintPath").text = str(path)
    project_path = output / "SharedHydrologyHeightSampling.Tests.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    result_path = output / "results.json"
    subprocess.run(["dotnet", "run", "--project", str(project_path), "--configuration", "Release", "--",
                    str(result_path)], cwd=ROOT, check=True)
    results = json.loads(result_path.read_text(encoding="utf-8"))
    expected_path = HERE / "expected.json"
    if args.write_baseline:
        expected_path.write_text(json.dumps([{"name": row["name"], "hash": row["hash"]} for row in results],
                                            ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print("Saved baseline:", expected_path)
        return
    expected = json.loads(expected_path.read_text(encoding="utf-8"))
    assert len(expected) == len(results)
    for before, after in zip(expected, results):
        assert before["name"] == after["name"] and before["hash"] == after["hash"], after["name"]
        detail = f"MATCH {after['name']}: {after['hash']}"
        if "computed" in after:
            detail += f" production height computations {after['coldComputed']} -> {after['computed']}; shared hits {after['hits']}"
        print(detail)


if __name__ == "__main__":
    main()
