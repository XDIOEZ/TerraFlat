# FlatWorld Excel 配置同步

## 使用方式

1. 在 Unity 中打开 `FlatWorld > Excel配置同步`。
2. 初次使用时可以从 Prefab 重新导出三份工作簿。
3. 后续只修改 Excel 中的黄色列。
4. 保存 Excel 后，Unity 会自动校验整张表；全部通过才会写入 Prefab。
5. Console 会输出每个发生变化的字段。

## 映射规则

- `AssetGuid`：唯一定位 Prefab，Prefab 改名或移动后仍能映射。
- `ItemId`：二次校验，防止 GUID 或表格行错位。
- `PrefabPath`：只用于查看，Prefab 移动后会通过 GUID 自动解析新路径。
- `Enabled`：设为 `FALSE` 可暂时跳过该行。

不要手动修改灰色的 `AssetGuid`、`ItemId`、`PrefabPath` 列。

## 数据方向

首次建立：`Prefab -> Excel`

日常维护：`Excel -> Prefab`

从 Prefab 重新导出会覆盖 Excel，因此需要显式确认。
