"""Build the appearance catalog and sprite imports; Unity owns prefab reconstruction.

Only sprite import rectangles/outlines are authored; source bitmap pixels are preserved.
"""
from __future__ import annotations
import argparse
import copy
import json
import math
import re
import shutil
import uuid
from pathlib import Path
import yaml
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / 'Assets/Resources/UI/PhysicalDeviceArtworkCatalog.asset'
SCRIPTS = ROOT / 'Assets/5_Scripts/5-3_GamePlay/Presentation/UI'
MOTIONS = ['Still', 'Rotate', 'Reciprocate', 'Compress', 'Stroke', 'Drill', 'Flame', 'Smoke', 'Ember',
           'Liquid', 'Charge', 'Valve', 'Powered', 'Heat', 'Progress', 'CordLeft', 'CordRight',
           'Wheel', 'Gas', 'Fuel', 'SliderCrank', 'ConnectingRod', 'WorkingNeedle',
           'VerticalStroke', 'PumpCordLeft', 'PumpCordRight', 'OpenLift', 'FlowPulse', 'InputAttachment', 'ToolStroke',
           'Contents0', 'Contents1', 'Contents2', 'Contents3']
PANELS = {
    'UI_FireDrill': ('SparkMaker', 0, True), 'UI_HandDrill': ('HandDrill', 0, True),
    'UI_FishingRod': ('FishingRod', 420, False), 'UI_Anvil': ('IronAnvil', 350, False),
    'UI_MakerTable': ('WorkBench', 350, False), 'UI_Furnace': ('BlastFurnace', 420, False),
    'UI_Bonfire': ('Bonfire', 420, False), 'UI_CompostBin': ('CompostBin', 360, False),
    'UI_MeatRack': ('Mod_Meatrack', 380, False), 'UI_Cultivator': ('SpaceCultivator', 380, False),
    'UI_Mechanical': ('HandCrank', 420, False), 'UI_IndustrialFluid': ('GasPump_Mechanical', 420, False),
    'UI_FluidTank': ('PortableGasTank_Iron', 380, False), 'UI_Spacesuit': ('Spacesuit_Torso', 400, False),
    'UI_FuelInteraction': ('Torch_Building', 360, False), 'UI_SteamEngine': ('SteamEngine', 420, False),
    'UI_Minecart': ('Minecart', 420, False), 'UI_SpaceElevator': ('SpaceElevatorTerminal', 380, False),
}

class Dumper(yaml.SafeDumper):
    pass

def represent_dict(dumper, value):
    flow = bool(value) and (set(value) <= {'fileID', 'guid', 'type'} or set(value) <= {'x', 'y', 'z', 'w'} or set(value) <= {'r', 'g', 'b', 'a'})
    return dumper.represent_mapping('tag:yaml.org,2002:map', value, flow_style=flow)

Dumper.add_representer(dict, represent_dict)
Dumper.add_representer(type(None), lambda dumper, value: dumper.represent_scalar('tag:yaml.org,2002:null', ''))

def dump(value):
    return yaml.dump(value, Dumper=Dumper, allow_unicode=True, sort_keys=False, width=160)

def write_existing_text(path,text):
    # 原子替换避免 Unity 导入器读取到一半 YAML，也能更新隐藏的 meta。
    if path.read_text(encoding='utf-8-sig')==text:return
    temporary=ROOT/'Library/FlatWorldDeviceArtwork/AssetWrites'/path.name
    temporary.parent.mkdir(parents=True,exist_ok=True)
    temporary.write_text(text,encoding='utf-8',newline='\n')
    temporary.replace(path)

def meta(path, kind='DefaultImporter'):
    path = Path(path)
    target = Path(str(path) + '.meta')
    if not target.exists():
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n' + kind + ':\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')
    return re.search(r'^guid: (\w+)', target.read_text(encoding='utf-8-sig'), re.M)[1]

def folders(path):
    if path == ROOT / 'Assets' or path == ROOT:
        return
    path.mkdir(parents=True, exist_ok=True)
    folders(path.parent)
    target = Path(str(path) + '.meta')
    if not target.exists():
        target.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n', encoding='utf-8')

def ref(file_id=0, guid=None):
    return {'fileID': file_id, **({'guid': guid, 'type': 3} if guid else {})}

def vec(x=0, y=0, z=None, w=None):
    result = {'x': x, 'y': y}
    if z is not None: result['z'] = z
    if w is not None: result['w'] = w
    return result

def script_meta(name):
    path = SCRIPTS / (name + '.cs')
    return meta(path, 'MonoImporter')

def common():
    return {'m_ObjectHideFlags': 0, 'm_CorrespondingSourceObject': ref(), 'm_PrefabInstance': ref(), 'm_PrefabAsset': ref()}

def sprite(address):
    if not address: return None
    path, _, suffix = address.partition('[')
    source = ROOT / path
    if not source.exists(): raise FileNotFoundError(source)
    data = yaml.safe_load(Path(str(source) + '.meta').read_text(encoding='utf-8-sig'))
    importer = data['TextureImporter']
    sheet = importer.get('spriteSheet', {})
    spec = next((s for s in sheet.get('sprites', []) if s['name'] == suffix.rstrip(']')), None) if suffix else None
    if suffix and spec is None and sheet.get('sprites'): raise ValueError(f'Missing sprite {address}')
    if spec:
        r, pivot = spec['rect'], spec.get('pivot', vec(.5,.5))
        file_id = spec['internalID']
        width, height = r['width'], r['height']
    else:
        width, height = Image.open(source).size
        pivot, file_id = importer.get('spritePivot', vec(.5,.5)), 21300000
    return {'ref': ref(file_id, data['guid']), 'width': width, 'height': height, 'pivot': pivot,
            'ppu': importer.get('spritePixelsToUnits', 100), 'address': address}

def layer(address=None, rect=(-0,0,380,340), motion='Still', tint=(1,1,1,1), **extra):
    s = sprite(address) if address else None
    return {'Sprite': s['ref'] if s else ref(), 'Rect': vec(*rect), 'Pivot': vec(.5,.5),
        'Tint': dict(zip('rgba', tint)), 'Movement': MOTIONS.index(motion), 'Travel': 24,
        'Compression': .15, 'WheelRadius': .18, 'CordAnchor': vec(0,24), 'Rotation': 0,
        'CrankCenter': vec(), 'CrankRadius': 0, 'RodLength': 0, 'SliderAxis': 0, 'SliderBranch': 1, **extra}

def slice_components(image_path, name, columns, rows, names):
    folder = ROOT / ('Assets/6_Art/Generated/Tools/'+name+'UI')
    folders(folder)
    path = folder / (name+'_Components.png')
    if image_path: shutil.copy2(image_path, path)
    if not path.exists(): raise FileNotFoundError('Provide component source for '+name)
    meta_path = Path(str(path)+'.meta')
    guid = meta(path, 'TextureImporter')
    template = yaml.safe_load((ROOT/'Assets/6_Art/Generated/WaterVessel/ClayJarUI/ClayJar_Cutaway.png.meta').read_text(encoding='utf-8-sig'))
    template['guid'] = guid
    previous=yaml.safe_load(meta_path.read_text(encoding='utf-8-sig')).get('TextureImporter',{}).get('spriteSheet',{}).get('sprites',[])
    sprite_ids={s['name']:s['spriteID'] for s in previous}
    imp = template['TextureImporter']
    imp['spriteMode'] = 2
    imp['spritePixelsToUnits'] = 128
    imp['spriteMeshType'] = 0
    imp['maxTextureSize'] = 2048
    imp['mipmaps']['enableMipMap'] = 0
    imp['textureSettings']['filterMode'] = 0
    for platform in imp['platformSettings']: platform['textureCompression'] = 0
    # 精灵切片轮廓只剔除生成稿的外部柔光，原始像素与 Alpha 完整保留。
    import cv2
    import numpy as np
    source = Image.open(path).convert('RGBA')
    cell_w, cell_h = source.width//columns, source.height//rows
    cells=[(i%columns*cell_w,i//columns*cell_h,cell_w,cell_h) for i in range(len(names))]
    if rows==1 and len(names)==2:
        contours,_=cv2.findContours((np.array(source.getchannel('A'))>=200).astype('uint8'),cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
        contours=sorted(contours,key=cv2.contourArea,reverse=True)[:2]
        cells=sorted([cv2.boundingRect(c) for c in contours],key=lambda r:r[0])
    sprites = []
    for i, name in enumerate(names):
        x,top,cell_w,cell_h=cells[i]
        alpha = np.array(source.getchannel('A').crop((x, top, x+cell_w, top+cell_h)))
        threshold = 200 if name != 'Smoke' else 140
        contours, _ = cv2.findContours((alpha>=threshold).astype('uint8'), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        contours = [c for c in contours if cv2.contourArea(c)>40]
        joined=np.concatenate(contours)
        left,upper,bw,bh=cv2.boundingRect(joined)
        outline = [[vec(float(p[0]-left-bw/2), float(upper+bh/2-p[1])) for p in cv2.approxPolyDP(c, .7, True)[:,0]] for c in contours]
        sprites.append({'serializedVersion': 2, 'name': name,
            'rect': {'serializedVersion':2,'x':x+left,'y':source.height-top-upper-bh,'width':bw,'height':bh},
            'alignment':9, 'pivot':vec(.5,.5), 'border':vec(0,0,0,0), 'outline':outline, 'physicsShape':[],
            'tessellationDetail':-1, 'bones':[], 'spriteID':sprite_ids.get(name,uuid.uuid4().hex), 'internalID':21300000+i,
            'vertices':[], 'indices':'', 'edges':[], 'weights':[]})
    imp['spriteSheet']['sprites'] = sprites
    imp['spriteSheet']['nameFileIdTable'] = {s['name']:s['internalID'] for s in sprites}
    imp['internalIDToNameTable'] = [{'first': {213:s['internalID']}, 'second':s['name']} for s in sprites]
    write_existing_text(meta_path,dump(template))
    return str(path.relative_to(ROOT)).replace('\\','/')

def item_definitions():
    items = {}
    for path in (ROOT/'Assets/StreamingAssets/GameConfig/Items').rglob('*.json'):
        for item in json.loads(path.read_text(encoding='utf-8-sig')).get('items',[]): items[item['id']] = item
    def merge(a,b):
        result = copy.deepcopy(a)
        for k,v in b.items():
            result[k] = merge(result[k],v) if isinstance(v,dict) and isinstance(result.get(k),dict) else copy.deepcopy(v)
        return result
    resolved = {}
    def resolve(key):
        if key not in resolved:
            src = items[key]
            resolved[key] = merge(resolve(src['parent']),src) if src.get('parent') in items else src
        return resolved[key]
    return {key:resolve(key) for key in items}

def module_parameters(item):
    for module in item.get('modules',{}).values():
        if module.get('prefab') == 'Module_MechanicalNode':
            data=module.get('data',{})
            raw=data.get('BitData')
            saved=json.loads(raw) if isinstance(raw,str) and raw else data
            return {**saved,**module.get('parameters',{})}
    return {}

def appearances(drill,pump_drill,minecart):
    items = item_definitions()
    nodes = json.loads((ROOT/'Assets/Resources/Config/Mechanical/mechanical-catalog.json').read_text(encoding='utf-8-sig'))['Nodes']
    node_map = {n['Id']:n for n in nodes}
    selected = set(node_map) | {p[0] for p in PANELS.values()} | {'CopperAnvil','PortableGasTank_Steel','SteamEngine_Body'}
    selected -= {'ToolCabinet', 'ShipFloor_Iron','ShipFloor_Refractory','Wire_Hidden'}
    selected |= {key for key,item in items.items() if any(m.get('prefab') in {'Module_HandDrill','Module_ManualProcessor'} for m in item.get('modules',{}).values())}
    appearances=[]
    used=set()
    for key in sorted(selected):
        item=items.get(key)
        if item is None: raise ValueError(f'Unknown artwork item {key}')
        address=item.get('visual',{}).get('spriteAddress')
        if not address: continue
        ids=[key]+[alias for alias in (key+'_Summoner',key+'_Building') if alias in items and alias not in selected]
        ids=[alias for alias in ids if alias not in used]
        used.update(ids)
        body=layer(address)
        layers=[body]
        states=item.get('visual',{}).get('spriteStates',{})
        config=module_parameters(item)
        # 转子沿用世界资源的 PPU 与安装偏移，转换为独立 UI 像素锚点。
        body_sprite=sprite(address)
        fit=min(380/(body_sprite['width']/body_sprite['ppu']),340/(body_sprite['height']/body_sprite['ppu']))
        def world_layer(addr, position, movement, **extra):
            s=sprite(addr)
            return layer(addr,(position[0]*fit,position[1]*fit,s['width']/s['ppu']*fit,s['height']/s['ppu']*fit),movement,
                         Pivot=s['pivot'],**extra)
        body['Rect']=vec(0,0,body_sprite['width']/body_sprite['ppu']*fit,body_sprite['height']/body_sprite['ppu']*fit)
        if 'rotor' in states:
            pos=config.get('RotorLocalPosition',{})
            layers.append(world_layer(states['rotor'],(pos.get('x',0),pos.get('y',0)),'Rotate'))
        for state_name,movement in [('reciprocating','Reciprocate'),('bellowsLeather','Compress'),('gearboxGear','Rotate')]:
            if state_name in states: layers.append(world_layer(states[state_name],(0,0),movement,Travel=26))
        rig=config.get('Articulation')
        if rig:
            layers=[]
            ppu=rig['CanvasPixelsPerUnit']
            canvas_pivot=rig['CanvasPivotPixels']
            crank=rig['CrankCenterPixels']
            cx=(crank['x']-canvas_pivot['x'])/ppu*fit
            cy=(canvas_pivot['y']-crank['y'])/ppu*fit
            for part in sorted(rig['Parts'],key=lambda p:p.get('Order',0)):
                name=part['SpriteState']
                motion={'crank':'Rotate','slider':'SliderCrank','rod':'ConnectingRod','needle':'WorkingNeedle'}.get(part.get('Motion'),'Still')
                pivot=part.get('PivotPixels',canvas_pivot)
                pos=((pivot['x']-canvas_pivot['x'])/ppu,(canvas_pivot['y']-pivot['y'])/ppu)
                piece=world_layer(states[name],pos,motion)
                source_scale=part.get('Scale',1)
                s=sprite(states[name])
                piece['Rect']['z']=s['width']/ppu*fit*source_scale
                piece['Rect']['w']=s['height']/ppu*fit*source_scale
                piece['Rotation']=-part.get('SourceAngleRadians',0)*180/math.pi
                piece.update(CrankCenter=vec(cx,cy),CrankRadius=rig['CrankRadiusPixels']/ppu*fit,
                    RodLength=rig['ConnectingRodLengthPixels']/ppu*fit,
                    SliderAxis=(part.get('SliderAxisPixelsX',crank['x'])-canvas_pivot['x'])/ppu*fit)
                layers.append(piece)
        node=node_map.get(key,{})
        kind=node.get('Kind')
        fluid=node.get('Fluid',{}).get('Kind')
        if kind=='electrical_battery': layers.append(layer(None,(-20,-65,40,150),'Charge',(.3,.8,.38,.85),Pivot=vec(.5,0)))
        if 'Tank' in key or fluid=='tank': layers.append(layer(None,(90,-100,12,180),'Gas' if 'Gas' in key else 'Liquid',(.2,.65,.85,.85),Pivot=vec(.5,0)))
        if key=='ElectricLightBulb' and 'powered' in states: layers.append(layer(states['powered'],motion='Powered'))
        if key=='LiquidPump_Electric':
            layers.extend(layer(None,(-65,-155,12,5),'FlowPulse',(.2,.65,.85,.9),Travel=130) for _ in range(3))
        if fluid=='valve': layers.append(layer(None,(0,70,85,7),'Valve',(.72,.3,.2,1)))
        if key in {'BlastFurnace','Bonfire','Torch_Building','FuelEngine','GasEngine','SteamEngine_Body','SteamEngine'}:
            layers.append(layer(items['FireSeed']['visual']['spriteAddress'],(0,-55,150,110),'Flame'))
            layers.append(layer(drill+'[Smoke]',(0,70,120,120),'Smoke',Travel=100))
            layers.append(layer(None,(140,-90,9,130),'Fuel',(.83,.62,.23,.9),Pivot=vec(.5,0)))
        if key in {'ElectricHeater','InductionStove'}: layers.append(layer(None,(0,-55,100,15),'Heat',(1,.35,.1,.9)))
        if key in {'CompostBin','Mod_Meatrack'}: layers.append(layer(None,(145,-90,8,170),'Progress',(.58,.7,.35,.8),Pivot=vec(.5,0)))
        if key in {'CompostBin','Mod_Meatrack'}:
            for i in range(4):
                addr=items['Meat_Rotten' if key=='CompostBin' else 'Meat']['visual']['spriteAddress']
                layers.append(layer(addr,(-105+i*70,30 if key=='CompostBin' else 45,60,75),'Contents'+str(i)))
        if key in {'IronAnvil','CopperAnvil'}: layers.append(layer(items['IronHammer']['visual']['spriteAddress'],(0,100,140,140),'ToolStroke',Travel=35))
        if key=='WorkBench': layers.append(layer(items['WoodHammer']['visual']['spriteAddress'],(0,95,120,120),'Stroke',Travel=18))
        if key=='SpaceCultivator':
            plant='Assets/6_Art/Generated/Farming/Rice/RiceCrop_Mature.png'
            for i in range(4):
                layers.append(layer(plant,(-105+i*70,-50,65,115),'Contents'+str(i),Pivot=vec(.5,0)))
        if key.startswith('Spacesuit_'):
            body['Rect']=vec(0,-20,210,235)
            layers.append(layer(items['Spacesuit_Head']['visual']['spriteAddress'],(0,130,160,140),'OpenLift',Travel=30))
            layers.append(layer(None,(120,-90,10,160),'Gas',(.24,.7,.8,.9),Pivot=vec(.5,0)))
        if key=='FishingRod':
            layers.append(layer(items['FishingHook_Bone']['visual']['spriteAddress'],(110,-70,60,60),'InputAttachment'))
        if key=='SteamEngine':
            layers.append(layer(None,(0,85,75,6),'Valve',(.75,.4,.2,1)))
            layers.append(layer(None,(145,-70,8,140),'Progress',(.75,.6,.35,.9),Pivot=vec(.5,0)))
        if key=='SpaceElevatorTerminal': layers.append(layer(None,(140,-100,10,200),'Progress',(.3,.7,.8,.8),Pivot=vec(.5,0)))
        if key=='SparkMaker':
            layers=[layer(drill+'[Hearth]',(0,-110,480,250)),
                layer(drill+'[Spindle]',(0,35,80,325),'Drill'),
                layer(None,(-185,48,185,3),'CordLeft',(.78,.67,.47,1)),
                layer(drill+'[Bow]',(0,24,430,200),'Stroke'),
                layer(None,(185,48,185,3),'CordRight',(.78,.67,.47,1)),
                layer(drill+'[Cap]',(0,190,160,110)),
                *[layer(drill+'[Smoke]',(0,-65,105,125),'Smoke',Travel=160) for _ in range(4)],
                layer(drill+'[Ember]',(0,-115,110,85),'Ember')]
        if key=='HandDrill':
            layers=[layer(pump_drill+'[Spindle]',(0,20,90,400),'Drill'),
                layer(pump_drill+'[Flywheel]',(0,-95,230,150),'Drill'),
                layer(None,(-165,55,180,3),'PumpCordLeft',(.78,.67,.47,1),CordAnchor=vec(0,185)),
                layer(pump_drill+'[Crossbar]',(0,55,420,120),'VerticalStroke',Travel=35),
                layer(None,(165,55,180,3),'PumpCordRight',(.78,.67,.47,1),CordAnchor=vec(0,185)),
                layer(pump_drill+'[Cap]',(0,185,60,60))]
        if key=='Minecart':
            layers=[layer(minecart+'[Body]',(0,35,380,230)),
                layer(minecart+'[Wheel]',(-110,-90,105,105),'Wheel'),
                layer(minecart+'[Wheel]',(110,-90,105,105),'Wheel'),
                layer(None,(170,-20,55,5),'Valve',(.7,.7,.65,1))]
        appearances.append({'ItemIds':ids,'Layers':layers})
    # 手持召唤器与落地器具共用同一套可动部件。
    by_id={a['ItemIds'][0]:a for a in appearances}
    for key,appearance in by_id.items():
        base=key.removesuffix('_Summoner')
        if base!=key and base in by_id:
            appearance['Layers']=copy.deepcopy(by_id[base]['Layers'])
    missing=set(p[0] for p in PANELS.values())-used
    if missing: raise ValueError(f'Missing default artwork: {missing}')
    if max(len(a['Layers']) for a in appearances)>16: raise ValueError('Artwork exceeds 16 prebuilt Image layers')
    return appearances

def write_catalog(art):
    folders(CATALOG.parent)
    guid=meta(CATALOG,'NativeFormatImporter')
    catalog_meta=Path(str(CATALOG)+'.meta')
    importer=yaml.safe_load(catalog_meta.read_text(encoding='utf-8-sig'))
    importer['NativeFormatImporter']['mainObjectFileID']=11400000
    write_existing_text(catalog_meta,dump(importer))
    panels=[{'PanelId':key,'DefaultAppearance':value[0],'ArtworkWidth':value[1],'Dedicated':int(value[2])} for key,value in PANELS.items()]
    data={**common(),'m_GameObject':ref(),'m_Enabled':1,'m_EditorHideFlags':0,
        'm_Script':ref(11500000,script_meta('PhysicalDeviceArtworkCatalog')),'m_Name':'PhysicalDeviceArtworkCatalog',
        'm_EditorClassIdentifier':'','Appearances':art,'Panels':panels}
    CATALOG.write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n'+dump({'MonoBehaviour':data}),encoding='utf-8')
    return guid

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--drill-image',type=Path)
    parser.add_argument('--pump-image',type=Path)
    parser.add_argument('--minecart-image',type=Path)
    args=parser.parse_args()
    for name in ['PhysicalDeviceArtworkCatalog','PhysicalDeviceArtworkView','PhysicalDeviceArtworkBindings']: script_meta(name)
    drill=slice_components(args.drill_image,'BowDrill',2,3,['Hearth','Bow','Spindle','Cap','Smoke','Ember'])
    pump_drill=slice_components(args.pump_image,'PumpDrill',2,2,['Spindle','Crossbar','Flywheel','Cap'])
    minecart=slice_components(args.minecart_image,'Minecart',2,1,['Body','Wheel'])
    art=appearances(drill,pump_drill,minecart)
    write_catalog(art)
    print(json.dumps({'appearances':len(art),'panels':len(PANELS)},ensure_ascii=True))

if __name__=='__main__':main()
