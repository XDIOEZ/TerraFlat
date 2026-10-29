# FlatWorld 项目导航

Unity 2022.3.62f3c1 的 2D 俯视角沙盒游戏。按下面的入口寻找代码、资源和开发资料。

| 要找什么 | 入口 |
| --- | --- |
| 玩法、世界模型和公共能力 | [Assets/5_Scripts](Assets/5_Scripts/README.md) |
| 自研编辑器工具 | [Assets/Editor/FlatWorld](Assets/Editor/FlatWorld/README.md) |
| Prefab / 场景 / ScriptableObject | `Assets/2_Prefabs` / `Assets/3_Scenes` / `Assets/4_ScriptObjects` |
| 正式美术、地块、动画和 Shader | `Assets/6_Art` / `Assets/7_Tiles` / `Assets/8_Animations` / `Assets/9_Shaders` |
| 运行时配置、Lua 回归和 ItemWiki | `Assets/StreamingAssets` |
| 按名称加载的 Unity 资源 | `Assets/Resources`；路径也是加载契约，移动时同步消费方 |
| 音频素材与制作工程 | `Assets/Audio` / `SoundDesign` |
| 构建、测试和离线工具 | [Tools](Tools/README.md) |
| MOD 开发和示例 | [ModSDK](ModSDK) |
| 设计、待办、验证记录和美术样张 | [开发文档文件夹](开发文档文件夹/README.md) |
| 世界观与机制图 | `故事背景文件夹` / [游戏机制SVG文件夹](游戏机制SVG文件夹/README.md) |
| Agent 工作规则 | [AGENTS.md](AGENTS.md) 与 `.agents/skills` |

## 放置约定

- 新文件放到已有职责目录；一个主题只有一个维护入口。
- 正式资源留在 `Assets`；网页演示放 `Tools/Demos`，第三方示例归 [Tools/ReferenceSamples](Tools/ReferenceSamples/README.md)，美术对照稿归 [参考样张](开发文档文件夹/09_美术素材/参考样张/README.md)。
- 第三方插件保留供应商布局；自研代码分别放在 `Assets/5_Scripts` 和 `Assets/Editor/FlatWorld`。
- Unity 资源移动时保留 `.meta` 身份，并更新文档、工具和测试中的路径。
- `Library`、`Temp`、`Logs`、`Builds`、`.codex-tmp` 和根部 `.csproj/.sln` 是本地生成内容，不作为源文件归档。
