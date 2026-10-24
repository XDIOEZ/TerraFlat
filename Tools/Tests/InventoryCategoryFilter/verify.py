"""静态资产检查、真实分类规则测试与外部编译；不刷新或中断 Unity PlayMode。"""
from pathlib import Path
import argparse, json, re, subprocess, uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Temp/InventoryCategoryValidation'

def run(args, name):
    path = OUT / name
    with path.open('w', encoding='utf-8') as log:
        result = subprocess.run(args, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
    text = path.read_text(encoding='utf-8-sig', errors='replace')
    print(text[-12000:])
    if result.returncode: raise SystemExit(result.returncode)

def assets():
    for relative in ['Assets/2_Prefabs/2-1_UI/Common/Controls/UI_MultiSelectDropdown.prefab',
                     'Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Bag.prefab']:
        text = (ROOT / relative).read_text(encoding='utf-8')
        ids = re.findall(r'^--- !u!\d+ &(\d+)', text, re.M)
        assert len(ids) == len(set(ids)), f'Duplicate prefab IDs: {relative}'
        known = set(ids) | {'0'}
        references = re.findall(r'\{fileID: (\d+)\}', text)
        missing = set(references) - known
        assert not missing, (relative, missing)
    print('PASS: prefab local references and unique object IDs')
    blocks = {int(re.search(r'^--- !u!\d+ &(\d+)', part).group(1)): part
              for part in re.split(r'(?=^--- !u!)', text, flags=re.M) if part.startswith('--- !u!')}
    search = next(part for part in blocks.values() if 'guid: 137c03bfb760497f99b7eb56d1f38973' in part)
    search_go = int(re.search(r'  m_GameObject: \{fileID: (\d+)\}', search).group(1))
    search_rect = int(re.search(r'component: \{fileID: (\d+)\}', blocks[search_go]).group(1))
    empty_go = next(part for part in blocks.values() if '  m_Name: FWUI_InventoryFilterEmpty\n' in part)
    empty_rect = int(re.search(r'component: \{fileID: (\d+)\}', empty_go).group(1))
    assert f'  m_Father: {{fileID: {search_rect}}}' in blocks[empty_rect], 'Empty-result label must be a direct child of search owner'
    print('PASS: empty-result label is reachable by the runtime binding')
    source = (ROOT / 'Assets/5_Scripts/5-3_GamePlay/Presentation/UI/InventoryCategoryFilter.cs').read_text(encoding='utf-8')
    labels = re.findall(r'new\("[a-z]+", "([^"]+)"', source)
    labels += ['全部分类', '分类（{0}）', '匹配任一所选分类', '清除分类', '没有匹配物品']
    shared = (ROOT / 'Assets/Localization/StringTables/FlatWorldUI Shared Data.asset').read_text(encoding='utf-8')
    keys = {key: identity for identity, key in re.findall(r'^  - m_Id: (\d+)\n    m_Key: ([^\n]+)', shared, re.M)}
    for label in labels:
        value = 2166136261
        for character in label: value = ((value ^ ord(character)) * 16777619) & 0xffffffff
        key = f'ui.text.{value:08X}'
        assert key in keys, label
        for locale in ['zh-CN', 'en']:
            table = (ROOT / f'Assets/Localization/StringTables/FlatWorldUI_{locale}.asset').read_text(encoding='utf-8')
            assert re.search(r'^  - m_Id: '+keys[key]+r'\n    m_Localized: \S', table, re.M), (label, locale)
    print('PASS: all filter labels exist in both formal localization tables')

def build_targets():
    # 播放期间 Unity 未重新导入的新脚本仅加到外部构建，不改 Unity 生成的项目文件。
    project = ET.Element('Project')
    folders = [('GamePlay', 'Assets/5_Scripts/5-3_GamePlay'), ('UI', 'Assets/5_Scripts/5-5_UI'),
               ('Data', 'Assets/5_Scripts/5-1_Data'),
               ('FlatWorld.WorldModel', 'Assets/5_Scripts/5-0_WorldModel'),
               ('FlatWorld.Automation.Editor', 'Assets/Editor/FlatWorld/Automation'),
               ('Assembly-CSharp-Editor', 'Assets/Editor')]
    for assembly, folder in folders:
        csproj = ROOT / (assembly+'.csproj')
        if not csproj.exists(): continue
        existing = {str((ROOT / node.attrib['Include']).resolve()).lower() for node in ET.parse(csproj).iter()
                    if node.tag.split('}')[-1] == 'Compile' and 'Include' in node.attrib}
        group = ET.SubElement(project, 'ItemGroup', Condition=f"'$(MSBuildProjectName)' == '{assembly}'")
        for path in (ROOT / folder).rglob('*.cs'):
            owner = None
            parent = path.parent
            while parent != ROOT:
                definitions = list(parent.glob('*.asmdef'))
                if definitions:
                    owner = json.loads(definitions[0].read_text(encoding='utf-8-sig'))['name']
                    break
                parent = parent.parent
            if owner is None and '/Editor/' in path.as_posix(): owner = 'Assembly-CSharp-Editor'
            if owner == assembly and str(path.resolve()).lower() not in existing:
                ET.SubElement(group, 'Compile', Include=str(path))
    target = OUT / 'CurrentSources.targets'
    ET.ElementTree(project).write(target, encoding='utf-8', xml_declaration=True)
    return target

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--build', action='store_true')
    options = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    assets()
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(project, 'PropertyGroup')
    for name, value in {'OutputType':'Exe', 'TargetFramework':'net8.0', 'EnableDefaultCompileItems':'false', 'Nullable':'disable'}.items():
        ET.SubElement(props, name).text = value
    group = ET.SubElement(project, 'ItemGroup')
    for file in [Path(__file__).with_name('Program.cs'), ROOT / 'Assets/5_Scripts/5-3_GamePlay/Presentation/UI/InventoryCategoryFilter.cs']:
        ET.SubElement(group, 'Compile', Include=str(file))
    path = OUT / 'InventoryCategoryFilter.Tests.csproj'
    ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
    run(['dotnet','run','--project',str(path),'--configuration','Release','--',str(ROOT)], 'rules.log')
    if options.build:
        target = build_targets()
        for assembly in ['GamePlay','Assembly-CSharp-Editor']:
            run(['dotnet','build',assembly+'.csproj','--ignore-failed-sources','-nologo','-v:q','-clp:ErrorsOnly',
                 f'-p:CustomAfterMicrosoftCommonTargets={target}'],assembly+'.build.log')

if __name__ == '__main__': main()
