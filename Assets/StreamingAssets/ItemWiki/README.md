# FlatWorld 物品字典与科技树

这是直接读取当前项目配置的 HTML 工具，无需安装前端依赖或导出一份物品副本。

## 内置数值调节

档案浏览页选择物品后，在基础数值下方直接使用「数值调节」面板。点字段旁边的「↕ 微调」可直接跳转定位，也可以在调参器内搜索/按武器、护甲、食物等分组筛选。

- 按「偏强 · 减小」「偏弱 · 增大」每次小幅调整；连续相同方向逐渐加速，反馈反转时减小步长，不会像二分查找一样最终锁死。
- 每项独立保存步长、方向、历史趋势，支持手动数值输入及撤销最近一次调整。原有字段双击输入数值仍然可用，并计入调参历史。
- 所有数值写入都复用原有 Item JSON 的**指纹冲突检测、配置校验、备份和原子保存**，只覆盖当前子物品对应字段，不修改父级模板。
- 历史记录保存于项目 Library/FlatWorldItemWiki/balance-history.json，第一次使用时自动尝试迁移旧 Unity 工作台 BalanceTuningDatabase.asset 中的 JSON 物品调参记录，**不会修改、删除旧文件**。历史不进入游戏资源与公开分享。
- 编辑器正在单机 Play Mode 时，ItemWikiBalanceLiveBridge 会接收本机成功保存事件，直接更新允许原位更新的已加载 Item，并合并触发已有的 GameRes.RequestResourceReload()；已冷缓存的内容可能需要资源重载或重新生成实例。网页显示 Unity 的实际回执，不把“已保存”当成“已实时生效”。联机期间拒绝单边运行态修改。
- 公开分享继续只读；**Prefab-only 条目没有 JSON 权威定义，不经 ItemWiki 写入**。

Unity 内亦可由菜单「FlatWorld → 物品百科 → 打开 ItemWiki」启动。本地网页是编辑器，游戏运行包不会包含 Python 服务和编辑器桥接脚本。

科技树画布上方的「铺满网页」会隐藏导航、筛选和详情，只保留可拖拽、缩放的画布。按 Esc 或点击右上角「退出铺满」返回，浏览位置和选择会保留。

## 启动

双击本目录的 `打开物品Wiki.cmd`。需要 Python 3，默认打开：

`http://127.0.0.1:8765/Assets/StreamingAssets/ItemWiki/`

启动器会复用同项目、同读写模式的健康服务；没有服务时在后台启动。端口被其他程序占用时会报错，不会结束对方进程。浏览器关闭后后台服务仍可复用。日志位于项目的 `Library/FlatWorldItemWiki/server-8765.out.log` 和 `.err.log`，不应提交。

在项目根目录也可执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Assets/StreamingAssets/ItemWiki/Start-ItemWiki.ps1
```

脚本支持 `-NoBrowser`、`-Port 8767`、`-PublicReadOnly`。也可以直接用 Python 启动：

```powershell
python -B Assets/StreamingAssets/ItemWiki/wiki_server.py --port 8765 --ensure-running
```

`-B` 防止 Python 在 Unity 的 Assets 中生成缓存。需要前台服务并用 Ctrl+C 退出时，去掉 `--ensure-running`。修改 HTML、JS、CSS、JSON 后刷新页面即可；修改 Python 服务代码后需重新启动对应服务。

`公开分享物品Wiki.cmd` 是已有 SakuraFrp 分享入口，使用独立的 8766 只读服务及精确资源白名单。它需要本机 SakuraFrp 配置；本地查看无需启用分享。公共入口只回收本次创建的进程，复用的 Wiki 服务会保留。

## 数据来源

| 内容 | 当前来源 |
| --- | --- |
| 物品和模板 | `../GameConfig/Items/item-manifest.json` 中启用的分包 |
| 生物 | `../GameConfig/Actors/actor-manifest.json` 中启用的分包 |
| 固定制作配方 | `../GameConfig/Recipes/recipe-manifest.json` 中启用的分包 |
| 单料加工 | 每个物品自己的 `processing`，不继承父物品的加工产物 |
| 机器加工与工作站能力 | `Assets/Resources/Config/Mechanical/mechanical-catalog.json` |
| 温变、液体凝固 | 物品 `matter/reactions` 与 `../GameConfig/Liquids/liquids.json` |
| 战利品和 Buff | `../GameConfig/LootTables/loot-tables.json` 与 Buff Manifest |
| 图标 | 当前定义的 Sprite 地址及必要的纹理 `.meta` 切片信息 |

物品继承由 `mechanics.js` 与 Python 写入校验共同对齐游戏加载器：更换模块 Prefab 后清理旧参数，执行 `removeModules`，不继承显示身份、`formerIds` 或 `processing`。模块说明在 `mechanics-rules.js`，名称与字段解释在 `module-glossary.json`。

本地 Item/Buff 编辑仍通过原来的备份、文件指纹和写入校验流程；单纯查看或切换科技树不会写回游戏配置。Actor 及公开服务保持只读。

## 时代和子阶段如何调整

`tech-tree-eras.json` 只管理物品字典的展示分期，不会增加或改变游戏解锁规则。

- `eras`：各大时代的名称、顺序和颜色。`order` 从小到大排列在左到右；导航按实际时代数量排成单行，空间不足时横向滚动。
- `stages`：每个大时代内的细分阶段，使用 `eraId` 关联大时代，`order` 控制先后。
- `assignments`：按稳定 `itemIds` 指定 `stageId`，`reason` 说明分期依据。调整单个物品时，把它从原组移到目标组，避免重复。
- `specialItems`：内部表现变体、调试工具等特殊说明。
- `stationAliases` / `moduleStations`：经过运行时代码确认的工作站别名和模块工作站映射；不凭名称猜测新的生产关系。

新增到 Manifest 的物品会自动进入字典。未明确分期的新物品会留在“待分期”，补 `assignments` 即可。连线从真实配方、加工、温变等数据生成，时代的先后位置本身不代表它们相互依赖。

当前顺序为：原始采集 → 石器 → 铜与青铜 → 铁器 → 机械动力时代 → 电力时代 → 化工时代 → 电子信息时代 → 太空时代。机械传动与机械加工归机械动力，发电储能和电网用电归电力，气液管道、泵送、过滤、电解与氢能按用途归化工；电子气压探针归电子信息，船体、船控、导航和光纤仍归太空。

塑料（`Plastic`）归化工材料，硅板（`SiliconPlate`）归半导体材料，两者先作为物品内容占位。科技树不会因为分期而补造生产配方或前置关系；后续添加正式加工路线后会自动显示连线。

自然采集物品在中下方「自然资源」区按下游用途就近排列，点击导航中的「自然资源」可定位。采矿节点、树木及其直接采集产物自动识别；`stages` 的 `layoutBand: "natural"` 可标记基础自然原料组，无生产路线的其他新物品不会被当成自然资源。资源仍保留原展示分期，筛选和真实配方关系不变。

`tech-tree-model.js` 负责数据关系，`tech-tree.js` 负责浏览和交互；修改显示时应保持两个职责分开。现有游戏机制文档导航仍在 [docs/README.md](docs/README.md)。

## 维护时的最低检查

以下检查仅验证工具的语法和数据契约，不代替浏览体验及游戏玩法验收：

```powershell
node Assets/StreamingAssets/ItemWiki/tests/maintenance.test.js
node --test Assets/StreamingAssets/ItemWiki/tests/balance-tuning.test.js
node --test Assets/StreamingAssets/ItemWiki/tests/tech-tree-model.test.js
python -B -m unittest discover -s Assets/StreamingAssets/ItemWiki/tests -p test_wiki_maintenance.py
python -B -m unittest discover -s Assets/StreamingAssets/ItemWiki/tests -p test_balance_tuning.py
```

增加浏览器脚本或新的数据源时，同时检查 `wiki_server.py` 的公开白名单；公开模式不能因为新页面而开放整个项目目录。
