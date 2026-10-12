# FlatWorld 玩法音效 LMMS 工程

本目录重做了游戏仍在使用的 30 类旧代码合成音效。素材由本机 LMMS 1.2.2 经 MCP 保存、加载与渲染，使用 TripleOscillator 的噪声、非整数谐波组合、滤波和短包络，全部为本轮原创合成，不使用外部采样。

## 游戏接入

- 29 类一次性音效各三个变体，加一条湿衣服摩擦循环，共 88 个 WAV。
- 成品在 `Assets/Audio/Generated/`；原 `__01` 和 AudioCue 的 GUID 均保留，新增 `__02/__03` 已写入同一 Cue 的 `clips` 数组。
- 世界短音为 48 kHz、PCM 16-bit、单声道，Unity 使用 PCM 预加载并关闭归一化。声音左右方向继续由现有空间 AudioSource 决定。
- `combat.weapon.*.attack` 仍只在动作开始播放；`combat.impact.*` 在确认造成伤害后播放。没有把命中声塞进挥动声。
- 旧 C# 默认音效生成器已删除，防止启动或菜单操作覆盖成品。自动 Catalog 整理工具保留。
- 本轮未操作 Play Mode，游戏内响度、音色与手感由用户验收。正在运行的 Unity 会在允许导入/重新加载时读取新资源和代码。

## 工程与试听顺序

| 工程 | 第一遍试听串 | 顺序 |
| --- | --- | --- |
| `Gameplay_Attacks.mmp` | `Preview_Attacks.wav` | 通用、刀、斧、镐、矛、钝器 |
| `Gameplay_Impacts.mmp` | `Preview_Impacts.wav` | 通用伤害、默认、草叶、木头、石头、金属、肉体、刀割草、刀碰石、斧砍木、镐凿石 |
| `Gameplay_Interactions.mmp` | `Preview_Interactions.wav` | 拿起、放下、锄土、开门、关门、吃、脆食、喝水、悬停、点击、确认、返回 |

试听串只包含每类第一个变体；湿衣服循环直接试听游戏目录中的 WAV。不同重量的挥动通过起音长短、低频厚度与空气层比例区分；命中声通过低频闷响、空腔木响、石屑颗粒、金属短余响和草叶摩擦区分。

## 再制作

1. 修改 `build_sfx.py` 的 `specs()`，执行 `python SoundDesign/Gameplay_SFX_LMMS/build_sfx.py build`。它基于 MCP 创建的 `MCP_Base.mmp` 生成三组原生 LMMS 工程与 `layout.json`。每个时间槽只放一个事件变体；轨道名包含用途、变体和分层编号。
2. 依次通过 LMMS MCP `load_project`、`save_project(compressed=false)`、`render_project(file_format="wav", samplerate=48000)` 导出到：

   ```text
   renders/Gameplay_Attacks_raw.wav
   renders/Gameplay_Impacts_raw.wav
   renders/Gameplay_Interactions_raw.wav
   ```

3. 执行 `python SoundDesign/Gameplay_SFX_LMMS/build_sfx.py finalize`。脚本只裁切真实 LMMS 母带、转单声道、去直流、收尾/循环接缝和调整增益，不自行合成声音。先写完整临时文件再替换 WAV；`.meta` 原位更新保留 GUID；绑定已有 Cue 并生成试听串。
4. `manifest.json` 保存逐条文件、Cue、工程、裁切时间、时长、峰值、RMS、GUID、SHA-256 和循环接缝差。`renders/` 是忽略的临时母带目录，不提交。

LMMS 工程可直接在 LMMS 打开编辑。如果手工移动了片段位置或改变长度，应同步调整 `layout.json` 后再裁切；再次执行 `build` 会按脚本参数覆盖手工工程修改。

## 制作依据

- [LMMS Triple Oscillator 官方手册](https://docs.lmms.io/user-manual/instruments/triple-oscillator)：噪声、混音、包络和滤波。
- [LMMS 1.2.2 Oscillator 枚举](https://github.com/LMMS/lmms/blob/v1.2.2/include/Oscillator.h)：白噪声编号为 6、SignalMix 编号为 2。
- [LMMS 1.2.2 包络实现](https://github.com/LMMS/lmms/blob/v1.2.2/src/core/EnvelopeAndLfoParameters.cpp)：原生 `att/dec/rel` 参数与五秒平方时间换算。
