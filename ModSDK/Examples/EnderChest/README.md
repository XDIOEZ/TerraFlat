# 末影箱：Harmony MOD 示例

![末影箱像素贴图放大预览](Art/EnderChest_Preview_8x.png)

这是一个可以直接编译、放进游戏 MOD 目录的示例。它复用原版木箱的放置流程和库存面板，使用新画的紫色末影箱像素图。同一名角色打开任意末影箱，看到的都是自己的那份库存。另一个角色使用同一箱体时看到自己的库存。箱子拆掉后，角色库存仍在存档里。

示例沿用木箱的 **5 个格子**。想扩展到 27 格，可以另外制作适配 27 格的面板 Prefab，并调整库存模板；不要只把数据格子数改大。

## 文件地图

| 文件 | 作用 |
| --- | --- |
| `manifest.json` | MOD 身份、配方文件和可信 C# 入口 |
| `Defs/recipes.json` | 木板 × 8、石板 × 1 合成末影箱召唤器；按物品总数量写，不写格子位置 |
| `EnderChestContent.cs` | 克隆木箱定义，注册建筑与召唤器两个新物品 ID |
| `Art/draw_ender_chest.py` | 手绘像素矩阵与调色板，可重生成正式贴图 |
| `Assets/6_Art/Generated/Building/EnderChest/EnderChest_Closed.png` | 16×16 正式单帧贴图；编译时会复制进 MOD 包 |
| `ModEntry.cs` | MOD 生命周期、两处 Harmony 补丁和无公共库存的机器逻辑 |
| `EnderVault.cs` | 按稳定角色 ID 缓存库存，并写入 `Data_Player.ItemSpecialData` |
| `EnderChestPanelSession.cs` | 将现有木箱面板绑定到当前角色的私有库存 |
| `EnderChest.csproj` | 引用游戏程序集和游戏自带 Harmony，输出可安装包 |

数据流：`末影箱实体 → 交互面板 → 当前角色的 EnderVault → 原版 Inventory 事务 → 角色存档`。联机时客户端只发送“打开”“搬运”“整理”的意图；服务端验证角色、距离、槽位物品身份，再只给该客户端返回其私有库存。箱体机械快照、公共角色快照和加入世界的公共存档快照都剔除私有物品。

## 编译与安装

1. 先让当前 Unity 项目编译完成，确保 `Library/ScriptAssemblies/GamePlay.dll` 含 `MachineInventoryCommands.RegisterPrivateInventory`。在仓库根目录运行：

   ```powershell
   dotnet build ModSDK/Examples/EnderChest/EnderChest.csproj
   ```

2. 在主菜单的 MOD 管理页点击“打开 MOD 文件夹”，把 `ModSDK/Examples/EnderChest/package` 里的内容放进该目录下的 `EnderChest` 子文件夹，形成 `Mods/EnderChest/manifest.json`。点击“刷新列表”即可看到示例；开启 MOD 后重启游戏加载，无需核对 SHA256 或单独授权。可直接拖拽列表条目调整加载顺序。
3. 在游戏里用 8 块木板和 1 块石板制作“末影箱”并放置。也可以用 GM 工具按 `example.enderchest:chest_Summoner` 查找召唤器。放两个箱子测试共享；换角色测试隔离；保存读档后测试物品仍在。

游戏的桌面 Mono 发行包自带 Harmony。包里有 `Example.EnderChest.dll`、清单、配方和 `Art/EnderChest_Closed.png`，**不要把 `0Harmony.dll` 再打进去**。Unity 项目之外编译时，可通过 `-p:GameManagedDirectory=...` 指向游戏的 `Managed` 目录；若 Unity 安装位置不同，再传 `-p:UnityEngineCorePath=...`。打包工具不会复制游戏或 Unity 的程序集。

## 美术资源

正式贴图是 16×16、底部中心 Pivot、16 PPU、10 色、硬 Alpha 的单帧像素图。运行时从 MOD 包读取 PNG，Point 过滤创建 Sprite；不用改本体木箱图集，也不用额外设置 Addressables。项目里的 PNG `.meta` 同样设置了 Sprite、Point、关闭 Mipmap 和压缩。`Art/draw_ender_chest.py` 保存了可读的逐像素画稿，其他作者可以改调色板或矩阵再执行脚本。

绘制时对照了游戏实际使用的 `Tileset.png[Tileset_77]` 木箱格和 `Scarecrow_Idle.png`、`IronAnvil_World.png` 的建筑像素语言。先用内置 ImageGen 生成一张透明背景的末影箱概念图，再手工压成与木箱同世界尺度的 16×16 正式图；概念图没有进 MOD 包。概念提示词重点为“单个闭合箱体、俯视三分之四视角、深紫材质、青色锁眼、有限色板、硬边像素、透明背景、无投影和文字”。

## 读代码时先看这几个约束

- `ChestId` 和 `SummonerId` 是存档身份；改名会让旧档找不到物品。`VaultKey` 是角色存档中的私有库存键，更新版本时也要保持稳定或写迁移。
- `EnderChestLogic.Inventories` 故意为空。若把私有库存放进这个列表，机器快照就会把内容当成公共库存广播。
- `EnderVault` 对每个角色只创建一个 `Inventory` 对象。相同 `Inventory_Data` 不能同时给多个 `Inventory.InitData()`，否则旧 UI 和数据监听会被清掉。
- `MachineInventoryCommands.RegisterPrivateInventory` 只是通用网络扩展点，服务端最终仍用原版 `Inventory.ExecuteMachineTransfer` 处理拖放，不信任客户端上传的物品数据。
- `ManagedModContext.Track` 在卸载时撤销私有库存注册和内容定义；`ModEntry.Dispose` 只撤销本 MOD 的 Harmony 补丁和角色缓存。
- 这份示例面向 **桌面 Mono**。当前 IL2CPP 构建不会加载可信 C# MOD。

Harmony 的 Prefix 在这里只拦截 `example.enderchest:chest`，其余机器继续执行原方法。可以对照 [Harmony 官方补丁说明](https://harmony.pardeike.net/v2/articles/patching.html) 和 [基本用法](https://harmony.pardeike.net/v2/articles/basics.html) 读 `ModEntry.cs`。
