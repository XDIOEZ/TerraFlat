# 游泳：真实水声经 LMMS MCP 编排

三类世界音效各保留 3 个变体，共 9 个 48 kHz、单声道、16-bit PCM WAV。

| Cue | 内容 | 长度 | 峰值上限 | 平均电平上限 |
| --- | --- | --- | --- | --- |
| `player.swim.enter` | 真实近录泳池/湖泊入水水花 | 1.05 秒 | -9 dBFS | -24 dBFS |
| `player.swim.stroke` | 真实游泳中的三次独立划水 | 0.72 秒 | -11 dBFS | -26 dBFS |
| `player.swim.exit` | 真实水花回落、流淌与水滴尾声 | 1.00 秒 | -14 dBFS | -29 dBFS |

当前版本使用真实游泳、泳池近距离入水和湖泊入水录音，替换了不具备水感的 TripleOscillator 白噪声版本。LMMS MCP 创建 9 条 Sample Track、放置采样片段、保存工程并渲染；保留水花的宽频冲击、水流主体和回落细节，不添加合成鼓点或持续噪声层。

源录音、作者、CC0 许可、公开 HQ MP3 预览地址、SHA256 与逐变体裁切范围在 `sources.json`，来源说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。`Sources/` 是保留的 48kHz 单声道采样，原始预览下载缓存放在项目忽略目录。使用的是公共压缩预览，不是需要登录下载的原始无损文件。

## 再制作

```powershell
python SoundDesign/Swimming_SFX_LMMS/prepare_sources.py
python SoundDesign/Swimming_SFX_LMMS/build_swimming.py build
& 'C:\Users\PC\Documents\Codex\2026-10-01\yz\outputs\lmms-mcp\.venv\Scripts\python.exe' SoundDesign/Gameplay_Feedback_LMMS/lmms_mcp_render.py --render --project SoundDesign/Swimming_SFX_LMMS/Swimming.mmp --output SoundDesign/Swimming_SFX_LMMS/renders/Swimming_real_raw.wav
python SoundDesign/Swimming_SFX_LMMS/build_swimming.py finalize
python SoundDesign/Swimming_SFX_LMMS/build_swimming.py verify
```

`Swimming.mmp` 是可编辑 LMMS 采样工程，必须通过真实 LMMS MCP 重新加载、保存和渲染后再整理；采样路径随项目目录改变时，重新执行 `build`。Python 只裁切实录、去直流、保留 60～12000 Hz、整理孤立尖峰的增益、淡入淡出及 Unity 绑定，不合成替代声源。尖峰整理使用 4 毫秒前探和 1 毫秒平滑，防止少数瞬间把整段水流压成听不清的弱声。`finalize` 拒绝早于工程/采样文件的旧母带。

`renders/` 忽略渲染母带，`manifest.json` 保存母带与各采样 SHA256、成品电平和 GUID；`Preview_Swimming_Recorded.wav` 按入水、划水、出水顺序串联首个变体。

三个 Cue 复用浅水脚步的世界 SFX 总线与线性空间衰减：1.5 格内正常，14 格静音；冷却 0.12 秒，划水最多重叠 2 个声音。玩法按真实水体状态切换和主动移动距离触发；浅水继续使用已有涉水脚步声，静止游泳不连续播放划水。

技术检查只确认素材、电平和引用完整，游戏内听感由用户验收。
