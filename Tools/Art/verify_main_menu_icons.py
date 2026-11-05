"""离线校验菜单/库存 PNG 的导入设置、Prefab身份与点击布局，不连接其他项目。"""
from pathlib import Path
import re
import json
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
IMAGE_GUID = 'fe87c0e1cc204ed48ad3b37840f39efc'


def prefab_blocks(path):
    source = (ROOT / path).read_text(encoding='utf-8')
    entries = re.split(r'(?=^--- !u!)', source, flags=re.M)[1:]
    result = {re.search(r'&(\d+)', b)[1]: b for b in entries}
    assert len(result) == len(entries), 'Duplicate prefab IDs'
    assert set(re.findall(r'\{fileID: (\d+)\}', source)) <= set(result) | {'0'}
    return result


def verify_png_binding(folder, name, blocks):
    path = ROOT / f'Assets/6_Art/UI/{folder}/{name}.png'
    image = Image.open(path)
    assert image.mode == 'RGBA' and image.size == (96, 96), path
    alpha = image.getchannel('A')
    assert alpha.getextrema() == (0, 255) and alpha.getbbox(), path
    meta = path.with_suffix('.png.meta').read_text()
    guid = re.search(r'guid: ([a-f0-9]{32})', meta)[1]
    for setting in ['enableMipMap: 0', 'isReadable: 0', 'filterMode: 1', 'textureType: 8',
                    'spriteMode: 1', 'spriteMeshType: 1', 'alphaIsTransparency: 1']:
        assert setting in meta, (path, setting)
    matches = [b for b in blocks.values() if f'm_Sprite: {{fileID: 21300000, guid: {guid}, type: 3}}' in b]
    assert len(matches) == 1, path
    component = matches[0]
    for setting in [f'guid: {IMAGE_GUID}', 'm_Material: {fileID: 0}', 'm_RaycastTarget: 0', 'm_PreserveAspect: 1', 'm_UseSpriteMesh: 0']:
        assert setting in component, (path, setting)
    go = re.search(r'm_GameObject: \{fileID: (\d+)\}', component)[1]
    rect = next(b for b in blocks.values() if b.startswith('--- !u!224 ') and f'm_GameObject: {{fileID: {go}}}' in b)
    return go, rect


menu = prefab_blocks('Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_MainMenu.prefab')
assert 'ef8b96895a0ec4ba685178a052544251' not in ''.join(menu.values())
for name, button_name in {'continue': '选择存档', 'new-world': '新游戏', 'multiplayer': '联机模式',
                          'mods': 'MOD管理', 'settings': '设置'}.items():
    go, rect = verify_png_binding('MainMenuIcons', name, menu)
    assert 'm_Name: "\\u83DC\\u5355\\u56FE\\u6807"' in menu[go]
    size = 28 if name == 'settings' else 32
    assert f'm_SizeDelta: {{x: {size}, y: {size}}}' in rect
    parent = re.search(r'm_Father: \{fileID: (\d+)\}', rect)[1]
    parent_go = re.search(r'm_GameObject: \{fileID: (\d+)\}', menu[parent])[1]
    caption = re.search(r'  m_Name: (.+)', menu[parent_go])[1]
    assert (json.loads(caption) if caption.startswith('"') else caption) == button_name

bag = prefab_blocks('Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Bag.prefab')
for name in ['all', 'building', 'nature', 'functional', 'tool', 'equipment', 'food', 'material', 'transport']:
    go, rect = verify_png_binding('InventoryCategoryIcons', name, bag)
    assert 'm_Name: CategoryIcon' in bag[go]
    assert 'm_SizeDelta: {x: 22, y: 22}' in rect
    assert 'm_AnchoredPosition: {x: 8, y: 0}' in rect
    tab = next(b for b in bag.values() if f'value: FWUI_Category_{name}\n' in b)
    parent = re.search(r'm_Father: \{fileID: (\d+)\}', rect)[1]
    instance = re.search(r'm_PrefabInstance: \{fileID: (\d+)\}', bag[parent])[1]
    assert tab.startswith(f'--- !u!1001 &{instance}\n')
    child = re.search(r'&([0-9]+)', rect)[1]
    assert f'addedObject: {{fileID: {child}}}' in tab
    assert 'propertyPath: m_margin.x\n      value: 36' in tab
    assert 'propertyPath: m_margin.z\n      value: 6' in tab
print('PASS: 5 menu + 9 inventory PNG Sprite bindings, import settings and category ownership')
