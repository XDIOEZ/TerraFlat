---
name: flatworld-audio
description: "FlatWorld 声音素材检索、许可署名、LMMS 制作，以及音频服务、Cue、声源池、空间衰减与音量设置。"
---

# FlatWorld 音频

## 音效来源与制作

- 新增或替换音效、环境声、配乐时，先联网寻找适合的免费开放授权资源，可从 [Freesound](https://freesound.org/) 和 [OpenGameArt](https://opengameart.org/) 入手；结合实际用途、音色、噪声、时长和循环需要选择，合适的现成资源优先使用。
- 逐项核对原作者发布页或资源包的许可，优先采用允许商用、修改和随游戏分发的 **CC0 / CC BY** 素材；其他许可须满足其具体条件，包括 ShareAlike 的要求。仅限非商用、不允许所需改编或授权不清的素材不用于正式资源；“免费下载”“免版税”本身不能代替许可。
- 合适的成品可直接接入，按需裁切、处理循环接缝、整理增益和转换格式；需要进一步编排或混音时再用 LMMS，不要求所有外部音源都重新渲染。
- 找不到效果和许可都合适的资源时，可参考其音色、节奏和层次，再用 **LMMS MCP** 自行创作、编排和渲染，保留可编辑 `.mmp` 工程到 `SoundDesign/` 对应目录。引用采样或改编仍须遵守原许可；不得把未获许可的原音频或可识别旋律混入成品。
- LMMS 制作分支中的外部脚本只负责裁切、循环接缝、增益整理、文件检查和 Unity 接入，不直接合成音效替代 LMMS MCP。

## 来源与署名

- 采用、改编或参考第三方声音时，在 `SoundDesign/<分组>/sources.json` 记录作品标题、原作者、原始页面链接、许可证名称/版本及链接、原始文件与成品文件/Cue 的对应关系、使用方式和修改内容；已有来源清单（如 `grass_sources.json`）沿用即可。保留随包许可、版权声明及作者指定的署名信息。
- 同步整理可直接发布的 `CREDITS.md`：优先沿用项目已有的统一鸣谢文件；没有时放在对应 `SoundDesign/<分组>/`。交付时一并提供，发布时随游戏分发，不能只在来源 JSON 或聊天里留记录。署名包含“作品 — 作者 — 来源 — 许可 — 使用位置 — 修改说明”，并遵从作者的合理署名要求。
- **CC0 素材也按项目约定保留作者致谢**，尊重分享者；经过 LMMS 加工后仍保留原作者和许可证。只作风格参考、未使用原音频的，明确标成“参考学习，未使用原音频”，不得与实际采样/改编混淆，也不暗示原作者为本项目背书。
- 许可与署名细节按素材实际版本核对，常用规则见 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) 和 [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)。

## 入口

- 核心：`Assets/5_Scripts/5-6_Audio/Runtime/` 的 `AudioService`、`AudioCatalog`、`AudioCue`、`AudioRuntimeConfig`、`AudioTypes`。
- 玩法桥接：`Assets/5_Scripts/5-3_GamePlay/Presentation/Audio/Mod_AudioEmitter.cs`、`Entities/Combat/CombatAudioRouter.cs`；UI 桥接：`Assets/5_Scripts/5-5_UI/Audio/`。
- 配置：`Assets/Resources/Audio/`；工具：`Assets/5_Scripts/5-6_Audio/Editor/`。
- 已接入天气音：`Assets/Audio/Generated/weather.*.wav`；LMMS MCP 工程与母带整理脚本在 `SoundDesign/Weather_LMMS/`。雨声使用 `rain_sources.json` 登记的 CC0 连续实录，`prepare_rain_sources.py` 整理采样，`Weather_Rain.mmp` 采样轨经 MCP 重渲染后用 `process_weather.py finalize --only-rain` 单独替换；24 秒立体声循环做 1 秒等功率首尾交叠，预加载 PCM/DecompressOnLoad，避免恢复为零散雨点或实时压缩解码。保留风雷资源，鸣谢随 `StreamingAssets/ThirdPartyNotices/Weather-SFX-CREDITS.md` 发布。
- 玩法音效：`SoundDesign/Gameplay_SFX_LMMS/` 保存攻击、材质命中和交互三组工程；31 个 Cue 绑定 91 个单声道变体，`build_sfx.py build/finalize` 负责编排与母带整理。`combat.weapon.crossbow.loaded` 仅在弩完成上膛时播放。旧 C# 默认音效生成器已删除，避免覆盖 LMMS 成品。
- 湿衣服移动循环 `player.wet_clothes.move.loop` 只保留短衣料摩擦，禁止恢复持续白噪声底层；经 LMMS MCP 重渲染 `Gameplay_Interactions.mmp` 后用 `build_sfx.py finalize --only-cue player.wet_clothes.move.loop` 单独替换，保留其他成品。
- 事务反馈：`SoundDesign/Gameplay_Feedback_LMMS/` 的 6 类 Cue/12 个 LMMS MCP 变体覆盖建造成功/失败、合成、熔炼、火种和点炉；`GameplayFeedbackAudioBridge` 只订阅真实提交与主动拒绝事件，工程通过 `build_feedback.py build → lmms_mcp_render.py --render → finalize/verify` 重做。
- 脚步由 `SoundDesign/Footsteps_CC0/prepare.py` 整理开放授权实录；重建后执行 `expand_recordings.py`（FFmpeg）恢复七类各八个变体与 `player.roll` 三变体，旧 WAV GUID 不变。扩展继续使用 Kenney/TinyWorlds/Fantozzi/Sara Lana 实录及轻微变速，雪兼用原雪声与 Kenney；`AudioCue.noRepeatHistory=3` 避开最近三声，不用固定轮播。来源、manifest、CREDITS 与随包 ThirdPartyNotices 同步；翻滚由 `Mod_Roll.BeginPresentation` 按真实动作首次发动播放。
- `RuntimeTileDefinition.FootstepCueId` 来自地块 JSON `footstepCueId`，木地板/木平台声明 `player.footstep.wood`；计步读取真实支撑地块而非把所有平台当石头，水/雪仍优先。`Mod_Mover.FootstepAudio` 按真实主动位移计步，本地玩家与带 `Mod_Mover` 的生物都会发声；钓鱼三个 Cue/六个变体仍由 `Player_Fishing_SFX_LMMS` 持有，`Mod_FishingRod.SoundRequested` 只在成功抛竿、咬钩、首次收线时发声。
- 游泳：`SoundDesign/Swimming_SFX_LMMS/` 用真实游泳/近录水花，经 LMMS MCP Sample Track 编排渲染入水、划水、出水共 3 类/9 个变体；来源许可和裁切在 `sources.json`，保留 `Sources/` 采样，先 `prepare_sources.py` 再 build/render/finalize，拒绝旧母带。`Mod_Mover.SwimmingAudio` 按本机角色脚下真实 `WaterContact` 液体与主动位移控制；`player.swim.stroke` 为三变体 5.5 秒循环（保留稳定 ID），0.25 秒淡入、0.35 秒淡出，短暂停走保留句柄和播放进度；入出水为单次短音，水格间不重复，浅水沿用涉水脚步。素材用 6 秒实录经 LMMS 渲染、0.5 秒首尾交叠，`finalize --only-stroke` 可保留入出水成品。
- 库存取放：`SoundDesign/Inventory_Items_LMMS/install_runtime_cues.py` 将原有 72 个 LMMS 候选音效接入 `ui.inventory.*` UI Cue；`SoundDesign/Inventory_UI_Feedback_LMMS/` 的新工程经 LMMS MCP 生成交换、装备、卸装共 6 个变体。事件入口为 `InventoryAudioFeedbackEvents/EquipmentAudioFeedbackEvents`，由 `InventoryInteractionAudio` 选择材质、转移重量/体积并过滤非本机音效；重建 Catalog 会保留不在 Generated 内的外部 Cue。
- 候选素材/工程：`Assets/Audio/UI/{LMMS_Clicks,InventoryItems_LMMS}/`、`SoundDesign/{UI_Click_LMMS,Inventory_Items_LMMS}/`。

## 职责与契约

- 飞船专用舱门、点火与触地使用 Kenney `Sci-fi Sounds 1.0` 的 CC0 原声，`SoundDesign/Space_SFX/install_kenney.py` 接入独立 `ship.door.open/close`、`ship.ignition/landing`；来源与哈希、原声及原许可同目录保留，随包署名在 `ThirdPartyNotices/Space-SFX-CREDITS.md`。推进循环仍复用已登记的燃烧实录，转变音与持续音各归正式状态/出力，读档静默。

- 树木受击使用独立 `combat.impact.tree` 三个真实砍木变体，成品在 `Assets/Audio/Trees/`，`SoundDesign/Tree_Chopping/` 保留来源、署名和裁切重建脚本；原生资源与 GO 共用 `health.hurtAudioCueId` / `playHurtAudioOnBlockedHit`，仅正式接受的攻击命中后播放，后者默认关闭、树配置开启后零掉血也响，拒绝命中与环境扣血不触发。
- 篝火燃烧循环在 `SoundDesign/Fire_LMMS/`：CC0 木柴实录经 LMMS MCP 渲染、`finalize.py` 整理成 20 秒单声道循环；`fire.burning.loop` 绑定在 Bonfire 的 `Mod_Furnace.burningLoopCueId`。`MechanicalDepthVisual.Audio` 读取真实燃烧状态控制空间循环，熄灭、禁用与卸载释放句柄；新增设备通过该字段选择 Cue，不在音频服务识别建筑类型。
- 动物待机声由 `Mod_AnimalAmbientAudio` 读取 Actor 的 `animalAudio` 模块配置，`animal.<species>.idle` 走附着动物的单声道空间短音；`AnimalAmbientAudioSchedule` 使用独立随机时钟错峰、拒播后等待完整间隔，不改玩法随机。行为模块通过 `IAnimalAmbientAudioActivity` 提供平静状态资格，死亡、休眠、禁用和卸载停声；声音资源位于 `Assets/Audio/Animals/`，避免 Generated 重建改变空间设置。第三方素材统一登记于 `开发文档文件夹/09_美术素材/第三方素材引用与鸣谢记录.md`，详细采样证据仍留各组来源账。

- 业务通过稳定 Cue ID 发声；`AudioService` 持有跨场景生命周期、声源池、并发优先级、淡入淡出及音量。循环 Cue 的句柄由调用方停止/回收；生成音效 ID 的 `.loop` 后缀表示循环，历史 `player.swim.stroke` 由 Cue 显式标记循环，`player.*` 属于世界 SFX。
- 临时禁声用 `AudioCue.playbackEnabled=false`（默认开启），播放入口在选片、冷却与声源分配前拒播，循环覆盖也不能绕过；停用 Cue 可以保留空录音，正常启用却缺片仍警告并由 Catalog 校验报错。`ui.click` 目前明确停用，Lua `catalog_lifecycle/voice_lifecycle` 分别检查声明校验和真实点击静音。
- 世界音效使用 `At/Attached`、完整空间混音与线性衰减：`MinDistance` 内正常，`MaxDistance` 处静音。`Mod_Cam` 绑定本机玩家和已有监听器，服务将声源投影到监听器 XY 平面，排除相机高度、前探与跟随偏移；解绑按目标身份。范围外短音不占池/冷却，循环音保留句柄待靠近恢复；角色循环音绑定角色 Transform。
- Catalog/Config 的 Resources 路径与加载常量对应。候选 UI 音效按风格分目录，位于自动扫描目录 `Assets/Audio/Generated/` 之外，选定后再绑定 Cue；`build_preview.py --unity .` 重导保留 WAV GUID。
- 正式 GameRes 启动在开放主菜单前等待 AudioService.PreloadAsync，以 Resources.LoadAsync 预热 Config/Catalog/Cue/Clip 强引用图，再创建服务、目录索引与声源池；重复预热复用实例，不在首个点击中同步加载整库。异步请求仍有主线程接入成本，同步入口仅保留给绕过正式启动流程的调用。
- 玩法短音采用 48 kHz 单声道 PCM、预加载、关闭导入归一化；旧 `__01` WAV 和 Cue 保留 GUID，新变体必须写 `.meta` 并加入现有 Cue。采用外部素材时按上述来源与署名流程整理后接入；使用 LMMS 制作时，按工程分组经 LMMS MCP 渲染后再执行 `build_sfx.py finalize`，脚本仅裁切、接缝、增益和绑定，不合成替代 LMMS 声源。临时 WAV 完整写好后原子替换，避免 Unity 导入半个文件。
- 脚步的步频和单次音量倍率集中在 `Mod_Mover.FootstepAudio`；七类脚步 Cue 关闭全局冷却，交由每个角色的真实位移步距控制频率，并将每个 Cue 的并发上限设为 16，避免群体行走时互相吞掉声音。
- 雪地脚步读取脚下真实 `WorldSnowSystem.GetSurfaceDepth`，复用 `SnowDepthLayer` 十档量化：一层音量 0.45/pitch 1.10，十层 1.60/pitch 0.86，超十层仅声音封顶；Lua `audio/footstep_surface` 采样正式选音与两种倍率，不改积雪、水声或其他材质规则。
- 入水、出水、游泳循环和浅水脚步共用 `Mod_Mover.ResolveWaterAudioVolume`：真实脚下 `LiquidDepth` 的 0.1～1 对应 1～10 份水，两端 10%、0.55 处 100%，其间倒抛物线；循环水量倍率平滑更新，出水取离开前最后水格，非水脚步不变。Lua 用 `audio/water_depth_volume` 和 `swimming_sequence` 采样正式规则。
- 涉水脚步与游泳共用 `WaterEnvironmentRules.SwimmingDepthThreshold`（0.3）分界；游泳首次加载和载具、太空、传送、换世界重建时静默，停步或完全淹没不划水，状态由角色持有，不放入共享液体定义。
- `Mod_Mover.FixedUpdate` 对本机脚步与游泳每物理帧只冻结一次世界拓扑及当前位置；两种音效仍保留各自位移基线，入出水与重置逻辑不变，规则 `audio/motion_audio_spatial`。
- 取放音索引按操作、材质、重量 kg、体积 L 选择；重量/体积按实际转移数量独立划档，背包/箱子区分接触音色。拿起在点击取物或开始拖拽时播放一次，放入跟随成功事务；候选索引与运行时 Cue 接入独立。
- 服务直接实现 `ISettingsProvider`，经 `SettingsProviderRegistry` 暴露音量/静音；`FlatWorld.Audio` 只依赖 Data 设置契约。六路滑块使用 `UI_SliderControl`，绑定名为 `MasterVolume/MusicVolume/SfxVolume/UIVolume/AmbientVolume/VoiceVolume` 及对应 `_数值`。
- `AudioService.SetVolumeScale` 调整调用方音量，保留 Cue 随机增益、淡入淡出与用户设置；`SetWorldPosition` 更新非附着空间声源。`WeatherMgr.Audio` 管雨风循环及雷声落点/近远选音/延迟，并通过 `GetLightningDistanceAttenuation` 让雷光共用当前雷声 Cue 的距离与线性衰减，通用服务不识别天气和拓扑；近远雷 Cue 均完整空间混音、线性衰减 8–192 格，使用单声道真实录音，来源见 `SoundDesign/Weather_LMMS/sources.json`。LMMS 雨声不再由默认生成器覆盖。
- 风声以本机玩家位置读取正式混合风场，实际完整风级复用 `WindEnvironmentRules.ResolveWindLevel`：0～7 级为 `weather.wind.loop`，8 级起为 `weather.wind.gale.loop`，两路按 0.8 秒交叉淡化，失效路回收，退出/禁用天气同时停止。`WindAudioState` 独立平滑同一玩家风强，沿用天气过渡时长、超过 `0.5` 才起声、1.5 次方曲线和满风调用方音量 `0.4`，不受相机跨风区影响；Lua `audio/wind_sequence` 采样边界、续接和重置，阈值只影响声音。
- 雨声从本机玩家逻辑位置读取 `TryGetPrecipitationAt`，不跟随镜头雨量；`RainAudioState` 沿用完整降水过渡时长与 0.9 倍音量，缺失/无效样本保留上一目标，真实零雨量才淡出，退出清状态。`audio/rain_sequence` 覆盖缺样续接、途中恢复、停雨和重置；雨风循环共用句柄维护，包络归零后回收。
- 游戏暂停由 `AudioService` 统一跟随 `Time.timeScale<=0`：非 UI 总线保留句柄和采样位置暂停/续播，UI 照常，加减速不改 pitch；暂停时冻结淡入、Stop 淡出仍按真实时间回收，新音立即同步，池回收清掉暂停状态。Lua `audio/voice_lifecycle` 覆盖正式音源跨帧采样。
