# 飞船与舱门声音来源

- 飞船舱门使用独立 `ship.door.open`、`ship.door.close`，各 3 个 Kenney CC0 机械门原声变体；普通木门 `door.open/close` 保留原 LMMS 声音。
- `ship.ignition` 使用 `thrusterFire_000.ogg`，`ship.landing` 使用 `impactMetal_000.ogg`；只有成功点火与新的触地事件才响，载入和重复快照静默。太空点火反馈为地表 10%。
- 原声与原始许可保存在 `Sources/Kenney/`，`sources.json` 记录逐文件 SHA256 和对应 Cue；运行 `python SoundDesign/Space_SFX/install_kenney.py` 重建接入。Unity 导入为 48 kHz 单声道 PCM，不额外合成或归一化声音。
- `ship.engine.loop` 复用项目内已经处理并登记的 `Assets/Audio/Generated/fire.burning.loop__01.wav`，通过独立 Cue 的 0.62～0.70 音高和独立增益，将持续燃烧底噪用于引擎喷流。原录音是 Defaultv 的 [Campfire.wav](https://freesound.org/people/Defaultv/sounds/536935/)，许可 [CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/)，无强制署名要求；原始下载、校验值、LMMS 工程与处理步骤沿用 `SoundDesign/Fire_LMMS/sources.json`，该清单记录的许可核验日期是 2026-10-17，本轮没有重新下载音频。
- 引擎声只随真实燃料出力启动和停止；地表起飞与下降保留完整增益，太空持续喷流为同出力地表音量的 10%。这是现有 CC0 录音的音色改编；最终响度、门片遮挡及听感由游戏内验收。
