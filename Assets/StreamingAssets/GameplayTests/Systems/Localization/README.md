# 本地化

快速入口：`rules.lua`；操作：`localization/stable_identity`。

已编写的规则覆盖：正式 ItemTagLocalizationCatalog 的 ASCII稳定ID；FlatWorldLocalizationService 的物品、Tag、UI文本稳定翻译键。

PlayMode 共用入口：[UISettings/gameplay-suite.json](../UISettings/gameplay-suite.json)，实际脚本 [UISettings/gameplay.lua](../UISettings/gameplay.lua)，案例 ID `ui_localization_gameplay`。本地化与真实 UI 控件高耦合，只保留一套现场实现，系统目录选择映射到这个共用案例。

真实流程覆盖：已入世隔离玩家打开正式设置 Prefab，现有语言下拉驱动正式 Locale Provider，读取 `FlatWorldUI` 中英文真实表并核对正式 TMP Binder 显示、保持节点身份、缺项回退、非法 Locale 拒绝，以及保存后关闭放弃再重开的语言基线。英文表必须存在且和中文不同，不能只看到 fallback 就算加载成功。共享脚本保留实际英文窗口截图；成功、失败和中止均恢复原语言和对应 PlayerPrefs 原值/原有无。

关键流程待补：全部内容/台词译文完整性、MOD 翻译覆盖边界、语言资源加载失败及断网恢复、CJK 字体/字形、翻译后布局、自动本地化器对动态新增控件的覆盖。读取一条正式译文和控件刷新不代表完整语言资源或全部界面画面合格。

本轮仅静态编写，未编译、未运行；实际文字、排版和最终验收由用户执行。
