# 地块 JSON 配置与 MOD 扩展

## 配置入口

`tile-manifest.json` 显式声明分包。`terrain.json` 保存自然地表、水体、冰雪和耕地；`buildings.json` 保存墙、岩壁、平台和地板。未写入清单的文件不会自动加载。

JSON 是静态配置唯一真源。旧 `Tile_Block.asset` 仅保留地块 ID 和原 GUID，供群系、结构编辑器及 Prefab 引用；选中这些引用资源后可以通过 Inspector 的“打开地块 JSON”按钮进入配置文件。不要把数值或 Behaviour 再写回 SO。

资源加载阶段执行 `JSON → TileDefinitionFactory → RuntimeTileDefinition → GameRes`。每种定义创建一组共享 Behaviour；玩家踩格子时按定义调用 `OnEnter / OnUpdate / OnExit`，不重新解析 JSON，也不为每个格子创建 Behaviour。

## 格式

本体分包根为 `schemaVersion: 1` 和 `tiles` 数组。下面展示一份使用现有冰面资源的地块定义：

```json
{
  "id": "sample.tiles:ice_path",
  "runtimeTileId": 1000001,
  "displayName": "光滑冰径",
  "tileAsset": "TileBase_Ice",
  "data": {
    "type": "universal",
    "parameters": {
      "isWalkable": true,
      "penalty": 1000
    }
  },
  "behaviours": [
    {
      "type": "ice",
      "parameters": {
        "accelerationMultiplier": 0.35,
        "decelerationMultiplier": 0.15
      }
    }
  ]
}
```

`id` 是地块身份，`runtimeTileId` 是世界模型保存的稳定整数，`tileAsset` 是已加载的 TileBase 资源键。三者不可混用。JSON 不保存 Unity 对象、资源 GUID、程序集类名或方法体。

本体数字编号保留原值。`0` 仅用于不进入新版格子的旧地图定义；MOD 新地块必须显式指定不小于 `1000000` 的编号。不同地块编号冲突会报错，不会静默覆盖，也不会按 MOD 加载顺序自动分配。已经发布到存档的身份和编号不能改变。

`data.parameters` 和行为 `parameters` 使用现有 C# 配置字段的 camelCase 名称。未知字段、未知类型、无效枚举、重复 JSON 键、非有限数值和越界参数会被拒绝；不得使用 `$type`。`TileData.ID/Name` 由根 `id` 注入，`position/workTime/Version` 不属于静态配置。

## 内建类型

数据类型：`universal`、`grass`、`water`、`farmland`、`cellBuilding`。

行为类型：`universal`、`grass`、`water`、`farmland`、`ice`、`snow`。行为按数组顺序调用，具体功能仍由对应的 C# 类实现。水体行为要求 `water` 数据，耕地行为要求 `farmland` 数据；组合行为时仍应遵守各行为的进入、更新和清理契约。

`water` 数据/行为只保留代码与历史序列化兼容入口；新的世界水体在 `Liquids/liquids.json` 声明 `worldWater`，不再创建 Water Tile。液体身份使用已注册的 `LiquidId`，例如 `core:dirty_water` 或 `core:sea_water`。`buffInfo` 使用真实 Buff ID，例如 `潮湿`，不是显示英文译名。潮湿叠层和水深结算继续由现有水体/Buff 系统处理，不要额外复制一套每格计时器。

墙体伤害使用 `damageProfile`；平台和地板使用 `groundPlacement`。可配置字段以实际分包为准。枚举支持合法名称或数字，例如 `requiredTool: "Pickaxe"`、`requiredSourceFlags: "Water"`。

## JSON MOD

在 MOD 的 `manifest.json` 中通过 `definitionFiles` 声明文件，并将上例放入该文件根级 `tiles` 数组。根 `id` 必须使用当前 MOD 的 `modId:` 命名空间；上例的 MOD ID 为 `sample.tiles`。

可以复用本体 TileBase 资源。自带外观时，仍通过 MOD `assets` 中的 `type: "tile"` 注册 Bundle 内的 TileBase，再将 `tileAsset` 指向注册 ID。Bundle 不再承载地块逻辑 SO；旧 `type: "tileblock"` 会报告迁移指引，而不是继续读取第二份 SO 配置。

注册新定义只让目录认识它，不会自动把它撒进世界。还需要在生成规则、建筑物品或其他正式玩法入口中引用该地块。新版行为查询、建筑数字编号解析和 BRG/Tilemap 表现映射均支持 JSON 地块，无需为新 MOD 地块修改本体 Palette SO。冻结世界的原有映射仍优先，不能用新编号覆盖老地形身份。

### 覆盖已有地块

在 `patchFiles` 的文件中使用 `tile:<地块ID>` 作为目标，沿用项目既有的 `operation/path/value/expect/optional` 协议。例如：

```json
{
  "patches": [
    {
      "target": "tile:Tile_Snow",
      "operation": "replace",
      "path": "/behaviours/0/parameters/moveSpeedMultiplier",
      "expect": 0.9,
      "value": 0.8
    }
  ]
}
```

Patch 不能更改 `id` 或 `runtimeTileId`。地块定义与 Patch 先完整构建、校验，再发布；失败或卸载会恢复被覆盖的原定义，并清理新增地块的整数映射和 MOD TileBase 字典键。配置变更在下一次资源加载时生效，不在角色正在行走时原地替换共享行为。

## C# 扩展

新参数组合可以直接复用已有行为。全新算法由代码 MOD 在地块目录加载前调用 `TileBehaviourRegistry.RegisterBehaviour("modId:behaviour", factory)`；新 TileData 类型使用 `RegisterData`。工厂接收参数 `JObject`，可以调用 `TileDefinitionJson.Populate` 使用同一严格字段契约。

注册返回 `IDisposable` 租约，代码 MOD 应持有它并在卸载时释放。工厂每次必须返回当前定义自己的实例；不要返回所有定义共用、再被后续参数覆盖的可变对象。

共享 Behaviour 只能保存规则参数。角色计时、环境效果实例等留在 `TileEffectReceiver / EnvironmentInteractionRunner`；格子数据留在 `ChunkTerrainData` 及其权威扩展层。自定义 TileData 的 `Clone()` 必须深复制可变成员；需要进入旧 MemoryPack TileData 存档时还需另外处理序列化类型注册。JSON 行为注册本身不会自动增加 MemoryPack Union，也没有新增任意 Lua 方法执行入口。

原 `Tile_Water` 等行为类及生命周期方法保留。`GameRes.GetTileBlock(string)` 现在返回 `RuntimeTileDefinition`，旧代码 MOD 的返回类型声明和相关 Harmony Patch 签名需要相应更新；只读取常用 `tileDataTemplate / behaviours / GetTileBaseAsset()` 的调用保留同名入口。

## 检查

Unity 菜单 `FlatWorld/诊断/检查地块 JSON 目录` 进行只读目录检查，不创建世界或读取玩家存档。资源装配工具也改为显式更新 JSON，不再写 SO 数值。运行时主菜单加载会继续执行本体与 MOD 的资源引用校验，任何失败都阻止资源目录进入 Ready。
