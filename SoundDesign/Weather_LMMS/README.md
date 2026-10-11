# FlatWorld 天气音效

雨声和风声用 LMMS 1.2.2 的 MCP 创建轨道、编排音符与音量自动化，并通过 MCP 离线渲染，由 TripleOscillator 白噪声、滤波和包络合成。雷声使用真实打雷录音，裁切、滤波、淡出后通过 LMMS MCP 建立采样轨、编排和渲染。近雷保留炸响和不规则回响；远雷来自乡间远处滚雷录音，低通至 1150 Hz。LMMS 合成方式参考[官方 TripleOscillator 说明](https://docs.lmms.io/user-manual/instruments/triple-oscillator)。

| 游戏事件 | 资源 | 声音结构 |
| --- | --- | --- |
| `weather.rain.loop` | `Assets/Audio/Generated/weather.rain.loop__01.wav` | 31.7 秒；雨幕、中频溅水、位置与间隔不规则的雨滴 |
| `weather.wind.loop` | `Assets/Audio/Generated/weather.wind.loop__01.wav` | 43.7 秒；低频风体、较亮的空气与枝叶层，缓慢变化的阵风 |
| `weather.thunder` | `Assets/Audio/Generated/weather.thunder__01.wav` 至 `__03.wav` | 17、17、23 秒单声道；真实近处炸雷与滚雷 |
| `weather.thunder.far` | `Assets/Audio/Generated/weather.thunder.far__01.wav` 至 `__03.wav` | 18.2、23、23 秒单声道；真实远处滚雷 |

四个完整工程 `Weather_Rain.mmp`、`Weather_Wind.mmp`、`Weather_Thunder.mmp`、`Weather_Thunder_Far.mmp` 可以在 LMMS 编辑。雨风音色参数通过 XML 调整，音符、自动化和渲染使用 MCP；近雷工程在 0、24、48 秒各放一段，远雷在 0、28、56 秒各放一段。准备好的采样保存在 `Sources/`；作者、CC0 许可、公共 HQ MP3 预览地址、SHA256、裁切范围和滤波参数见 `sources.json`，来源说明见 `THIRD_PARTY_NOTICES.md`。采样从公共预览解码，不是需要登录下载的原始无损文件。

重新导出雷声时，通过 MCP `load_project` 加载对应工程，再用 `render_project` 输出 48 kHz WAV 到 `renders/Weather_Thunder_raw.wav`、`renders/Weather_Thunder_Far_raw.wav`。然后在项目根目录运行：

```powershell
python SoundDesign/Weather_LMMS/process_weather.py finalize --only-thunder
```

脚本依赖 numpy，只整理 LMMS 母带：裁切、去直流、雨风等功率接缝、雷声收尾与电平控制。`--only-thunder` 只替换雷声，保留雨风资源及其清单记录；所有已有 WAV 保留 GUID。最终均为 48 kHz、PCM 16-bit，雨风立体声、雷声单声道；雷声方向由游戏落点决定。时长和电平记录在 `manifest.json`。母带与预览下载缓存不提交 Git，已裁切采样保留，方便直接重新渲染。

需要重建采样时，安装 numpy、soundfile、scipy 后运行 `prepare_thunder_sources.py`；脚本校验下载 SHA256，再按来源清单处理。项目移到其他目录后，运行 `python SoundDesign/Weather_LMMS/process_weather.py relink` 修复 LMMS 的绝对采样路径，再加载工程。

游戏接入在 `WeatherMgr.Audio.cs`：雨风按本地强度平滑调音量，64 格内选近雷，64–192 格选远雷。雷声位置与雷光共用事件落点，传播延迟为最短环绕距离 / 64 秒；两组 Cue 均完整空间混音，8 格内不衰减，之后线性衰减至 192 格静音。播放期间每帧用 `WorldLocalPresentation.ProjectPosition` 映射落点，再用通用 `AudioService.SetWorldPosition` 更新，避免地图边界造成方位跳错。所有天气声走 Ambient 音量；读档不补播旧雷，退出世界或抑制天气的维度清理循环、等待和活动雷声。

本轮只做文件、引用与静态编译检查。实际音色、雨风过渡、循环听感、雷声与闪电的配合由玩家在游戏中验收。
