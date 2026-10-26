"""Compile production coast and runtime rules outside Unity without disrupting Play Mode."""
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / "Temp/OceanCurrentValidation"


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    response = max((ROOT / "Library/Bee").rglob("FlatWorld.WorldModel.rsp"), key=lambda p: p.stat().st_mtime)
    text = response.read_text(encoding="utf-8-sig")
    for path in (ROOT / "Assets/5_Scripts/5-0_WorldModel").rglob("*.cs"):
        relative = path.relative_to(ROOT).as_posix()
        if f'"{relative}"' not in text:
            text += f'\n"{relative}"\n'
    refs = [Path(p) if Path(p).is_absolute() else ROOT / p
            for p in re.findall(r'^-r:"([^"]+)"', text, re.M)]
    engine = next(p for p in refs if p.name == "UnityEngine.CoreModule.dll")
    editor = engine.parents[2]
    text = re.sub(r'^-out:.*$', f'-out:"{(OUTPUT / "FlatWorld.WorldModel.dll").as_posix()}"', text, flags=re.M)
    text = re.sub(r'^-refout:.*$', f'-refout:"{(OUTPUT / "FlatWorld.WorldModel.ref.dll").as_posix()}"', text, flags=re.M)
    source = "Assets/5_Scripts/5-0_WorldModel/Generation/DeterministicChunkGenerator.MacroHydrology.cs"
    managed = OUTPUT / Path(source).name
    # The standalone CLR has no native Unity profiler; only marker construction is disabled.
    contents, count = re.subn(r'(ProfilerMarker \w+ =) new\("[^"]+"\);', r'\1 default;',
                             (ROOT / source).read_text(encoding="utf-8-sig"))
    assert count == 4
    managed.write_text(contents, encoding="utf-8")
    text = text.replace(f'"{source}"', f'"{managed.as_posix()}"')
    source = "Assets/5_Scripts/5-0_WorldModel/Generation/SurfaceClimateBurstKernel.cs"
    managed = OUTPUT / Path(source).name
    # Keep the same Burst calculation but execute managed math without Unity native calls.
    contents, count = re.subn(r'Type.GetType\("UnityEngine.Application, UnityEngine.CoreModule"\) != null', 'false',
                             (ROOT / source).read_text(encoding="utf-8-sig"))
    assert count == 1
    managed.write_text(contents, encoding="utf-8")
    text = text.replace(f'"{source}"', f'"{managed.as_posix()}"')
    rsp = OUTPUT / "WorldModel.rsp"
    rsp.write_text(text, encoding="utf-8")
    subprocess.run([str(editor / "NetCoreRuntime/dotnet.exe"), str(editor / "DotNetSdkRoslyn/csc.dll"),
                    "@" + str(rsp)], cwd=ROOT, check=True)

    runtime_source = (ROOT / "Assets/5_Scripts/5-3_GamePlay/World/Chunk/Management/ChunkMgr.TileEffects.cs").read_text(encoding="utf-8-sig")
    enum = re.search(r'public enum RuntimeWaterCurrentKind : byte\s*\{[^}]+\}', runtime_source).group(0)
    (OUTPUT / "RuntimeWaterCurrentKind.cs").write_text(enum, encoding="utf-8")
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    props = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0",
                       "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(props, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    sources = [Path(__file__).with_name("Program.cs"), Path(__file__).with_name("RuntimeReferenceStubs.cs"),
               OUTPUT / "RuntimeWaterCurrentKind.cs",
               ROOT / "Assets/5_Scripts/5-3_GamePlay/World/Environment/WaterEnvironmentRules.cs",
               ROOT / "Assets/5_Scripts/5-3_GamePlay/World/Chunk/Management/WorldItemWaterRules.cs"]
    for source in sources:
        ET.SubElement(group, "Compile", Include=str(source))
    paths = [OUTPUT / "FlatWorld.WorldModel.dll", engine]
    paths += [ROOT / "Library/ScriptAssemblies" / f"{name}.dll" for name in
              ("FlatWorld.WorldTopology", "Unity.Mathematics", "Unity.Burst", "Unity.Collections")]
    paths += [p for p in refs if p.name == "MemoryPack.Core.dll"]
    for path in paths:
        ET.SubElement(ET.SubElement(group, "Reference", Include=path.stem), "HintPath").text = str(path)
    project_path = OUTPUT / "OceanCurrent.Tests.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    subprocess.run(["dotnet", "run", "--project", str(project_path), "--configuration", "Release", "--", *sys.argv[1:]],
                   cwd=ROOT, check=True)


if __name__ == "__main__":
    main()
