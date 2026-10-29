"""复用未改动的 Data 工厂程序集验证快照，不连接或刷新 Unity。"""
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Temp/Tests/NaturalSnapshots'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(project, 'PropertyGroup')
    for key, value in {'OutputType': 'Exe', 'TargetFramework': 'net8.0',
                       'ImplicitUsings': 'enable', 'EnableDefaultCompileItems': 'false'}.items():
        ET.SubElement(props, key).text = value
    group = ET.SubElement(project, 'ItemGroup')
    ET.SubElement(group, 'Compile', Include=str(Path(__file__).with_name('Program.cs')))
    references = [ROOT / 'Library/ScriptAssemblies' / f'{name}.dll' for name in
                  ('Data', 'FlatWorld.ContentCache', 'NaughtyAttributes.Core', 'Kybernetik.UltEvents', 'MemoryPack.Unity')]
    names = {'UnityEngine.CoreModule', 'MemoryPack.Core', 'FastCloner', 'Newtonsoft.Json',
             'Sirenix.OdinInspector.Attributes'}
    for node in ET.parse(ROOT / 'Data.csproj').iter():
        if node.tag.split('}')[-1] == 'Reference' and node.get('Include') in names:
            hint = next(c.text for c in node if c.tag.split('}')[-1] == 'HintPath')
            references.append(ROOT / hint)
    for path in references:
        if not path.is_file():
            raise FileNotFoundError(path)
        reference = ET.SubElement(group, 'Reference', Include=path.stem)
        ET.SubElement(reference, 'HintPath').text = str(path)
    target = OUT / 'NaturalSnapshots.Tests.csproj'
    ET.ElementTree(project).write(target, encoding='utf-8', xml_declaration=True)
    result = subprocess.run(['dotnet', 'run', '--project', str(target), '--configuration', 'Release'],
                            cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
    output = result.stdout + result.stderr
    (OUT / 'result.log').write_text(output, encoding='utf-8')
    print(output)
    if result.returncode:
        raise SystemExit(result.returncode)
    renderer = (ROOT / 'Assets/5_Scripts/5-3_GamePlay/World/WorldModel/Presentation/ChunkNaturalItemRenderer.cs').read_text(encoding='utf-8-sig')
    natural = (ROOT / 'Assets/5_Scripts/5-3_GamePlay/Entities/NaturalECS/NaturalEntityEcsService.Physics.cs').read_text(encoding='utf-8-sig')
    assert renderer.count('ItemInstanceDataFactory.CloneRuntime(item.itemData)') == 2
    assert 'FastCloner.FastCloner.DeepClone(item.itemData)' not in renderer
    assert 'source == null ? null : ItemInstanceDataFactory.CloneRuntime(source)' in natural
    assert 'if (changedData?.SharedConfiguration != null)' in renderer
    assert 'changedData = ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, changedData)' in renderer
    print('PASS: natural capture, coroutine capture and ECS clone use the production runtime factory.')


if __name__ == '__main__':
    main()
