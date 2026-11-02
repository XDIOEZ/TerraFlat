"""核对正式所有可走地块与脚步 Cue、八个实录 WAV 及 Unity 引用。"""

from __future__ import annotations

import hashlib
import json
import re
import wave
from collections import Counter
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
TILES = PROJECT / "Assets/StreamingAssets/GameConfig/Tiles"
AUDIO = PROJECT / "Assets/Audio"
CUES = PROJECT / "Assets/Resources/Audio/Cues"
CATALOG = (PROJECT / "Assets/Resources/Audio/AudioCatalog.asset").read_text(encoding="utf-8")


# region 正式地块与音频资源覆盖验证
def verify() -> None:
    packages = json.loads((TILES / "tile-manifest.json").read_text(encoding="utf-8"))["packages"]
    tiles = [tile for package in packages if package.get("enabled")
             for tile in json.loads((TILES / package["path"]).read_text(encoding="utf-8"))["tiles"]]
    ids = [tile["id"] for tile in tiles]
    assert len(ids) == len(set(ids)), "正式地块存在重复 ID"
    walkable = [tile for tile in tiles if tile["data"]["parameters"].get("isWalkable")]
    missing = [tile["id"] for tile in walkable if not tile.get("footstepCueId")]
    assert not missing, f"缺少脚步音色的可走地块：{missing}"

    sources = {}
    for folder in (AUDIO / "Generated", AUDIO / "FootstepsCC0"):
        for path in folder.glob("player.footstep.*.wav"):
            meta = Path(str(path) + ".meta").read_text(encoding="utf-8")
            guid = re.search(r"guid: ([a-f0-9]{32})", meta).group(1)
            assert guid not in sources, f"脚步 WAV 重复 GUID：{guid}"
            sources[guid] = path

    for cue_id in sorted({tile["footstepCueId"] for tile in walkable} |
                         {"player.footstep.water", "player.footstep.snow"}):
        cue = CUES / f"{cue_id}.asset"
        assert cue.is_file(), f"缺少正式 Cue：{cue_id}"
        config = cue.read_text(encoding="utf-8")
        assert f"  id: {cue_id}\n" in config, f"Cue ID 不匹配：{cue_id}"
        guid = re.search(r"guid: ([a-f0-9]{32})", Path(str(cue) + ".meta").read_text(encoding="utf-8")).group(1)
        assert f"fileID: 11400000, guid: {guid}, type: 2" in CATALOG, f"Catalog 漏掉 Cue：{cue_id}"
        clip_guids = re.findall(r"fileID: 8300000, guid: ([a-f0-9]{32})", config)
        assert len(clip_guids) == len(set(clip_guids)) == 8, f"脚步缺少八种不同录音：{cue_id}"
        assert "  noRepeatHistory: 3\n" in config and "  maxInstances: 16\n" in config, f"脚步随机或并发配置退化：{cue_id}"
        for clip_guid in clip_guids:
            assert clip_guid in sources, f"Cue 引用了不存在的录音：{cue_id} / {clip_guid}"
            with wave.open(str(sources[clip_guid]), "rb") as recording:
                assert (recording.getframerate(), recording.getnchannels(), recording.getsampwidth()) == (48000, 1, 2), f"脚步录音格式错误：{sources[clip_guid]}"
                assert recording.getnframes() > 0, f"脚步录音为空：{sources[clip_guid]}"

    metal = json.loads((PROJECT / "SoundDesign/Footsteps_CC0/manifest-metal.json").read_text(encoding="utf-8"))
    assert len(metal["clips"]) == 8, "金属脚步生成清单不完整"
    for clip in metal["clips"]:
        actual = PROJECT / clip["file"]
        assert hashlib.sha256(actual.read_bytes()).hexdigest() == clip["sha256"], f"金属录音被意外修改：{actual}"

    counts = Counter(tile["footstepCueId"].removeprefix("player.footstep.") for tile in walkable)
    print(f"地块脚步映射通过：正式 {len(tiles)} 种，允许行走 {len(walkable)} 种；材质分布 {dict(sorted(counts.items()))}；8 类 Cue × 8 声均已注册")


if __name__ == "__main__":
    verify()
# endregion
