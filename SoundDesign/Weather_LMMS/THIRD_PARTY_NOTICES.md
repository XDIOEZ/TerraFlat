# 天气音效素材来源

以下雷声录音在获取时均按 [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) 发布，允许复制、修改和商用。这里保留作者和原页面，便于追溯。没有使用其他候选录音。

| 作者 | 原作品 | 用途 |
| --- | --- | --- |
| bastipictures | [peal of thunder close](https://freesound.org/people/bastipictures/sounds/243778/) | 近雷 01 |
| unfa | [Close Thunder 2](https://freesound.org/people/unfa/sounds/126005/) | 近雷 02 |
| unfa | [Close Thunder 1](https://freesound.org/people/unfa/sounds/124548/) | 近雷 03；作者说明原录音开头略有失真，保留原雷声并低通整理 |
| moxobna | [102610_distant_thunder_01.wav](https://freesound.org/people/moxobna/sounds/107518/) | 三段远雷 |

获取时间：2026-10-11。处理包括公共 HQ MP3 预览解码、裁切、合并为单声道、重采样为 48 kHz、高低通滤波、尾音淡出、电平整理和 LMMS 编排渲染。具体参数、裁切时间和下载散列见 `sources.json`。

普通风声由项目内 LMMS 工程合成，没有使用这批第三方录音。

2026-10-25 雨声替换为 soundrecorder7 的 [Light Rain](https://freesound.org/people/soundrecorder7/sounds/167034/)，原作者页面核对为 [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)。使用公开 HQ MP3 预览，保留立体声，重采样至 48 kHz、80 Hz 高通/5000 Hz 低通后，经 LMMS MCP 采样轨编排渲染，再裁切、做 1 秒首尾等功率交叠和增益整理。来源与散列在 `rain_sources.json`，整理后的采样为 `Sources/rain_light_01.wav`。最终 24 秒循环替换同 GUID 的 `weather.rain.loop__01.wav`，发布署名保存在 `CREDITS.md`，并随 `Assets/StreamingAssets/ThirdPartyNotices/Weather-SFX-CREDITS.md` 分发。

狂风 `weather.wind.gale.loop` 使用 juryduty 的 [howling_wind.wav](https://freesound.org/people/juryduty/sounds/185070/)，原作者发布页在 2026-10-18 核对为 [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)。使用公共 HQ MP3 预览，非需要登录的原始无损下载；裁切 4～58 秒、转为 48 kHz、去直流和增益整理后保存在 `Sources/gale_howling_01.wav`。LMMS MCP 将实录与低频风体、高处啸声两条合成轨及不规则阵风自动化混音渲染，并在 LMMS 内处理动态和尖峰；母带脚本只裁切、接缝和整理增益。完整来源、下载散列、成品/Cue 对应及修改说明见 `gale_sources.json`；发布用署名见 `CREDITS.md`。
