"""在独立进程执行正式后台调度器及 Lua 探针的并发场景。"""
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Temp" / "BackgroundGenerationSchedulingValidation"
SCHEDULER = ROOT / "Assets/5_Scripts/5-0_WorldModel/Generation/ChunkGenerationScheduler.cs"
PROBE = ROOT / "Assets/5_Scripts/5-3_GamePlay/Development/Testing/GameplayTestSystemProbe.BackgroundGenerationScheduling.cs"
HYDROLOGY = ROOT / "Assets/5_Scripts/5-0_WorldModel/Generation/DeterministicChunkGenerator.HydrologyRegions.cs"


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    if "Task.Run" in HYDROLOGY.read_text(encoding="utf-8-sig"):
        raise AssertionError("水文预热仍绕过调度器启动后台任务")
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(properties, key).text = value
    items = ET.SubElement(project, "ItemGroup")
    for path in (SCHEDULER, PROBE, HERE / "Program.cs"):
        ET.SubElement(items, "Compile", Include=str(path))
    target = OUT / "BackgroundGenerationScheduling.csproj"
    ET.ElementTree(project).write(target, encoding="utf-8", xml_declaration=True)
    subprocess.run(["dotnet", "run", "--project", str(target), "--configuration", "Release"], cwd=ROOT, check=True)


if __name__ == "__main__":
    main()
