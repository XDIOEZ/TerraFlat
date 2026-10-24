"""在 Temp 中编译并运行大资源纯规则，不刷新、不暂停正在运行的 Unity。"""
from __future__ import annotations
import argparse
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Temp' / 'SharedNaturalResourcesValidation'

def build_targets() -> Path:
    """Unity 播放时项目文件尚未更新，外部构建临时补入各程序集的新脚本。"""
    xml = ET.Element('Project')
    for assembly, folder in [('GamePlay', ROOT / 'Assets/5_Scripts/5-3_GamePlay'),
                             ('UI', ROOT / 'Assets/5_Scripts/5-5_UI'),
                             ('FlatWorld.WorldModel', ROOT / 'Assets/5_Scripts/5-0_WorldModel')]:
        group = ET.SubElement(xml, 'ItemGroup', Condition=f"'$(MSBuildProjectName)' == '{assembly}'")
        for source in folder.rglob('*.cs'):
            parent = source.parent
            while parent != folder.parent:
                definitions = list(parent.glob('*.asmdef'))
                if definitions:
                    owner = json.loads(definitions[0].read_text(encoding='utf-8-sig'))['name']
                    break
                parent = parent.parent
            else:
                continue
            if owner == assembly:
                ET.SubElement(group, 'Compile', Include=str(source), Exclude='@(Compile)')
    target = OUT / 'CurrentSources.targets'
    ET.ElementTree(xml).write(target, encoding='utf-8', xml_declaration=True)
    return target

def run(command: list[str], log: Path) -> None:
    with log.open('w', encoding='utf-8') as output:
        result = subprocess.run(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT)
    text = log.read_text(encoding='utf-8-sig', errors='replace')
    print(text[-18000:])
    if result.returncode:
        raise SystemExit(result.returncode)

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--skip-build', action='store_true')
    options = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    if not options.skip_build:
        target = build_targets()
        run(['dotnet', 'build', 'GamePlay.csproj', '-nologo', '-v:q', '-clp:ErrorsOnly',
             '--ignore-failed-sources', f'-p:CustomAfterMicrosoftCommonTargets={target}'], OUT / 'build.log')
    # 独立 CLR 没有 Unity 原生 Profiler，只在临时副本移除四个 Marker 初始化，不改生成算法。
    source = ROOT / 'Assets/5_Scripts/5-0_WorldModel/Generation/DeterministicChunkGenerator.MacroHydrology.cs'
    contents, count = re.subn(r'(ProfilerMarker \w+ =) new\("[^"]+"\);', r'\1 default;', source.read_text(encoding='utf-8-sig'))
    if count != 4:
        raise RuntimeError('Profiler instrumentation changed; review external-test isolation before proceeding.')
    managed = OUT / source.name
    managed.write_text(contents, encoding='utf-8')
    targets = ET.Element('Project')
    ET.SubElement(targets, 'Import', Project=str(build_targets()))
    group = ET.SubElement(targets, 'ItemGroup', Condition="'$(MSBuildProjectName)' == 'FlatWorld.WorldModel'")
    ET.SubElement(group, 'Compile', Remove=str(source) + ';' + str(source.relative_to(ROOT)))
    ET.SubElement(group, 'Compile', Include=str(managed))
    managed_targets = OUT / 'ManagedWorldModel.targets'
    ET.ElementTree(targets).write(managed_targets, encoding='utf-8', xml_declaration=True)
    managed_output = OUT / 'Managed'
    run(['dotnet', 'build', 'FlatWorld.WorldModel.csproj', '--no-restore', '-nologo', '-v:q', '-clp:ErrorsOnly',
         f'-p:CustomAfterMicrosoftCommonTargets={managed_targets}', f'-p:OutputPath={managed_output}'], OUT / 'managed-build.log')
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(project, 'PropertyGroup')
    for key, value in {'OutputType': 'Exe', 'TargetFramework': 'net8.0',
                       'ImplicitUsings': 'enable', 'EnableDefaultCompileItems': 'false'}.items():
        ET.SubElement(props, key).text = value
    group = ET.SubElement(project, 'ItemGroup')
    ET.SubElement(group, 'Compile', Include=str(Path(__file__).with_name('Program.cs')))
    for file in [str(managed_output / 'FlatWorld.WorldModel.dll'), str(managed_output / 'FlatWorld.WorldTopology.dll'),
                 'Library/ScriptAssemblies/Unity.Mathematics.dll', 'Library/ScriptAssemblies/Unity.Burst.dll']:
        reference = ET.SubElement(group, 'Reference', Include=Path(file).stem)
        ET.SubElement(reference, 'HintPath').text = str(ROOT / file)
    source_project = ET.parse(ROOT / 'FlatWorld.WorldModel.csproj')
    for item in source_project.iter():
        if item.tag.split('}')[-1] == 'Reference' and item.get('Include') == 'UnityEngine.CoreModule':
            hint = next(child.text for child in item if child.tag.split('}')[-1] == 'HintPath')
            reference = ET.SubElement(group, 'Reference', Include='UnityEngine.CoreModule')
            ET.SubElement(reference, 'HintPath').text = str(ROOT / hint)
    target = OUT / 'SharedNaturalResources.Tests.csproj'
    ET.ElementTree(project).write(target, encoding='utf-8', xml_declaration=True)
    run(['dotnet', 'run', '--project', str(target), '--configuration', 'Release', '--', str(ROOT)], OUT / 'tests.log')

if __name__ == '__main__':
    main()
