"""将已有 LMMS 物品取放素材注册成 72 个正式的 Unity UI Cue。"""

from __future__ import annotations

import argparse
import json
import re
import uuid
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
INDEX = ROOT / "Assets/Audio/UI/InventoryItems_LMMS/InventoryAudioLibrary.json"
CUE_FOLDER = ROOT / "Assets/Resources/Audio/Cues"
CATALOG = ROOT / "Assets/Resources/Audio/AudioCatalog.asset"


# region LMMS 素材注册
def install():
    entries = json.loads(INDEX.read_text(encoding="utf-8"))["sounds"]
    assert len(entries) == 72
    catalog = CATALOG.read_text(encoding="utf-8")
    seen_ids = set()
    for entry in entries:
        cue_id = entry["cueId"]
        assert cue_id.startswith("ui.inventory.") and cue_id not in seen_ids
        seen_ids.add(cue_id)
        audio = ROOT / entry["unityFile"]
        with wave.open(str(audio), "rb") as reader:
            assert (reader.getnchannels(), reader.getframerate(), reader.getsampwidth()) == (1, 48000, 2), audio
            assert reader.getnframes() > 0
        original_meta = Path(str(audio) + ".meta").read_text(encoding="utf-8")
        assert re.search(r"^guid: ([0-9a-f]{32})$", original_meta, re.M)[1] == entry["guid"], audio

        asset = CUE_FOLDER / (cue_id + ".asset")
        meta = Path(str(asset) + ".meta")
        if meta.exists():
            guid = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(encoding="utf-8"), re.M)[1]
        else:
            guid = uuid.uuid4().hex
            meta.write_text(f"""fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
""", encoding="utf-8")
        # 纯 UI 音效不使用世界空间衰减；高频操作仍受总音量、UI 总线与冷却控制。
        if not asset.exists():
            asset.write_text(f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: c0c19eb3306146649bcc5130f92e0433, type: 3}}
  m_Name: {cue_id}
  m_EditorClassIdentifier:
  id: {cue_id}
  bus: 2
  clips:
  - {{fileID: 8300000, guid: {entry['guid']}, type: 3}}
  selection: 0
  loop: 0
  volume:
    Min: 1
    Max: 1
  pitch:
    Min: 0.98
    Max: 1.02
  spatialBlend: 0
  minDistance: 1
  maxDistance: 20
  priority: 48
  cooldown: 0.03
  maxInstances: 3
  concurrencyPolicy: 1
  rolloffMode: 1
  outputOverride: {{fileID: 0}}
""", encoding="utf-8")
        reference = f"  - {{fileID: 11400000, guid: {guid}, type: 2}}"
        if reference not in catalog:
            catalog = catalog.rstrip() + "\n" + reference + "\n"

    CATALOG.write_text(catalog, encoding="utf-8")
    verify()


def verify():
    entries = json.loads(INDEX.read_text(encoding="utf-8"))["sounds"]
    catalog = CATALOG.read_text(encoding="utf-8")
    seen_guids = set()
    for entry in entries:
        cue_id = entry["cueId"]
        asset = CUE_FOLDER / (cue_id + ".asset")
        text = asset.read_text(encoding="utf-8")
        meta = Path(str(asset) + ".meta").read_text(encoding="utf-8")
        guid = re.search(r"^guid: ([0-9a-f]{32})$", meta, re.M)[1]
        assert guid not in seen_guids and f"guid: {guid}, type: 2" in catalog
        assert f"id: {cue_id}" in text and f"guid: {entry['guid']}" in text
        assert "bus: 2" in text and "spatialBlend: 0" in text and "cooldown: 0.03" in text
        seen_guids.add(guid)
    assert len(seen_guids) == 72
    print("Verified 72 original LMMS WAV references and independent Unity UI Cues")
# endregion


if __name__ == "__main__":
    cli = argparse.ArgumentParser()
    cli.add_argument("action", choices=["install", "verify"])
    args = cli.parse_args()
    {"install": install, "verify": verify}[args.action]()
