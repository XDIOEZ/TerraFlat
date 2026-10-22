# 开放录音脚步

正式来源、许可、原始文件散列与 Cue 对应见 [sources.json](sources.json)，随游戏发布的作者致谢见 [CREDITS.md](CREDITS.md)。Sources 保留实际采用的原始录音或此前已在项目中整理的水花片段；没有使用合成层。

在普通 Python 3 环境安装 `numpy`、`scipy` 和 `soundfile`，从项目根执行：

```powershell
python -m pip install numpy scipy soundfile
python SoundDesign/Footsteps_CC0/prepare.py
```

脚本保留已有 WAV GUID、校验源文件 SHA-256、写入 48 kHz 单声道 PCM16，并更新对应 Cue、Catalog 和两份 manifest。新木板素材位于 `Assets/Audio/FootstepsCC0/`；已有五类脚步原位替换，雪声保持原有录音。旧 `Player_Fishing_SFX_LMMS/build_sfx.py` 将脚步视为外部实录，不再从 LMMS 旧母带覆盖。

来源页已于 2026-10-21 核对 CC0；实际主观听感与游戏内切换由用户验收。

2026-10-22：再运行 `python SoundDesign/Footsteps_CC0/expand_recordings.py`（需 FFmpeg）恢复扩展音库。七类脚步各保留原三声并增加五个实录／轻微变速版本，单类最近三声不重复，并使用小幅随机音高；没有固定八声循环。`player.roll` 的三个变体由 Kenney 的软物撞击和地毯摩擦录音混合，供翻滚发动时播放。扩展的逐文件来源与输出清单在 `sources-expanded.json` / `manifest-expanded.json`；本次仅生成和接入，未执行游戏试听验收。
