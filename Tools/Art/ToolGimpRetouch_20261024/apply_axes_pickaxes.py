from pathlib import Path
import json
import hashlib
import os
from gi.repository import Gimp, Gegl, Gio

# region 原生像素计划
# 只按复查过的明确坐标落笔，保留原图层和像素颗粒。
ROOT = Path(__file__).resolve().parents[3]
PLAN_PATH = Path(__file__).with_name('plan-axes-pickaxes.json')
OUT = Path('C:/Users/PC/.codex/visualizations/2026/10/24/01a16c89-036a-77c1-b978-4a2c1a4e7a6a/gimp-production/axes/final')

def export_png(image, output):
    procedure = Gimp.get_pdb().lookup_procedure('file-png-export')
    config = procedure.create_config()
    for key, value in {'run-mode': Gimp.RunMode.NONINTERACTIVE, 'image': image, 'file': Gio.File.new_for_path(str(output)), 'compression': 9, 'include-color-profile': False, 'include-exif': False, 'include-iptc': False, 'include-xmp': False, 'include-thumbnail': False, 'include-comment': False}.items():
        config.set_property(key, value)
    if procedure.run(config).index(0) != Gimp.PDBStatusType.SUCCESS:
        raise RuntimeError('PNG导出失败：' + str(output))
# endregion

# region GIMP 一像素整理和透明边裁切
# 裁切只去透明边，不缩放可见像素；手持锚点在记录中同步换算。
def apply():
    plan = json.loads(PLAN_PATH.read_text(encoding='utf-8'))
    OUT.mkdir(parents=True, exist_ok=True)
    results = []
    Gimp.context_push()
    try:
        Gimp.context_set_brush(Gimp.Brush.get_by_name('1. Pixel'))
        Gimp.context_set_brush_size(1.0)
        Gimp.context_set_opacity(100.0)
        Gimp.context_enable_dynamics(False)
        Gimp.context_set_paint_mode(Gimp.LayerMode.NORMAL)
        for entry in plan['assets']:
            source = ROOT / entry['source']
            if hashlib.sha256(source.read_bytes()).hexdigest() != entry['beforeSha256']:
                raise RuntimeError('原图版本变化：' + entry['id'])
            image = Gimp.file_load(Gimp.RunMode.NONINTERACTIVE, Gio.File.new_for_path(str(source)))
            if image.get_base_type() == Gimp.ImageBaseType.INDEXED:
                image.convert_rgb()
            image.undo_group_start()
            image.get_layers()[0].set_name('Original_32px')
            layer = Gimp.Layer.new(image, 'Connected_Color_Retouch', 32, 32, Gimp.ImageType.RGBA_IMAGE, 100.0, Gimp.LayerMode.NORMAL)
            image.insert_layer(layer, None, 0)
            layer.fill(Gimp.FillType.TRANSPARENT)
            for edit in entry['edits']:
                Gimp.context_set_foreground(Gegl.Color.new(edit['to']))
                x, y = edit['x'] + 0.5, edit['y'] + 0.5
                if not Gimp.pencil(layer, [x, y, x, y]):
                    raise RuntimeError('像素落笔失败：' + entry['id'])
            layer.update(0, 0, 32, 32)
            crop = entry['crop']
            if not image.crop(crop['width'], crop['height'], crop['x'], crop['y']):
                raise RuntimeError('透明边裁切失败：' + entry['id'])
            image.undo_group_end()
            xcf = ROOT / entry['xcf']
            if not Gimp.file_save(Gimp.RunMode.NONINTERACTIVE, image, Gio.File.new_for_path(str(xcf))):
                raise RuntimeError('XCF保存失败：' + entry['id'])
            output = OUT / (entry['id'] + '.png')
            export_png(image, output)
            results.append({'id': entry['id'], 'imageId': image.get_id(), 'output': str(output), 'xcf': str(xcf), 'crop': crop})
    finally:
        Gimp.context_pop()
        Gimp.displays_flush()
    (OUT / 'gimp-results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    print(json.dumps(results))
# endregion

apply()
