"""整理开放授权录音并绑定战斗短音；只裁切、重采样、淡化和增益整理。"""
from pathlib import Path
import hashlib
import json
import re
import uuid
import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
RATE = 48000


# region 录音裁切与 Unity 资源绑定
def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = ('\n'.join(line.rstrip() for line in text.splitlines()) + '\n').encode('utf-8')
    if path.exists() and [line.rstrip() for line in path.read_text('utf-8-sig').splitlines()] == payload.decode('utf-8').splitlines():
        return
    temporary = path.with_name('.' + path.name + '.tmp')
    temporary.write_bytes(payload)
    temporary.replace(path)


def guid(path, template=None):
    meta = Path(str(path) + '.meta')
    if meta.exists():
        return re.search(r'^guid: (\w+)', meta.read_text('utf-8'), re.M)[1]
    value = uuid.uuid4().hex
    write(meta, re.sub(r'^guid: \w+', 'guid: ' + value, template, flags=re.M)
          if template else f'fileFormatVersion: 2\nguid: {value}\n')
    return value


def install():
    manifest = json.loads((ROOT / 'sources.json').read_text('utf-8'))
    output = PROJECT / 'Assets/Audio/Combat'
    output.mkdir(parents=True, exist_ok=True)
    guid(output)
    write(Path(str(output) + '.meta'), f'fileFormatVersion: 2\nguid: {guid(output)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
    clip_template = (PROJECT / 'Assets/Audio/Generated/combat.impact.stone__01.wav.meta').read_text('utf-8')
    clip_template = re.sub(r'  userData: .*', '  userData: CC0_Combat_Recordings', clip_template)
    cue_template = (PROJECT / 'Assets/Resources/Audio/Cues/combat.impact.stone.asset').read_text('utf-8')
    catalog_path = PROJECT / 'Assets/Resources/Audio/AudioCatalog.asset'
    catalog = catalog_path.read_text('utf-8')
    cue_clips = {}
    recording_by_hash = {}
    used_recordings = set()
    for cue, segments in manifest['cues'].items():
        clips = []
        for index, segment in enumerate(segments, 1):
            source = ROOT / segment['source']
            values, rate = sf.read(source, always_2d=True)
            values = values.mean(axis=1)
            begin = int(segment.get('start', 0) * rate)
            end = min(len(values), int((segment.get('start', 0) + segment.get('seconds', len(values) / rate)) * rate))
            values = values[begin:end]
            values = np.interp(np.arange(round(len(values) * RATE / rate)) * rate / RATE, np.arange(len(values)), values)
            values -= values.mean()
            # 保留短起音，清掉源文件两端无效空白，不改变播放速度或音高。
            active = np.flatnonzero(np.abs(values) >= max(np.max(np.abs(values)) * .012, .00001))
            values = values[max(0, active[0] - 96):min(len(values), active[-1] + 1440)]
            values *= 10 ** ((-15 if '.weapon.' in cue else -10) / 20) / np.max(np.abs(values))
            fade = np.minimum(1, np.minimum(np.arange(len(values)) / 96, np.arange(len(values))[::-1] / 720))
            destination = output / f'{cue}__{index:02}.wav'
            temporary = destination.with_suffix('.tmp.wav')
            sf.write(temporary, values * fade, RATE, subtype='PCM_16')
            digest = hashlib.sha256(temporary.read_bytes()).hexdigest()
            if digest in recording_by_hash:
                temporary.unlink()
                destination = recording_by_hash[digest]
            else:
                if destination.exists() and destination.read_bytes() == temporary.read_bytes():
                    temporary.unlink()
                else:
                    temporary.replace(destination)
                recording_by_hash[digest] = destination
            used_recordings.add(destination)
            clips.append(guid(destination, clip_template))
            meta = Path(str(destination) + '.meta')
            write(meta, re.sub(r'  userData: .*', '  userData: CC0_Combat_Recordings', meta.read_text('utf-8')))
            segment['sourceSha256'] = hashlib.sha256(source.read_bytes()).hexdigest()
            segment['output'] = destination.relative_to(PROJECT).as_posix()
            segment['outputSha256'] = hashlib.sha256(destination.read_bytes()).hexdigest()
        cue_clips[cue] = clips
    for cue, clips in cue_clips.items():
        path = PROJECT / 'Assets/Resources/Audio/Cues' / (cue + '.asset')
        cue_guid = guid(path)
        write(Path(str(path) + '.meta'), f'fileFormatVersion: 2\nguid: {cue_guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
        content = re.sub(r'combat\.impact\.stone', cue, cue_template)
        content = re.sub(r'  clips:\n(?:  - .*\n)+', '  clips:\n' + ''.join(
            f'  - {{fileID: 8300000, guid: {clip}, type: 3}}\n' for clip in clips), content)
        content = re.sub(r'  cooldown: .*', '  cooldown: 0', content)
        content = re.sub(r'  maxInstances: .*', '  maxInstances: 16', content)
        content = re.sub(r'^  noRepeatHistory: .*\n', '', content, flags=re.M)
        content = content.replace('  selection: 0\n', '  selection: 0\n  noRepeatHistory: 2\n')
        write(path, content)
        if cue_guid not in catalog:
            catalog = catalog.replace('  cues:\n', f'  cues:\n  - {{fileID: 11400000, guid: {cue_guid}, type: 2}}\n')
    # 同源录音共用 GUID，不为十八类矩阵重复导入相同 PCM。
    for candidate in output.glob('combat.*.wav'):
        if re.fullmatch(r'combat\.(blocked|impact)\.(low|medium|high)\.(low|medium|high)__\d+\.wav', candidate.name) and candidate not in used_recordings:
            candidate.unlink()
            Path(str(candidate) + '.meta').unlink(missing_ok=True)
    write(catalog_path, catalog)
    write(ROOT / 'sources.json', json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    print(f'Installed {len(cue_clips)} cues / {sum(len(v) for v in manifest["cues"].values())} recording variants')


if __name__ == '__main__':
    install()
# endregion
