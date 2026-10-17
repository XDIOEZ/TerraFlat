# 动物自然待机音效

10 类动物共 23 个真实录音片段：鸡/鸟/羊各 3 段，猪/狼/海鸥/蜂/兔/小鱼/雪豹各 2 段。每个变体来自不同的实际时间段或不同录音，没有把同一个波形仅改音高充数。

`animal.snowleopard.idle` 使用真实猎豹低咕噜作临时 Foley 替代，原动物并非雪豹；`animal.smallfish.idle` 是轻水动提示，并非鱼叫。狼使用真实狼群嚎叫，猪使用真实猪哼声。详细作者、原始物种与许可见 [CREDITS.md](CREDITS.md) 和 `sources.json`。

正式 WAV 位于 `Assets/Audio/Animals/`，避免通用 Generated 目录重建覆盖动物的空间参数。全部 48 kHz、单声道、PCM16、预加载、关闭导入归一化。Cue 为 Sfx、单次播放、完整空间混音、线性距离衰减，`minDistance=1.5`、每 Cue 最多 3 声、冷却 0.2 秒；各动物由自身模块错峰随机播放。`maxDistance` 为鸡 10、猪 12、狼 18、鸟 14、海鸥 16、蜂 6、羊 14、兔 10、雪豹 12、小鱼 8。音量较低的蜂/兔/雪豹/鱼仍保留自然差异。

## 重建

1. 普通 Python 运行 `download_sources.py`，依据保存的 URL 和 SHA256 取得授权录音。仓库已保留 `Originals/`，无须重复下载。
2. 普通 Python 运行 `prepare_sources.py`，只裁切、滤波、转换和整理增益。需要 `numpy`、`scipy`、`soundfile`；可使用项目 `.codex-tmp/audio-python`。
3. 在已安装 `lmms_mcp` 的 Python 环境运行 `build_animals.py build`。脚本通过 MCP `create_project/add_sample_track/place_sample_clip/save_project/render_project` 编排并导出母带，不使用外部脚本合成替代声音。LMMS 路径可通过 `LMMS_EXECUTABLE` 配置，默认 `G:\LMMS\lmms.exe`。
4. 普通 Python 运行 `build_animals.py finalize`，从 LMMS 母带取回每个独立片段，生成或更新 WAV、保留已有 GUID、绑定对应 Cue，并只向 Catalog 追加动物 Cue。
5. 普通 Python 运行 `build_animals.py verify` 检查非静音、无削顶、48 kHz 单声道格式、变体独立、GUID、来源哈希、许可证明和 Catalog 连接。

可编辑工程是 `Animal_Idle.mmp`，实际布局在 `layout.json`，母带与试听串在忽略目录 `renders/`。`renders/Preview_Animal_Idle.wav` 按鸡、猪、狼、鸟、海鸥、蜂、羊、兔、小鱼、雪豹顺序串联所有变体，段间 0.35 秒空白。

鸡的原录音为 16 kHz，猎豹论文附录为 11025 Hz，统一重采样不会补出不存在的高频。处理保留原始音高，没有添加混响或烘焙左右声像，实际方向与距离感由游戏世界的空间声源决定。这里仅做离线资产完整性检查，听感与游戏内距离效果由用户自行验收。
