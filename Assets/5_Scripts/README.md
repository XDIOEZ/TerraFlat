# 脚本目录导航

| 目录 | 职责 |
| --- | --- |
| [5-0_WorldModel](5-0_WorldModel/README.md) | 世界与区块纯数据、生成、地形、液体和天气 |
| `5-1_Data` | 内容数据定义、实例数据与序列化模型 |
| `5-3_GamePlay/Core` | 启动、资源会话、世界生命周期、存档与规则 |
| `5-3_GamePlay/World` | 世界模型的 Unity 宿主、流送、地形表现、环境与机器 |
| `5-3_GamePlay/Entities` | 实体、物品模块、AI、战斗与 ECS |
| `5-3_GamePlay/Items` | 库存、制作和具体物品使用规则 |
| `5-3_GamePlay/Player` | 玩家、输入与交互控制 |
| `5-3_GamePlay/Presentation` | 对话、引导、特效、玩法面板与交互表现 |
| `5-3_GamePlay/Extensibility` | MOD、Lua 和内容扩展 |
| `5-3_GamePlay/Physics2D` | 世界物理边界、碰撞与接触查询 |
| `5-3_GamePlay/Development` | 调试、诊断、运行时工具与 Lua 测试夹具 |
| `5-4_Networking` | 联机协议、Mirror 适配和玩法同步 |
| `5-5_UI` | 通用面板、控件、主题、输入焦点与设置 |
| `5-6_Audio` / `5-7_Localization` | 音频与本地化服务 |
| `Shared` | 跨程序集复用的基础能力 |
| `Shader` | Sprite Shader 的自动配置脚本；Shader 资源位于 `Assets/9_Shaders` |

新增代码先选所属系统；只有确实被多个系统共用的能力才进入 `Shared`。编辑器代码放到 `Assets/Editor/FlatWorld` 或所属程序集的 `Editor` 目录。
