# 本地化

快速入口：`rules.lua`；操作：`localization/stable_identity`。

已编写的规则覆盖：正式 ItemTagLocalizationCatalog 的 ASCII稳定ID；FlatWorldLocalizationService 的物品、Tag、UI文本稳定翻译键。

关键流程待补：Addressables/Locale 实际加载、缺失翻译回退、语言切换、MOD翻译覆盖边界、CJK字体/字形与界面排版。快速键规则不表示译文完整或画面合格。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。
