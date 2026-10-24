"""盾牌资源校验、正式纯规则测试及独立编译；不刷新/中断 Unity。"""
from pathlib import Path
import json, re, subprocess, argparse
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT/'Temp/ShieldValidation'

def run(args, name):
    with (OUT/name).open('w',encoding='utf-8') as log:
        result = subprocess.run(args,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT)
    print((OUT/name).read_text('utf-8-sig',errors='replace')[-20000:])
    if result.returncode: raise SystemExit(result.returncode)

def validate_assets():
    item = json.loads((ROOT/'Assets/StreamingAssets/GameConfig/Items/shells/shields.json').read_text('utf-8'))['items'][0]
    assert item['id']=='Shield_Wood' and 'EquipmentSlot.OffHand' in item['tags']
    assert not item['modules']['Mod_EquipmentStore']['parameters']['equipmentInstances'], 'equipping must not add passive defense'
    assert item['modules']['damage']['parameters']['DamageValues']['ImpactKind']==4
    config=item['modules']['shield']['parameters']['Settings']
    assert (config['BashDefense'],config['GuardDefense'],config['ParryDefense'],config['GuardSpeedMultiplier'])==(5,10,10,.5)
    src=(ROOT/'Assets/PlayerInput/PlayerInputActions.inputactions').read_text('utf-8')
    wrapper=(ROOT/'Assets/PlayerInput/PlayerInputActions.cs').read_text('utf-8')
    embedded=re.search(r'InputActionAsset\.FromJson\(@"(.*?)"\);',wrapper,re.S).group(1).replace('""','"')
    assert json.loads(src)==json.loads(embedded), 'generated input JSON differs'
    win=next(m for m in json.loads(src)['maps'] if m['name']=='Win10')
    assert [b['action'] for b in win['bindings'] if b['path'].lower()=='<keyboard>/q']==['Parry']
    entries=(ROOT/'Assets/AddressableAssetsData/AssetGroups/Default.asset').read_text('utf-8')
    for path in [item['visual']['spriteAddress'],'Assets/2_Prefabs/Gameplay/Modules/Combat/Module_Shield.prefab']:
        assert (ROOT/path).exists()
        guid=re.search(r'^guid: (\w+)',(ROOT/(path+'.meta')).read_text('utf-8'),re.M).group(1)
        assert len(guid)==32 and f'm_GUID: {guid}' in entries
    for relative in ['Assets/2_Prefabs/Gameplay/Player/Player.prefab','Assets/2_Prefabs/Gameplay/Modules/Combat/Module_Shield.prefab',
                     'Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Equipment.prefab']:
        text=(ROOT/relative).read_text('utf-8')
        ids=re.findall(r'^--- !u!\d+ &(\d+)',text,re.M)
        assert len(ids)==len(set(ids)), relative
        missing=set(re.findall(r'\{fileID: (\d+)\}',text))-set(ids)-{'0'}
        assert not missing,(relative,missing)
    assert 'm_Name: FWUI_SlotLabel_OffHand' in text, 'formal equipment panel lacks offhand label'
    print('PASS shield asset data, defense values, input source/wrapper, Q binding, prefab references, Addressables')

def targets():
    project=ET.Element('Project')
    existing={}
    for path in ROOT.glob('*.csproj'):
        existing[path.stem]={str((ROOT/node.get('Include')).resolve()).lower() for node in ET.parse(path).iter()
                             if node.tag.split('}')[-1]=='Compile' and node.get('Include')}
        missing=[node.get('Include') for node in ET.parse(path).iter() if node.tag.split('}')[-1]=='Compile'
                 and node.get('Include') and not (ROOT/node.get('Include')).exists()]
        if missing:
            group=ET.SubElement(project,'ItemGroup',Condition=f"'$(MSBuildProjectName)' == '{path.stem}'")
            for source in missing:ET.SubElement(group,'Compile',Remove=source)
    groups={}
    owner_cache={}
    def owner(folder):
        if folder in owner_cache:return owner_cache[folder]
        defs=list(folder.glob('*.asmdef'))
        value=json.loads(defs[0].read_text('utf-8-sig'))['name'] if defs else (owner(folder.parent) if folder!=ROOT else None)
        owner_cache[folder]=value
        return value
    for path in (ROOT/'Assets/5_Scripts').rglob('*.cs'):
        assembly=owner(path.parent)
        if assembly not in existing or str(path.resolve()).lower() in existing[assembly]:continue
        if assembly not in groups:groups[assembly]=ET.SubElement(project,'ItemGroup',Condition=f"'$(MSBuildProjectName)' == '{assembly}'")
        ET.SubElement(groups[assembly],'Compile',Include=str(path))
    result=OUT/'CurrentSources.targets'
    ET.ElementTree(project).write(result,encoding='utf-8',xml_declaration=True)
    return result

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--build',action='store_true');args=parser.parse_args()
    OUT.mkdir(parents=True,exist_ok=True)
    validate_assets()
    run(['dotnet','run','--project',str(Path(__file__).with_name('ShieldActions.Tests.csproj')),'--configuration','Release'],'rules.log')
    if args.build:
        target=targets()
        run(['dotnet','build','FlatWorld.Networking.Gameplay.csproj','--ignore-failed-sources','-nologo','-v:q','-clp:ErrorsOnly',
             f'-p:CustomAfterMicrosoftCommonTargets={target}'],'build.log')

if __name__=='__main__':main()
