# 篝火燃烧循环

`Fire_Burning.mmp` 通过 LMMS MCP 编排真实木柴燃烧录音，保留小幅底声起伏和不规则噼啪声。温和版削弱 120 Hz 以下的轰鸣和 4.8 kHz 以上的尖锐声，加强爆点压低，母带 RMS 上限 0.045、峰值上限 0.30，让小篝火更安静温馨。原始素材及 CC0 来源见 `sources.json`。

在项目根目录重渲染（先建立 `.codex-tmp/fire` 目录）：

```powershell
python SoundDesign/Gameplay_Feedback_LMMS/lmms_mcp_render.py --render --project SoundDesign/Fire_LMMS/Fire_Burning.mmp --output .codex-tmp/fire/Fire_Burning_raw.wav
python SoundDesign/Fire_LMMS/finalize.py
```

最终音频为 `Assets/Audio/Generated/fire.burning.loop__01.wav`，20 秒、48 kHz、单声道 PCM16；一秒首尾交叠，循环接缝不淡到静音。工程采样路径使用仓库内的 `Sources/Campfire_Defaultv.wav`；移动仓库后需在 LMMS 中重新指定该采样路径。

正式 Cue 为 `fire.burning.loop`，SFX 总线，1.5 格以内保持音量，14 格处静音。仅 Bonfire 的炉体配置设置 `burningLoopCueId`；设备表现读取真实 `IsFacilityBurning`，0.3 秒淡入淡出，关闭表现、卸载、销毁时释放句柄。其他设备可通过同一字段选择自己的循环 Cue。

本轮 Unity 处于用户 PlayMode，仅静态接入；游戏内点燃、熄灭、添柴、走远再走近、卸载与听感由用户验收。
