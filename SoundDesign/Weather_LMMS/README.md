# FlatWorld 天气音效

雨声和风声用 LMMS 1.2.2 的 MCP 创建轨道、编排音符与音量自动化，并通过 MCP 离线渲染，由 TripleOscillator 白噪声、滤波和包络合成。雷声使用真实打雷录音，裁切、滤波、淡出后通过 LMMS MCP 建立采样轨、编排和渲染。近雷保留炸响和不规则回响；远雷来自乡间远处滚雷录音，低通至 1150 Hz。LMMS 合成方式参考[官方 TripleOscillator 说明](https://docs.lmms.io/user-manual/instruments/triple-oscillator)。

| 游戏事件 | 资源 | 声音结构 |
| --- | --- | --- |
| `weather.rain.loop` | `Assets/Audio/Generated/weather.rain.loop__01.wav` | 31.7 秒；轻薄雨幕、弱化持续溅水底噪，约每秒 3.9 次的不规则雨滴 |
| `weather.wind.loop` | `Assets/Audio/Generated/weather.wind.loop__01.wav` | 43.7 秒；低频风体、较亮的空气与枝叶层，缓慢变化的阵风 |
| `weather.wind.gale.loop` | `Assets/Audio/Generated/weather.wind.gale.loop__01.wav` | 49 秒；CC0 呼啸风实录、低频湍流风体、高处啸声与两组不规则阵风自动化，8 级起切换 |
| `weather.thunder` | `Assets/Audio/Generated/weather.thunder__01.wav` 至 `__03.wav` | 17、17、23 秒单声道；真实近处炸雷与滚雷 |
| `weather.thunder.far` | `Assets/Audio/Generated/weather.thunder.far__01.wav` 至 `__03.wav` | 18.2、23、23 秒单声道；真实远处滚雷 |

四个完整工程 `Weather_Rain.mmp`、`Weather_Wind.mmp`、`Weather_Thunder.mmp`、`Weather_Thunder_Far.mmp` 可以在 LMMS 编辑。雨风音色参数通过 XML 调整，音符、自动化和渲染使用 MCP；近雷工程在 0、24、48 秒各放一段，远雷在 0、28、56 秒各放一段。准备好的采样保存在 `Sources/`；作者、CC0 许可、公共 HQ MP3 预览地址、SHA256、裁切范围和滤波参数见 `sources.json`，来源说明见 `THIRD_PARTY_NOTICES.md`。采样从公共预览解码，不是需要登录下载的原始无损文件。

雨声工程保留 155 个雨滴音符（原为 465 个），保留随机力度和左右声像；雨幕自动化音量缩到原来的 28%，持续溅水轨从 38 降到 8，雨滴轨从 20 调到 26。雨幕低通从 5500 Hz 收到 2200 Hz，雨滴滤波从 3400 Hz 收到 2600 Hz，让雨点更清楚、背景少一些雪花噪声。雨声母带目标 RMS 为 -20 dBFS、峰值上限 -6 dBFS，保留雨点动态，不自动把底噪补回原响度。

重新导出雨声时，通过 MCP `load_project` 加载 `Weather_Rain.mmp`，再用 `render_project` 输出 48 kHz WAV 到 `renders/Weather_Rain_raw.wav`，然后只替换雨声：

```powershell
python SoundDesign/Weather_LMMS/process_weather.py finalize --only-rain
```

重新导出雷声时，通过 MCP `load_project` 加载对应工程，再用 `render_project` 输出 48 kHz WAV 到 `renders/Weather_Thunder_raw.wav`、`renders/Weather_Thunder_Far_raw.wav`。然后在项目根目录运行：

```powershell
python SoundDesign/Weather_LMMS/process_weather.py finalize --only-thunder
```

脚本依赖 numpy，只整理 LMMS 母带：裁切、去直流、雨风等功率接缝、雷声收尾与电平控制。`--only-rain` 只替换雨声，`--only-thunder` 只替换雷声，均保留其余资源及其清单记录；WAV 完整写好后原子替换，所有已有 WAV 保留 GUID。最终均为 48 kHz、PCM 16-bit，雨风立体声、雷声单声道；雷声方向由游戏落点决定。时长和电平记录在 `manifest.json`。母带与预览下载缓存不提交 Git，已裁切采样保留，方便直接重新渲染。

需要重建采样时，安装 numpy、soundfile、scipy 后运行 `prepare_thunder_sources.py`；脚本校验下载 SHA256，再按来源清单处理。项目移到其他目录后，运行 `python SoundDesign/Weather_LMMS/process_weather.py relink` 修复 LMMS 的绝对采样路径，再加载工程。

游戏接入在 `WeatherMgr.Audio.cs`：雨风按本地强度平滑调音量，64 格内选近雷，64–192 格选远雷。雷声位置与雷光共用事件落点，传播延迟为最短环绕距离 / 64 秒；两组 Cue 均完整空间混音，8 格内不衰减，之后线性衰减至 192 格静音。播放期间每帧用 `WorldLocalPresentation.ProjectPosition` 映射落点，再用通用 `AudioService.SetWorldPosition` 更新，避免地图边界造成方位跳错。所有天气声走 Ambient 音量；读档不补播旧雷，退出世界或抑制天气的维度清理循环、等待和活动雷声。

素材生成时只核对文件格式、电平和资源引用；实际音色、雨风过渡、循环听感、雷声与闪电的配合由玩家在游戏中验收。

狂风工程为 `Weather_Wind_Gale.mmp`：LMMS MCP 创建三层音轨、编排 54 秒采样和持续风声、两组音量自动化，再由 LMMS 1.2.2 渲染为 48 kHz WAV。实录来自 juryduty 的 [howling_wind.wav](https://freesound.org/people/juryduty/sounds/185070/)，原页面核对 CC0 1.0；使用公开 HQ MP3 预览，保留裁切后的 `Sources/gale_howling_01.wav`。另两层由 TripleOscillator 白噪声分别经过 260 Hz 低通与 1050 Hz 共振带通，形成低沉持续阵风和较高的风啸。LMMS 中的动态处理和软尖峰处理控制突然的峰值；新增工程使用 LMMS 1.2.2 正式读取的 `fxmixer/fxchannel` 标签，采样轨保留自己的效果链。

重做狂风时先运行 `python SoundDesign/Weather_LMMS/process_gale.py prepare` 重建实录采样（需 numpy、scipy、soundfile，沿用 `.codex-tmp/audio-python` 时会自动载入）；移动项目后运行同脚本 `relink`。通过 MCP `load_project` 加载工程，再调用 `render_project` 输出 `renders/Weather_Wind_Gale_raw.wav`（48 kHz WAV），最后运行 `python SoundDesign/Weather_LMMS/process_gale.py finalize`，只写狂风 WAV、首次导入 meta 与独立 `gale_manifest.json`。脚本取母带 2～52 秒并做 1 秒等功率接缝，得到 49 秒立体声 PCM16；沿用普通风的 -17 dBFS RMS 目标和 -5 dBFS 峰值上限，最终实际 RMS 受峰值上限约束，准确值见清单。现有普通风、雨声和雷声均不覆盖。

狂风来源、下载散列、使用方式和成品/Cue 对应保存在 `gale_sources.json`，总索引为 `sources.json`；可随游戏发布的作者致谢在 `CREDITS.md`。运行时复用正式风级：0～7 级保持普通风，8 级起在 0.8 秒内交叉换为狂风；音量仍沿用现有风声强度曲线。`Generated/weather.wind.gale.loop__01.wav` 遵循 Catalog 自动扫描命名，重新构建 Catalog 会保留其 Ambient 循环、零冷却和单实例设置。
