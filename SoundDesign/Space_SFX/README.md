# 飞船与舱门声音来源

- `door.open`、`door.close` 复用项目已有 LMMS 原创合成声音，各 3 个变体。工程在 `SoundDesign/Gameplay_SFX_LMMS/Gameplay_Interactions.mmp`，来源说明见该目录 README。没有使用外部采样，也没有新增第三方署名要求。
- `ship.engine.loop` 复用项目内已经处理并登记的 `Assets/Audio/Generated/fire.burning.loop__01.wav`，通过独立 Cue 的 0.62～0.70 音高和独立增益，将持续燃烧底噪用于引擎喷流。原录音是 Defaultv 的 [Campfire.wav](https://freesound.org/people/Defaultv/sounds/536935/)，许可 [CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/)，无强制署名要求；原始下载、校验值、LMMS 工程与处理步骤沿用 `SoundDesign/Fire_LMMS/sources.json`，该清单记录的许可核验日期是 2026-10-17，本轮没有重新下载音频。
- 引擎声只随真实燃料出力启动和停止；地表起飞与下降保留完整增益，太空持续喷流为同出力地表音量的 10%。这是现有 CC0 录音的音色改编；最终响度、门片遮挡及听感由游戏内验收。
