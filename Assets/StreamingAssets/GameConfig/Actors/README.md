# FlatWorld Actor JSON

## 本体目录

- 唯一入口：`actor-manifest.json`。
- 当前正式 Actor：`Chicken`、`WildBoar`、`Wolf`、`Ghost`。
- `Chicken_Tree`、`WildBoar_Tree` 是不实现现代 `IAIActor` 契约的旧 Kiwi 行为树兼容资源，继续保留给历史内容，但不进入正式 Actor 目录。
- `shellPrefab` 是逻辑外壳 ID；`shellAddress`、`spriteAddress`、`animatorControllerAddress` 是稳定 Addressables 地址。
- `sourcePrefab` 只供编辑器迁移和人工定位，运行时禁止依赖资源路径。
- Prefab 保存组件结构、事件引用和安全默认值；JSON 保存名称、标签、数值、Sprite、Animator、碰撞体与模块参数。

## MOD Actor 最小示例

```json
{
  "actors": [
    {
      "id": "my.mod:forest_wolf",
      "parent": "Wolf",
      "gameName": "Forest Wolf",
      "modules": {
        "ai": {
          "parameters": {
            "alertDetectDistance": 24.0,
            "chaseTriggerDistance": 34.0
          }
        }
      }
    }
  ]
}
```

- `parent` 可引用本体 Actor 或同次加载中的 MOD Actor；模块对象按字段深度合并，数组整体替换。
- 换 Sprite：在 `visual` 写 `spriteBundle` 与 `spriteAsset`。
- 换 Animator：在 `visual` 写 `animatorControllerBundle` 与 `animatorControllerAsset`。
- 单纯换外观优先继承本体外壳；无需复制 AI Prefab。
- 自定义 `shellPrefab` 必须引用已在 `assets` 注册、且确实包含 `Item + IAIActor` 的 Prefab。
- 自定义外壳资源 ID 与最终 Actor ID 应分别命名，例如 `my.mod:wolf_shell` 与 `my.mod:forest_wolf`，避免内容注册冲突。

## Lua 功能扩展

在 `modules` 中新增 `Mod_LuaBehaviour`：

```json
"lua": {
  "prefab": "Mod_LuaBehaviour",
  "id": "Mod_LuaBehaviour",
  "enabled": true,
  "parameters": {
    "scriptPath": "Lua/actor.lua",
    "tickMode": 1,
    "fixedTickInterval": 0.5
  }
}
```

- 运行时会强制把 Lua 模块绑定到定义所属 MOD；作者不能用 JSON 伪造 `modId`。
- Lua 可实现 `OnLoad`、`OnUpdate`、`OnAct`、`OnSave`，并通过受限 Actor API 读取位置/生命和调用 `MoveTo`、`StopMoving`。
- AssetBundle 不能注入新的 C# 程序集。完全新 AI 逻辑现阶段应组合本体模块与 Lua；需要新底层能力时，由游戏本体先提供安全 API。
