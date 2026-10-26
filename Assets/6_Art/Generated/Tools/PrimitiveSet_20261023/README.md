# FlatWorld 原始手作工具重绘

初版日期：2026-10-23；GIMP 像素整理：2026-10-24。**35 款工具按用户认可的三张样例，用 GIMP MCP 原生一像素铅笔沿原图合并碎色；保留材质、轮廓和原生像素。24 款画布仍为 32×32，11 款斧/镐裁掉外围透明留白，使背包图标更饱满；游戏内观感由用户确认。**

2026-10-26 更新：钻木取火器 `SparkMaker_Summoner` 已重新绘制，保留 1254×1254 生成原图与 Alpha，以 1778.571428571 PPU、2048 导入上限控制为可见约 0.679×0.700 Unity 单位；召唤器与放置机身共用同图和 1 倍缩放。它是下文旧 32 像素/GIMP 整理记录的例外，当前数据及提示词见 [本轮重绘记录](../../../../../Tools/Art/SparkMakerRedraw_20261026.json)，尺寸检测见 [检测工具](../../../../../Tools/Art/SpriteWorldSizeValidator.md)。

2026-10-26 手钻更新：`HandDrill_Summoner` 重绘为双绳横杆驱动的泵式手钻，保留 1254×1254 生成原图和 Alpha，采用约 1734.286 PPU、2048 导入上限；可见约 0.700×0.676 Unity 单位，便携与放置形态继续共用同图。此图同样不采用旧 32 像素导入规格，提示词与参数见 [手钻重绘记录](../../../../../Tools/Art/HandDrillRedraw_20261026.json)。

本套包含 35 款成品工具。双击 [Gallery.html](Gallery.html) 可离线浏览：支持分类筛选、深浅背景和三档显示大小；点击图片可打开原图。浏览页只改变显示尺寸，不修改 PNG 像素，无需网络或启动游戏。

## 接入说明

- 35 款成品工具共更新 38 个具体物品/放置态定义，包含钻木取火工具、火把、手钻的便携与放置形态；另同步火把公共基类的表现锚点。
- 新图登记到独立的 `PrimitiveTools` Addressables 组，具有 `ItemSprite` / `Sprite` 标签；旧图保留，现用物品改为新地址。
- 成品沿用 32 像素源网格、0/255 硬透明、Point、无 Mipmap、无压缩，最大导入尺寸 32。24 款保持 32×32；6 斧和 5 镐只将透明边裁到可见包围盒外 1 像素，不缩放或重采样可见像素，最终画布尺寸以 [ImportManifest.json](ImportManifest.json) 的每件记录为准。所有 PPU 保持整理前原值，斧/镐仅按裁切偏移重基 Pivot，使手持世界坐标不变；GUID、Sprite ID 和既有引用保持。
- 游戏手持前方统一为局部右方 `+X`。本套 30 件可挥动工具的原图从左下握柄指向右上工具头，子 Renderer 使用 `z=315°`、`flipX=false`、`flipY=false`；握柄世界锚点保持不变；斧/镐的归一化 Pivot 已随紧裁重基。父层 `Render` 继续承载原有动作，不能沿用旧版 `225° + flipX`，否则锄刃会翻到握柄轴的另一侧。
- 火把新图不烘焙火焰，保留运行时燃烧；斜向原图通过 Renderer 旋转竖起，并同步燃烧和灯光位置。钓鱼面板的直接 Sprite 引用及编辑器构建器也已改为新鱼竿。
- 本轮没有主动 Import、Refresh、切换 PlayMode 或重载，没有运行玩法验收；源文件与导入配置已写入，Unity 导入和控制台状态由本轮交付说明报告。实际握持、动作、细线、燃烧和背包观感由用户验收。

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
- 斧头用中灰石切面、近黑燧石、红铜、金棕青铜、深炭灰粗铁和较亮银蓝灰铁区分。金属只保留短而连贯的刃光，石材保持哑光切面；木柄和绑带合并碎亮点与棋盘杂色，轮廓修顺毛刺，不用模糊消除像素。
- 本轮在原图上用 GIMP MCP 原生 `1. Pixel` 铅笔整理，未重新生成、模糊、全图限色或缩小；每件保留原图层、修补层及明确逐点颜色差量。已认可的铁斧、铜镐、青铜铲样例精确沿用；斧/镐额外仅裁透明边。

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

本轮全部通过 **GIMP MCP 原生一像素铅笔**沿原图整理。原图与修补层、逐点原色/新色/原因、裁切位置及锚点换算保存在 [GIMP 源稿与计划](../../../../../Tools/Art/ToolGimpRetouch_20261024/README.md)。当前 PNG 的实际尺寸、色数、Alpha 包围盒、PPU 和 hash 以 [PixelRedrawRecords.json](PixelRedrawRecords.json) 为准；旧 32×32 和 64×64 图像来源、提示词与参数完整保留在 `history`。

上一轮 AI 去噪与材质整理记录：[斧与镐](records-retouch-axes-pickaxes.json)、[铲与锄](records-retouch-shovels-hoes.json)、[小刀及生存工具](records-retouch-small-tools.json)。

- [斧与镐](records-axes-pickaxes.json)
- [铲与锄](records-shovels-hoes.json)
- [小刀、锤、剪刀和打制石器](records-knives-hammers.json)
- [钓鱼、生火、照明和钻孔](records-survival.json)
- [统一制作约定](ProductionBrief.md)

当前交付状态：**35 款 GIMP 整理成品已写入；24 款为 32×32，11 款斧/镐为保留 1 像素透明边的紧裁画布，现有引用与手持世界锚点保持。** 本轮未主动 Import、Refresh 或切换 PlayMode，未做游戏验收；实际游戏观感由用户验收。
