# 树木砍击实录

`combat.impact.tree` 的三个短音来自 xkeril 的 CC0 真实斧头砍木录音。每次命中随机选一个自然变体，独立于箱子、门等通用木质命中声音。成品放在 `Assets/Audio/Trees/`，避免原有 Generated 音效重建时覆盖。

`Originals/xkeril_753925_hq.mp3` 保留本次实际使用的公开 HQ 预览文件，并非需要登录下载的原始 24-bit WAV。来源、散列、裁切时间与处理参数见 [sources.json](sources.json)，原作者网页的许可核对摘录见 `Licenses/`，可发布署名见 [CREDITS.md](CREDITS.md)。

重建只做裁切、去直流、统一增益、首尾淡化与格式整理，不使用脚本合成音效，不需要 LMMS 加工：

```powershell
python SoundDesign/Tree_Chopping/prepare_tree.py prepare
python SoundDesign/Tree_Chopping/prepare_tree.py verify
```

需要 `numpy` 与 `soundfile`；脚本会优先使用项目已有的 `.codex-tmp/audio-python`。`prepare` 保持来源账中的 Unity GUID，整理三段 0.36/0.37/0.39 秒、48 kHz 单声道 PCM16 WAV，并创建树木专用 Cue、追加 Catalog 引用；统一峰值上限为 -7 dBFS。`manifest.json` 保留成品散列、峰值与平均电平。

空间声参数沿用原有木质命中 Cue：距离 1.5～14、线性衰减、冷却 0.04 秒、同时最多 5 个实例。静态检查只覆盖文件格式、采样电平、GUID 与资源引用，实际声音和听感由游戏内验收确认。
