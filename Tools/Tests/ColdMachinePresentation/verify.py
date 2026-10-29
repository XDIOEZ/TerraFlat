"""在独立 .NET 进程验证正式配置入口和培育仓绑定事务，不调用运行中的 Unity。"""
from __future__ import annotations

import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Temp/ColdMachinePresentationValidation/isolated"
MACHINES = ROOT / "Assets/5_Scripts/5-3_GamePlay/World/Machines"
PRESENTATION = ROOT / "Assets/5_Scripts/5-3_GamePlay/World/WorldModel/Presentation"


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    catalog = (MACHINES / "MachineCatalog.cs").read_text(encoding="utf-8-sig")
    start = catalog.index("    [MemoryPack.MemoryPackIgnore]\n    [JsonIgnore] public MachineContent Content")
    end = catalog.index('    public string Kind =', start)
    # 原样编译正式入口属性，Unity 对象与快照编解码使用边界替身，不重写被测方法。
    contract = OUT / "MachineDefinitionContentContract.cs"
    contract.write_text("using Newtonsoft.Json;\npublic sealed class MachineDefinition\n{\n"
                        "    public string Id;\n" + catalog[start:end] + "}\n", encoding="utf-8")
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0",
                       "EnableDefaultCompileItems": "false", "Nullable": "disable"}.items():
        ET.SubElement(properties, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    for path in (Path(__file__).with_name("Program.cs"), Path(__file__).with_name("BoundaryDoubles.cs"),
                 contract, MACHINES / "MachineContent.cs", PRESENTATION / "CultivatorPlotPresentation.cs"):
        ET.SubElement(group, "Compile", Include=str(path.resolve()))
    assembly = None
    for node in ET.parse(ROOT / "GamePlay.csproj").iter():
        if node.tag.split("}")[-1] == "Reference" and node.get("Include", "").split(",")[0] == "Newtonsoft.Json":
            assembly = next(child.text for child in node if child.tag.split("}")[-1] == "HintPath")
    if assembly is None or not (ROOT / assembly).is_file():
        raise FileNotFoundError("未找到项目正式 Newtonsoft.Json 引用。")
    reference = ET.SubElement(group, "Reference", Include="Newtonsoft.Json")
    ET.SubElement(reference, "HintPath").text = str((ROOT / assembly).resolve())
    target = OUT / "ColdMachinePresentation.Tests.csproj"
    ET.ElementTree(project).write(target, encoding="utf-8", xml_declaration=True)
    result = subprocess.run(["dotnet", "run", "--project", str(target), "--configuration", "Release"],
                            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
                            env={**os.environ, "DOTNET_CLI_UI_LANGUAGE": "en-US"})
    text = result.stdout + result.stderr
    (OUT / "tests.log").write_text(text, encoding="utf-8")
    print(text)
    raise SystemExit(result.returncode)


if __name__ == "__main__":
    main()
