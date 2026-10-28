# 自然环境循环声

两段来自原作者发布页并核实为 CC0 的真实自然录音，成品绑定于 `ambient.foliage.rustle.loop` 和 `ambient.river.flow.loop`。

用 Python 3 与 FFmpeg/FFprobe 重建 WAV、Unity Cue、`.meta` 和简短试听：

```powershell
python SoundDesign/Nature_Ambience/prepare_nature_ambience.py build
python SoundDesign/Nature_Ambience/prepare_nature_ambience.py verify
```

处理过程仅合并立体声、重采样、裁切、一秒等功率循环交叠和静态增益；不合成任何声音。原始 HQ 预览、页面许可证据、哈希与裁切位置记录在 `sources.json`。未自动登记到 `AudioCatalog.asset`，Cue 由项目目录加载后仍需由运行时目录索引引用。
