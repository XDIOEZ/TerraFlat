# Character Soliloquy

## 责任边界

- `CharacterSoliloquyController`：计时、优先级、Provider 调度。
- `HungerSpeechProvider`：读取 `Mod_Food`，生成饥饿台词。
- `ScreenSpaceSpeechBubblePresenter`：只负责屏幕空间气泡显示与角色跟随。
- `CharacterSpeechContracts`：稳定扩展接口与上下文数据。

## 接入新的角色状态

实现 `ICharacterSpeechContextContributor`，通过 `context.SetFact()` 写入稳定键值。

## 接入新的台词来源

实现 `ICharacterSpeechProvider`。可同步回调，也可异步请求大模型后回调；异步回调必须回到 Unity 主线程。

## 测试入口

运行游戏后在 Hierarchy 选择 Player，在 `CharacterSoliloquyController` 组件菜单中可直接触发三种测试气泡。
