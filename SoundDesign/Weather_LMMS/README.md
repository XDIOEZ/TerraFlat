# FlatWorld 天气音效

用本机 LMMS 1.2.2 的 MCP 创建轨道、编排音符与音量自动化，并通过 MCP 离线渲染。声音由 TripleOscillator 白噪声、滤波和包络合成，没有使用外部录音或第三方采样。LMMS 合成方式参考[官方 TripleOscillator 说明](https://docs.lmms.io/user-manual/instruments/triple-oscillator)。

| 游戏事件 | 资源 | 声音结构 |
| --- | --- | --- |
| `weather.rain.loop` | `Assets/Audio/Generated/weather.rain.loop__01.wav` | 31.7 秒；雨幕、中频溅水、位置与间隔不规则的雨滴 |
| `weather.wind.loop` | `Assets/Audio/Generated/weather.wind.loop__01.wav` | 43.7 秒；低频风体、较亮的空气与枝叶层，缓慢变化的阵风 |
| `weather.thunder` | `Assets/Audio/Generated/weather.thunder__01.wav` 至 `__03.wav` | 每条 8.5 秒；短裂声、多段较暗的回响和低沉滚雷 |

三个完整工程 `Weather_Rain.mmp`、`Weather_Wind.mmp`、`Weather_Thunder.mmp` 可以直接在 LMMS 打开编辑；音色参数通过 XML 调整，因为当前 MCP 没有通用振荡器/包络参数写入工具。音符、音量自动化和最终渲染使用 MCP 完成。雷声工程在 0、10、20 秒各放一段，裁切后作为同一 Cue 的随机变体。

重新导出时，通过 MCP `load_project` 加载对应工程，再用 `render_project` 输出 48 kHz WAV 到 `renders/Weather_Rain_raw.wav`、`renders/Weather_Wind_raw.wav`、`renders/Weather_Thunder_raw.wav`。然后在项目根目录运行：

```powershell
python SoundDesign/Weather_LMMS/process_weather.py finalize
```

脚本依赖 numpy，只整理 LMMS 母带：裁切、去直流、雨风等功率交叉接缝、雷声收尾与电平控制。雨声保留原 GUID，其他资源重复整理也保留已有 GUID。最终均为 48 kHz、立体声、PCM 16-bit WAV；具体时长、电平和循环边界数值记录在 `manifest.json`。母带可从工程重新生成，不提交 Git。

游戏接入在 `WeatherMgr.Audio.cs`：雨风按本地强度平滑调音量，雷声随新雷电事件播放，并按距离衰减与延迟。所有天气声使用 Ambient 音量设置；读档不补播旧雷，退出世界或抑制天气的维度清理循环和等待中的雷声。

本轮只做文件、引用与静态编译检查。实际音色、雨风过渡、循环听感、雷声与闪电的配合由玩家在游戏中验收。
