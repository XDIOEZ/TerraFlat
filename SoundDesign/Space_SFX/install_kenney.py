"""将 Kenney CC0 舱门、推进点火和触地录音接入独立飞船 Cue，不改变普通木门。"""
from pathlib import Path
import hashlib
import json
import re
import shutil
import uuid

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]

# region 原始录音接入与来源登记
def write(path, text):
    # Unity 的隐藏 meta 保留属性原地写入，避免 Windows 拒绝截断打开。
    text = '\n'.join(line.rstrip() for line in text.splitlines()) + '\n'
    with path.open('r+b' if path.exists() else 'wb') as stream:
        stream.seek(0)
        stream.write(text.encode('utf-8'))
        stream.truncate()

def install():
    output = PROJECT / 'Assets/Audio/Space'
    output.mkdir(parents=True, exist_ok=True)
    template = (PROJECT / 'Assets/Audio/Generated/door.open__01.wav.meta').read_text('utf-8')
    cue_template = (PROJECT / 'Assets/Resources/Audio/Cues/door.open.asset').read_text('utf-8')
    entries = []
    groups = {'ship.door.open': [f'doorOpen_{i:03}.ogg' for i in range(3)],
              'ship.door.close': [f'doorClose_{i:03}.ogg' for i in range(3)],
              'ship.ignition': ['thrusterFire_000.ogg'], 'ship.landing': ['impactMetal_000.ogg']}
    catalog_path = PROJECT / 'Assets/Resources/Audio/AudioCatalog.asset'
    catalog = catalog_path.read_text('utf-8')
    for cue, files in groups.items():
        clips = []
        for index, filename in enumerate(files, 1):
            source = ROOT / 'Sources/Kenney/Audio' / filename
            target = output / f'{cue}__{index:02}.ogg'
            if not target.exists() or target.read_bytes() != source.read_bytes(): shutil.copyfile(source, target)
            guid = uuid.uuid5(uuid.NAMESPACE_URL, 'flatworld/space-audio/' + target.name).hex
            meta = re.sub(r'guid: [0-9a-f]+', 'guid: ' + guid, template, count=1)
            meta = meta.replace('sampleRateSetting: 0', 'sampleRateSetting: 2').replace('LMMS_MCP_Gameplay', 'Kenney_CC0_SciFi')
            write(Path(str(target) + '.meta'), meta)
            clips.append(f'  - {{fileID: 8300000, guid: {guid}, type: 3}}')
            entries.append({'source': str(source.relative_to(ROOT)), 'sourceSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                            'output': str(target.relative_to(PROJECT)), 'cue': cue, 'modifications': 'Original OGG; Unity imports mono 48 kHz PCM, no normalization.'})
        cue_path = PROJECT / 'Assets/Resources/Audio/Cues' / (cue + '.asset')
        content = cue_template.replace('door.open', cue)
        content = re.sub(r'  clips:\n.*?  selection:', '  clips:\n' + '\n'.join(clips) + '\n  selection:', content, flags=re.S)
        if cue == 'ship.ignition':
            content = content.replace('Min: 1\n    Max: 1', 'Min: 0.65\n    Max: 0.75', 1).replace('maxDistance: 14', 'maxDistance: 40')
        write(cue_path, content)
        cue_guid = uuid.uuid5(uuid.NAMESPACE_URL, 'flatworld/space-cue/' + cue).hex
        write(Path(str(cue_path) + '.meta'), 'fileFormatVersion: 2\nguid: ' + cue_guid + '\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
        line = f'  - {{fileID: 11400000, guid: {cue_guid}, type: 2}}\n'
        if line not in catalog: catalog += line
    write(catalog_path, catalog)
    (ROOT / 'sources.json').write_text(json.dumps({'verifiedOn': '2026-10-22', 'title': 'Sci-fi Sounds 1.0', 'author': 'Kenney',
        'sourcePage': 'https://kenney.nl/assets/sci-fi-sounds', 'mirrorPage': 'https://opengameart.org/content/sci-fi-sounds',
        'downloadUrl': 'https://opengameart.org/sites/default/files/sci-fi_sounds.zip', 'license': 'CC0-1.0',
        'licenseUrl': 'https://creativecommons.org/publicdomain/zero/1.0/', 'entries': entries}, ensure_ascii=False, indent=2) + '\n', 'utf-8')

if __name__ == '__main__': install()
# endregion
