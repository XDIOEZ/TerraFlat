# FlatWorld · ItemWiki 数值调节桥接

独立的 Editor 工作台窗口已经移除；数值调节统一在 ItemWiki 的物品详情页进行。

- BalanceTuningDatabase.cs 保留旧 Unity ScriptableObject 字段布局，历史由 ItemWiki 本地服务第一次读取旧 BalanceTuningDatabase.asset 时迁移。
- BalanceTuningLivePreview.cs 保留物品/装备/食物模块的**运行时原位更新适配**，没有数据扫描或编辑窗口。
- ItemWikiBalanceLiveBridge.cs 低频读取 Library/FlatWorldItemWiki/balance-live/pending 中的成功保存事件，处理已加载实例，再调用现有 GameRes.RequestResourceReload。仅 Editor Play Mode、离线时可修改正在运行的本机物品。
- 联机会话禁止单端调参；源 JSON 仍由网页保存，正式运行态可能存在冷缓存，需要真实游戏体验复核。
- Unity 菜单 **FlatWorld → 物品百科 → 打开 ItemWiki** 打开网页，原有 Wiki 快捷方式仍可使用。

旧 Prefab/食物数值表配置和一次性迁移器已清理；调参历史数据库继续保留，Prefab-only 内容仍通过各自权威 Prefab 编辑器维护。
