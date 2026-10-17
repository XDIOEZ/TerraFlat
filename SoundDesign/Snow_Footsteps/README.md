# 雪地实录脚步

三个成品替换现有 `player.footstep.snow__01/02/03.wav`，保留 WAV `.meta` 和 Cue GUID。原声来自 qubodup 的 CC0 干雪脚步，来源、下载包及逐文件哈希、处理参数和成品关联在 `sources.json`，可随游戏发布的作者致谢在 `CREDITS.md`。`Sources/info.txt` 是原包授权说明；原始 FLAC 未修改。原说明提到 96 kHz，但实际文件头和发布页均为 44.1 kHz，本流程以文件头为准。

```powershell
python SoundDesign/Snow_Footsteps/prepare_snow.py prepare
python SoundDesign/Snow_Footsteps/prepare_snow.py verify
```

依赖 NumPy、SciPy、SoundFile；脚本也可复用项目忽略目录 `.codex-tmp/audio-python/` 中已有依赖。所有输入均已保留，无需联网下载或 LMMS 渲染即可重建；脚本只处理真实录音，不合成声音。末尾加少量静音，三步长分别为 0.45 / 0.51 / 0.49 秒；共同增益和轻压尖峰后保留自然力度差异，峰值最高 -7 dBFS。

`Preview_Snow.wav` 按顺序播放三变体两轮，仅供单独试听。脚本会同步 `Player_Fishing_SFX_LMMS/manifest.json` 的三个雪条目和总试听；其他正式音效不写入。旧脚步工程的 `build_sfx.py build/finalize` 将雪识别为外部实录成品：保留原有 24 个事件位置，不合成雪、不从旧 LMMS 母带覆盖雪；成品缺失或哈希不一致会停止并提示先重建本组。

本组只做来源、媒体规格和成品完整性检查；游戏内雪层响度、节奏和主观听感由用户验收。
