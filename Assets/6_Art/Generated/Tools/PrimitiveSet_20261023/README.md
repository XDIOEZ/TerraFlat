# FlatWorld 原始手作工具重绘

日期：2026-10-23。**35 款新图已完成，并已替换物品、对应放置态及钓鱼界面的美术引用；游戏内观感待用户确认。**

本套包含 35 款成品工具。双击 [Gallery.html](Gallery.html) 可离线浏览：支持分类筛选、深浅背景和三档显示大小；点击图片可打开原图。浏览页只改变显示尺寸，不修改 PNG 像素，无需网络或启动游戏。

## 接入说明

- 35 款成品工具共更新 38 个具体物品/放置态定义，包含钻木取火工具、火把、手钻的便携与放置形态；另同步火把公共基类的表现锚点。
- 新图登记到独立的 `PrimitiveTools` Addressables 组，具有 `ItemSprite` / `Sprite` 标签；旧图保留，现用物品改为新地址。
- 导入使用 Point、关闭 Mipmap、无压缩，普通工具最大导入尺寸 128，鱼竿为 256 以保留细线；原始 PNG 保留。源 PPU 按原图可见世界尺寸换算，握柄 Pivot 与手持朝向同步标定，详细数据见 [ImportManifest.json](ImportManifest.json)。
- 火把新图不烘焙火焰，保留运行时燃烧；斜向原图通过 Renderer 旋转竖起，并同步燃烧和灯光位置。钓鱼面板的直接 Sprite 引用及编辑器构建器也已改为新鱼竿。
- 没有切换或暂停 Unity PlayMode，没有运行玩法测试；本轮为素材和静态引用交付，资源导入/重载后的实际握持、动作和燃烧观感由用户确认。

## 分类数量

| 分类 | 数量 |
| --- | ---: |
| 斧 | 6 |
| 镐 | 5 |
| 铲 | 6 |
| 锄 | 4 |
| 小刀 | 5 |
| 锤 | 2 |
| 采集与钓鱼 | 3 |
| 生火与照明 | 3 |
| 钻孔 | 1 |
| **合计** | **35** |

## 美术约定

- 低饱和、偏写实的复古像素风，以[已认可的铁铲](../StyleSamples_20261023/Shovel_Iron_StyleSample.png)作为像素颗粒锚点。大像素簇、少量色阶、左上光源，不用密集木纹、细碎高光或噪点增加细节。
- 世界背景是从零发展科技。早期复合工具保留不规则木柄、粗削或手锻结构，工具头与柄的连接处用清楚可读的粗纤维绳、皮条或绑带实际固定，并以宽缠绕和大结扣表现手工装配。
- 材质随物品身份保留石、燧石、骨、铜、青铜与铁的区别，不统一降成石器。单片打制石器没有拼装关系，不强加木柄或绳；剪刀的皮条不锁死运动结构。
- 每件独立生成，完整轮廓、透明背景，原图直接保存。大画布不代表高逻辑像素密度；实际 PNG 尺寸、参考图和完整实际提示词以生成记录为准。

## 35 款图片

| 分类 | 中文名 | ID | PNG |
| --- | --- | --- | --- |
| 斧 | 石斧 | `Axe_Stone` | [查看图片](Axe_Stone/Axe_Stone_Icon.png) |
| 斧 | 燧石斧 | `Axe_Flint` | [查看图片](Axe_Flint/Axe_Flint_Icon.png) |
| 斧 | 铜斧 | `Axe_Copper` | [查看图片](Axe_Copper/Axe_Copper_Icon.png) |
| 斧 | 青铜斧 | `Axe_Bronze` | [查看图片](Axe_Bronze/Axe_Bronze_Icon.png) |
| 斧 | 生铁斧 | `Axe_RawIron` | [查看图片](Axe_RawIron/Axe_RawIron_Icon.png) |
| 斧 | 铁斧 | `Axe_Iron` | [查看图片](Axe_Iron/Axe_Iron_Icon.png) |
| 镐 | 石镐 | `Pickaxe_Stone` | [查看图片](Pickaxe_Stone/Pickaxe_Stone_Icon.png) |
| 镐 | 铜镐 | `Pickaxe_Copper` | [查看图片](Pickaxe_Copper/Pickaxe_Copper_Icon.png) |
| 镐 | 粗铁铁镐 | `Pickaxe_RawIron` | [查看图片](Pickaxe_RawIron/Pickaxe_RawIron_Icon.png) |
| 镐 | 青铜镐 | `Pickaxe_Bronze` | [查看图片](Pickaxe_Bronze/Pickaxe_Bronze_Icon.png) |
| 镐 | 熟铁铁镐 | `Pickaxe_Iron` | [查看图片](Pickaxe_Iron/Pickaxe_Iron_Icon.png) |
| 铲 | 木铲 | `Shovel_Wood` | [查看图片](Shovel_Wood/Shovel_Wood_Icon.png) |
| 铲 | 石铲 | `Shovel_Stone` | [查看图片](Shovel_Stone/Shovel_Stone_Icon.png) |
| 铲 | 铜铲 | `Shovel_Copper` | [查看图片](Shovel_Copper/Shovel_Copper_Icon.png) |
| 铲 | 粗铁铲 | `Shovel_RawIron` | [查看图片](Shovel_RawIron/Shovel_RawIron_Icon.png) |
| 铲 | 青铜铲 | `Shovel_Bronze` | [查看图片](Shovel_Bronze/Shovel_Bronze_Icon.png) |
| 铲 | 铁铲 | `Shovel_Iron` | [查看图片](Shovel_Iron/Shovel_Iron_Icon.png) |
| 锄 | 木锄头 | `Hoe_Wood` | [查看图片](Hoe_Wood/Hoe_Wood_Icon.png) |
| 锄 | 石锄头 | `Hoe_Stone` | [查看图片](Hoe_Stone/Hoe_Stone_Icon.png) |
| 锄 | 铜锄头 | `Hoe_Copper` | [查看图片](Hoe_Copper/Hoe_Copper_Icon.png) |
| 锄 | 铁锄头 | `Hoe_Iron` | [查看图片](Hoe_Iron/Hoe_Iron_Icon.png) |
| 小刀 | 石制小刀 | `Dagger_Stone` | [查看图片](Dagger_Stone/Dagger_Stone_Icon.png) |
| 小刀 | 铜制小刀 | `Dagger_Copper` | [查看图片](Dagger_Copper/Dagger_Copper_Icon.png) |
| 小刀 | 骨制小刀 | `Dagger_Bone` | [查看图片](Dagger_Bone/Dagger_Bone_Icon.png) |
| 小刀 | 燧石小刀 | `Knife_Flint` | [查看图片](Knife_Flint/Knife_Flint_Icon.png) |
| 小刀 | 黑曜石小刀 | `Dagger_Obsidian` | [查看图片](Dagger_Obsidian/Dagger_Obsidian_Icon.png) |
| 锤 | 木锤 | `WoodHammer` | [查看图片](WoodHammer/WoodHammer_Icon.png) |
| 锤 | 石锤 | `StoneHammer` | [查看图片](StoneHammer/StoneHammer_Icon.png) |
| 采集与钓鱼 | 剪刀 | `Scissors` | [查看图片](Scissors/Scissors_Icon.png) |
| 采集与钓鱼 | 打制石器 | `ChippedTool` | [查看图片](ChippedTool/ChippedTool_Icon.png) |
| 采集与钓鱼 | 钓鱼竿 | `FishingRod` | [查看图片](FishingRod/FishingRod_Icon.png) |
| 生火与照明 | 打火石 | `FlintStrike` | [查看图片](FlintStrike/FlintStrike_Icon.png) |
| 生火与照明 | 钻木取火工具 | `SparkMaker_Summoner` | [查看图片](SparkMaker_Summoner/SparkMaker_Summoner_Icon.png) |
| 生火与照明 | 火把 | `Torch_Summoner` | [查看图片](Torch_Summoner/Torch_Summoner_Icon.png) |
| 钻孔 | 手钻 | `HandDrill_Summoner` | [查看图片](HandDrill_Summoner/HandDrill_Summoner_Icon.png) |

## 生成来源与记录

全部图片使用内置 `image_gen` 工具独立生成，未用脚本绘图或给同一图批量换色。记录保留原始生成路径、最终文件路径、完整提示词、参考路径和实际尺寸；原图复制至本目录各 ID 子目录。

- [斧与镐](records-axes-pickaxes.json)
- [铲与锄](records-shovels-hoes.json)
- [小刀、锤、剪刀和打制石器](records-knives-hammers.json)
- [钓鱼、生火、照明和钻孔](records-survival.json)
- [统一制作约定](ProductionBrief.md)

当前交付状态：**图片已生成；主Agent整合游戏引用**。此状态不代表已完成 PlayMode、手持表现或游戏内验收；主 Agent 完成整合后更新此处。
