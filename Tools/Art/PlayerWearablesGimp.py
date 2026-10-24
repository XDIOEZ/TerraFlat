from pathlib import Path
import json
import tempfile
import time

from gi.repository import Gimp, Gegl, Gio

# region 原图与局部修补记录

# 保留当前原图，只对清单明确列出的原生像素落笔。
PROJECT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT / 'Assets/6_Art/Generated/Characters/PlayerTemplate/Source/EquipmentCompletion/Wearable_Retouches.json'


def replace_complete(source, target):
    for attempt in range(6):
        try:
            source.replace(target)
            return
        except PermissionError:
            if attempt == 5:
                raise
            time.sleep(0.1)


def export_png(image, path):
    procedure = Gimp.get_pdb().lookup_procedure('file-png-export')
    config = procedure.create_config()
    for key, value in {'run-mode': Gimp.RunMode.NONINTERACTIVE, 'image': image,
                       'file': Gio.File.new_for_path(str(path)), 'compression': 9,
                       'include-color-profile': False, 'include-exif': False,
                       'include-iptc': False, 'include-xmp': False,
                       'include-thumbnail': False, 'include-comment': False}.items():
        config.set_property(key, value)
    if procedure.run(config).index(0) != Gimp.PDBStatusType.SUCCESS:
        raise RuntimeError(f'PNG 导出失败：{path}')

# endregion


# region GIMP 一像素修补

def repair():
    manifest = json.loads(SOURCE.read_text(encoding='utf-8'))
    Gimp.context_push()
    try:
        Gimp.context_set_brush(Gimp.Brush.get_by_name('1. Pixel'))
        Gimp.context_set_brush_size(1.0)
        Gimp.context_set_opacity(100.0)
        Gimp.context_enable_dynamics(False)
        Gimp.context_set_paint_mode(Gimp.LayerMode.NORMAL)
        for entry in manifest['assets']:
            if not entry['edits']:
                continue
            original = (PROJECT / entry['wearable']).read_bytes()
            with tempfile.TemporaryDirectory(prefix='wearable-retouch-', dir=PROJECT / 'Temp') as directory:
                directory = Path(directory)
                source_path = directory / 'original.png'
                source_path.write_bytes(original)
                target = PROJECT / entry['wearable']
                image = Gimp.file_load(Gimp.RunMode.NONINTERACTIVE, Gio.File.new_for_path(str(source_path)))
                try:
                    image.get_layers()[0].set_name('Original')
                    layer = Gimp.Layer.new(image, 'OnePixelRetouch', 32, 48, Gimp.ImageType.RGBA_IMAGE, 100.0, Gimp.LayerMode.NORMAL)
                    image.insert_layer(layer, None, 0)
                    layer.fill(Gimp.FillType.TRANSPARENT)
                    for edit in entry['edits']:
                        x, y = edit['x'], edit['y']
                        r, g, b, a = edit['rgba']
                        assert a == 255 and 0 <= x < 32 and 0 <= y < 48
                        Gimp.context_set_foreground(Gegl.Color.new(f'#{r:02x}{g:02x}{b:02x}'))
                        if not Gimp.pencil(layer, [x + 0.5, y + 0.5, x + 0.5, y + 0.5]):
                            raise RuntimeError(f'像素修补失败：{entry["itemId"]}')
                    layer.update(0, 0, 32, 48)
                    output = directory / 'repaired.png'
                    export_png(image, output)
                    replace_complete(output, target)
                    master = directory / 'retouch.xcf'
                    if not Gimp.file_save(Gimp.RunMode.NONINTERACTIVE, image, Gio.File.new_for_path(str(master))):
                        raise RuntimeError('局部修补源稿保存失败')
                    replace_complete(master, PROJECT / entry['master'])
                finally:
                    image.delete()
        print('仅对当前原图执行清单中的单像素修补。')
    finally:
        Gimp.context_pop()

# endregion

repair()
