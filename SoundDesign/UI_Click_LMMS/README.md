# FlatWorld UI 点击音效试听包

打开 `试听.html` 就能离线试听。页面内嵌所有音频，可以单独听、整套听、连续点击 10 次，也能调节试听音量。

## 三种方向

| 方向 | 声音设计 | 普通点击时长 |
| --- | --- | --- |
| A · 轻木敲击 | 短衰减的正弦泛音，加一点经过低通的噪声颗粒 | 约 46 ms |
| B · 圆润软按键 | 以较低的正弦音为主，少量三角波补质感 | 约 66 ms |
| C · 柔和像素音 | 三角波短音，加少量正弦音，保留轻微像素游戏感 | 约 58 ms |

每套 8 个 WAV：3 个点击变体，以及悬停、确认、返回、打开、关闭。

- `ABC_click_comparison.wav`：先 A、再 B、最后 C，每种连续点 6 次，中间有停顿。
- `preview_A_wood.wav`、`preview_B_soft.wav`、`preview_C_pixel.wav`：每套先听点击变体两轮，再听悬停、确认、返回、打开、关闭。
- `A_wood/`、`B_soft/`、`C_pixel/`：独立游戏音效，48 kHz、单声道、16-bit PCM WAV。
- `FlatWorld_UI_Clicks.mmp`：LMMS 1.2.2 原生可编辑工程，无外部采样和第三方插件依赖。
- `manifest.json`：工程小节与输出文件的对应关系，以及时长和峰值。

## 在 LMMS 中修改

工程使用本机 LMMS 的 TripleOscillator 合成。前三轨分别对应 A/B/C，第四轨给 A 加少量短噪声。每小节一个音效，120 BPM；每种风格占 8 小节，顺序与试听页一致。

打开工程后可以在乐器窗口调整波形、音量、ENV 音量包络、低通滤波，也可以在 Piano Roll 修改音高和确认/返回的短音组合。

保存修改后的工程，使用 LMMS 导出 48 kHz、16-bit WAV 母带，再运行拆分脚本：

```powershell
& 'G:\LMMS\lmms.exe' render '.\SoundDesign\UI_Click_LMMS\FlatWorld_UI_Clicks.mmp' -o '.\.codex-tmp\ui-click-lmms-master.wav' -f wav -s 48000
python '.\SoundDesign\UI_Click_LMMS\build_preview.py' --master '.\.codex-tmp\ui-click-lmms-master.wav'
```

拆分脚本会保留短起音、淡出尾部，并按每种风格的主要点击音统一调整增益；悬停和返回仍保留较轻的相对音量。更新后的 WAV 会重新嵌入试听页。

若要重新生成初始设计，可把当前工程作为模板传给脚本：

```powershell
python '.\SoundDesign\UI_Click_LMMS\build_preview.py' --base '.\SoundDesign\UI_Click_LMMS\FlatWorld_UI_Clicks.mmp'
```

此命令会将三套音色与编排恢复到脚本中的初始参数。

## 当前范围

这是试听包，保存在 Unity `Assets` 之外。没有替换游戏中正在使用的 WAV、AudioCue 或 Catalog，也没有执行 Unity 导入或 Play Mode 验收。最终听感由玩家试听决定。

所有波形由 LMMS 合成，未使用网络音效采样。制作时参考 [LMMS 官方包络文档](https://docs.lmms.io/user-manual/jian-ti-zhong-wen/shu-xi-lmms/yue-qi-chuang-kou) 和 [官方爆音排查说明](https://docs.lmms.io/user-manual/getting-started/troubleshooting)，使用短起音和退音处理硬切断。

后续选定一套时，现有 `ui.click`、`ui.hover`、`ui.confirm`、`ui.cancel` 可对应同名文件。`ui.open` / `ui.close` 是额外候选，正式接入需要增加对应 Cue 和调用处。现有默认音效生成器的“覆盖生成”会重写默认 WAV，正式替换时应同步考虑这个入口。
