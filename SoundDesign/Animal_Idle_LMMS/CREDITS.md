# 动物待机音效来源与鸣谢

感谢下列作者分享的真实录音。所有声音都由实录裁切，经 LMMS Sample Track 编排渲染后整理为 48 kHz 单声道 PCM16；修改包括截取、转单声道、重采样、轻度高低通、边缘淡入淡出和增益整理。没有混入音乐、人声模仿或电子合成动物叫声。精确裁切时间与成品对应见 `sources.json`，工程与成品校验见 `manifest.json`。

本文件可随游戏分发，CC0 也按项目约定保留作者致谢。这些署名不表示原作者为游戏背书。

| 作品 | 原作者 | 来源 | 许可 | 使用位置与修改 |
| --- | --- | --- | --- | --- |
| Chicken clucking | Breviceps | [Freesound 456803](https://freesound.org/people/Breviceps/sounds/456803/) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `animal.chicken.idle`，3 个不同咯咯叫片段；裁切、滤波、淡化边缘、单声道重采样与增益整理。原素材 16 kHz。 |
| Pig_Grunt_04.flac | wwstudioswastaken | [Freesound 612686](https://freesound.org/people/wwstudioswastaken/sounds/612686/) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `animal.wildboar.idle__01`，真实猪哼声，作为游戏野猪声音；裁切及统一整理。 |
| Pig_Grunt_07.flac | wwstudioswastaken | [Freesound 620083](https://freesound.org/people/wwstudioswastaken/sounds/620083/) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `animal.wildboar.idle__02`，另一段真实猪哼声；裁切及统一整理。 |
| Wolf howls.ogg | United States Fish and Wildlife Service | [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Wolf_howls.ogg) | [美国联邦政府作品，Public Domain](https://commons.wikimedia.org/wiki/Template:PD-USGov-FWS) | `animal.wolf.idle`，2 个不同的真实狼群嚎叫片段；裁切、滤波、淡化边缘、单声道整理。 |
| Bird, Robin Isolated | JohnsonBrandEditing | [Freesound 434024](https://freesound.org/people/JohnsonBrandEditing/sounds/434024/) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `animal.bird.idle`，3 个不同知更鸟叫声片段；裁切与统一整理。 |
| sea gulls.wav | Snapper4298 | [Freesound 166703](https://freesound.org/people/Snapper4298/sounds/166703/) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `animal.seagull.idle`，2 个不同海鸥叫声片段；使用原实录，未采用作者加延迟的另一个版本。 |
| insect_bee_fly_buzz.wav | bajko | [Freesound 328398](https://freesound.org/people/bajko/sounds/328398/) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `animal.bee.idle`，2 个不同近录蜂鸣片段；裁切与统一整理，不作持续背景循环。 |
| sheep bleat sound | Garuda1982 | [Freesound 428103](https://freesound.org/people/Garuda1982/sounds/428103/) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `animal.sheep.idle`，3 个不同羊叫片段；裁切与统一整理。 |
| Rabbit oinks and squeaks | kessir | [Freesound 372075](https://freesound.org/people/kessir/sounds/372075/) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `animal.rabbit.idle`，2 个不同兔子轻哼片段；保持较低音量，没有采用同作者呼吸困难兔子的录音。 |
| Water Splash | Sara Lana，上传者 felix.blume | [Freesound 434978](https://freesound.org/people/felix.blume/sounds/434978/) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `animal.smallfish.idle`，2 个轻水动片段，作为小鱼附近的克制提示；**这是近录水动，并非鱼的叫声**。 |
| Acoustic Structure and Contextual Use of Calls by Captive Male and Female Cheetahs (Acinonyx jubatus)，S1 Audio | Darya S. Smirnova, Ilya A. Volodin, Tatyana S. Demina, Elena V. Volodina | [PLOS ONE 原论文](https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0158546)，[Commons 音频](https://commons.wikimedia.org/wiki/File:Acoustic-Structure-and-Contextual-Use-of-Calls-by-Captive-Male-and-Female-Cheetahs-%28Acinonyx-pone.0158546.s001.oga) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | `animal.snowleopard.idle`，仅截取开头低咕噜的 2 个不重叠片段；**原动物是猎豹，作为用户认可的雪豹临时猫科 Foley 替代，并非雪豹实录**。裁切、滤波、重采样、淡化边缘与增益整理，无音高变造。原附录 11025 Hz。 |

原始录音保存在 `Originals/`。Freesound 文件使用作者公开的高质量 MP3 预览版本；Commons 使用原始 Ogg 音频。许可发布页快照保存在 `Licenses/`。具体文件、SHA256 与裁切起止时间均保存在 `sources.json`。
