# 物品取放 LMMS 音效库

打开 `物品取放音效试听.html`，选择材质、重量、体积，再点击或拖拽物品卡片到背包／木箱上试听。所有音频已嵌入页面，可以离线使用。

## 素材范围

共 72 个音效：3 种操作 × 4 种材质 × 3 档重量 × 2 档体积。

| 维度 | 档位 | 声音设计 |
| --- | --- | --- |
| 操作 | 拿起、放进背包、放进箱子 | 拿起是短触碰与摩擦；背包叠加柔软布料闷响；箱子叠加空腔木质碰撞 |
| 材质 | 柔软、木质、石质、金属 | 分别使用不同噪声比例、正弦泛音组合、非整数共鸣和低通滤波 |
| 重量 | 轻物、中等重量、重物 | 越重音调越低，力量感和衰减略增加 |
| 体积 | 小体积、大体积 | 独立增加摩擦、共鸣时长；大体积落入木箱时有轻微二次落稳声 |

重量与体积独立，大而轻和小而重都有自己的音效。本次箱子按木箱设计，金属箱等目标容器可以继续扩展。

声音为原创 LMMS 合成，未使用外部音效采样。音频时长约 60～149 ms，48 kHz、单声道、16-bit PCM。按材质统一调整增益，保留重量、体积和目标容器之间的相对音量，没有把每个文件归一化成同样响度。

## 位置与命名

- Unity 素材：`Assets/Audio/UI/InventoryItems_LMMS/`，先按操作、再按材质分目录。
- 文件名：`物品交互_材质_操作_重量_体积_01.wav`。
- `WAV/`：LMMS 母带拆出的源 WAV，与 Unity 中的文件内容相同。
- `manifest.json`：完整源文件、Unity 路径、建议 Cue ID、GUID、时长和分类索引。
- Unity 目录中的 `InventoryAudioLibrary.json` 与 `README_物品取放音效说明.md` 提供同样的素材索引与接入参考。

例子：`物品交互_金属硬物_放进箱子_重物_大体积_01.wav`。

## LMMS 工程与批量试听

有 3 个可编辑 LMMS 工程，分别对应拿起、背包、箱子；每个工程 24 个声音。120 BPM，每小节一个声音。每种材质内部的顺序是轻小、轻大、中小、中大、重小、重大。

每个工程有 24 个独立物品音色轨和 6 个接触层轨。可以单独修改每个档位的音量包络、波形、泛音、音高、低通与接触层。

- `试听_拿起物品_四种材质_轻中重对比.wav`
- `试听_放进背包_四种材质_轻中重对比.wav`
- `试听_放进箱子_四种材质_轻中重对比.wav`

上述试听均按柔软 → 木质 → 石质 → 金属排列，每种材质依次轻 → 中 → 重，使用小体积音效。

`试听_同一物品_拿起_放背包_放箱子_对比.wav` 按上述四种材质排列，每种材质依次拿起 → 背包 → 箱子，使用中等重量、大体积音效。

## 修改与重新导出

修改原生 `.mmp` 后，在项目根目录执行：

```powershell
& 'G:\LMMS\lmms.exe' render '.\SoundDesign\Inventory_Items_LMMS\物品交互_拿起物品_24种_LMMS工程.mmp' -o '.\.codex-tmp\inventory_pickup_master.wav' -f wav -s 48000
& 'G:\LMMS\lmms.exe' render '.\SoundDesign\Inventory_Items_LMMS\物品交互_放进背包_24种_LMMS工程.mmp' -o '.\.codex-tmp\inventory_backpack_master.wav' -f wav -s 48000
& 'G:\LMMS\lmms.exe' render '.\SoundDesign\Inventory_Items_LMMS\物品交互_放进箱子_24种_LMMS工程.mmp' -o '.\.codex-tmp\inventory_chest_master.wav' -f wav -s 48000
python '.\SoundDesign\Inventory_Items_LMMS\build_inventory_audio.py' --masters '.\.codex-tmp' --unity
```

脚本会更新 WAV、试听页和 Unity 素材，保留已有 `.meta` 和 GUID。若要恢复初始合成参数，先执行 `python SoundDesign/Inventory_Items_LMMS/build_inventory_audio.py --create` 再导出母带。

脚本复用 `SoundDesign/UI_Click_LMMS/build_preview.py` 的 LMMS 音量包络、WAV 读写和短尾处理。3 个 `.mmp` 本身没有外部采样或第三方插件依赖。

## 后续按游戏数据选音效

目前只完成音效素材、试听与 Unity 入库，没有接入自动播放。索引提供 `action/material/weightBand/volumeBand` 和建议 `ui.inventory.*` Cue ID。

项目 `ItemStack.Weight` 是单件 kg，`ItemStack.Volume` 是单件 L。后续应使用本次实际转移的数量计算重量、体积，不能直接拿整个来源堆叠的总量替代点击一件的数量。材质与容器类型通过稳定标签或配置扩展；轻中重、小大阈值由后续接入配置决定，本次没有修改游戏物品数据。

点击拿起与开始拖拽共用一次拿起反馈；只有库存事务真正提交成功才播放目标容器的放入音。取消、失败、放回原槽不应播放成功放入音，也不能为了发声改动移动、合并、拆分和交换语义。

合成方式参考 [LMMS 官方 TripleOscillator 文档](https://docs.lmms.io/user-manual/instruments/triple-oscillator)，Unity 导入设置参考 [Unity 2022.3 Audio Clip 文档](https://docs.unity3d.com/2022.3/Documentation/Manual/class-AudioClip.html)。
