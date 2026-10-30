"""Compile v1 production generator and compare fixed terrain, height and ecology samples."""
from pathlib import Path
import importlib.util
import json
import re
import subprocess
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[3]
here = Path(__file__).resolve().parent
out = root / "Temp" / "WorldGenerationV1"
out.mkdir(parents=True, exist_ok=True)
spec = importlib.util.spec_from_file_location("world_model_compiler", root / "Tools/Tests/SharedHydrologyHeightSampling/verify.py")
compiler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compiler)
response = max((root / "Library/Bee").rglob("FlatWorld.WorldModel.rsp"), key=lambda path: path.stat().st_mtime)
assembly = compiler.compile_world_model(out, response)
text = response.read_text(encoding="utf-8-sig")
references = [Path(p) if Path(p).is_absolute() else root / p for p in re.findall(r'^-r:"([^"]+)"', text, re.M)]
engine = next(path for path in references if path.name == "UnityEngine.CoreModule.dll")
paths = [assembly, engine] + [root / "Library/ScriptAssemblies" / f"{name}.dll" for name in
                              ("FlatWorld.WorldTopology", "Unity.Mathematics", "Unity.Burst", "Unity.Collections")]
paths += [path for path in references if path.name == "MemoryPack.Core.dll"]
project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
props = ET.SubElement(project, "PropertyGroup")
for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "EnableDefaultCompileItems": "false"}.items():
    ET.SubElement(props, key).text = value
group = ET.SubElement(project, "ItemGroup")
ET.SubElement(group, "Compile", Include=str(here / "Program.cs"))
for path in paths:
    ET.SubElement(ET.SubElement(group, "Reference", Include=path.stem), "HintPath").text = str(path)
ET.ElementTree(project).write(out / "Tests.csproj", encoding="utf-8", xml_declaration=True)
result_path = out / "results.json"
subprocess.run(["dotnet", "run", "--project", str(out / "Tests.csproj"), "-c", "Release", "--", str(result_path)], cwd=root, check=True)
results = json.loads(result_path.read_text(encoding="utf-8"))
expected = json.loads((here / "expected.json").read_text(encoding="utf-8"))
if results != expected: raise SystemExit(f"v1 golden samples changed: {results}")
for name, value in results.items(): print(f"MATCH {name}: {value}")
