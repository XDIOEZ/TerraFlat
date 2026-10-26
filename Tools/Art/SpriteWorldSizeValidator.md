# 贴图世界尺寸检测

Unity 菜单：`FlatWorld/其他工具/美术/贴图世界尺寸检测`。选择 PNG 或 Sprite，填实际额外缩放和目标最长边，再点「检测尺寸」。也可以直接载入钻木取火器样本；它的目标是 **0.70 Unity 单位**，不是所有物品的统一尺寸。

工具只读文件，不调整 PPU、不压缩图片、不修改素材、不刷新资源，不切换或暂停 PlayMode。编辑器窗口异步调用同一份 Python 检测器；首次新增窗口需等 Unity 自然结束 PlayMode 并编译后出现，离线检测器现在即可运行。

## 计算与对照

`可见宽高（Unity单位） = Alpha可见边界像素宽高 ÷ 源图配置PPU × |实际缩放X/Y|`

- 默认从相邻 `.png.meta` 读取 `spritePixelsToUnits`，排除透明留白；Alpha 阈值默认 1，可调高以忽略很淡的边缘。
- 支持不同源分辨率、不同 PPU、负数镜像、非均匀缩放。只有目标尺寸有意义时才判断大小一致；不指定目标则仅测量。
- 按用途设置目标最长边或宽高，或选同类参考图对照可见宽高。建议 PPU 保留源图，宽高比不同时一个 PPU 不能同时精确匹配两个目标。
- `.meta` 的默认/启用平台覆盖 `maxTextureSize` 会缩小源图时给出警告。忽略 Alpha 的导入器按完整矩形计算；灰度生成 Alpha 拒绝预测。
- 图集需显式指定 `--sprite-rect X Y W H`，按源图像素和 Unity 左下原点。报告中的可见边界是**切片内左上原点**的 `left/top/right/bottom`，右下不含。
- 这是**源图及导入配置的尺寸预测**，不是当前 PlayMode 中已加载旧 Sprite 的测量。父级变换、旋转、动画、Shader 位移、相机和 UI 拉伸需另行确认；不把库存 UI 图标像素大小当成世界尺寸。

## 离线用法

使用已装 Pillow 的 Python；没有时提示缺少依赖，不自动安装。窗口可填写 Python 完整路径。

```powershell
# 钻木取火器：自动读取本次物品配置中的 visual.rendererLocalScale
python Tools/Art/validate_sprite_world_size.py Assets/6_Art/Generated/Tools/PrimitiveSet_20261023/SparkMaker_Summoner/SparkMaker_Summoner_Icon.png --shell-manifest Assets/StreamingAssets/GameConfig/Items/shells/building_summoners.json --item-id SparkMaker_Summoner --target-longest 0.70

# 同类素材与参考图比宽高，允许 ±5%；参考图读取自己的 .meta
python Tools/Art/validate_sprite_world_size.py path/to/new.png --reference path/to/reference.png --tolerance 0.05

# 没有 .meta 时显式 PPU，或加入父级及物品的总缩放
python Tools/Art/validate_sprite_world_size.py path/to/new.png --ppu 1000 --scale -0.5 0.5 --target-longest 0.7 --json
```

`--shell-manifest` 与 `--item-id` 同用，支持同清单父项的 visual 字段继承并核对 `spriteAddress`，额外 `--scale` 在配置缩放之外继续相乘。跨清单父项缺失会拒绝，避免漏算继承。窗口手动缩放同样要包含这些因素。

退出码：`0` 为测量成功或目标通过，`1` 为素材/尺寸不通过，`2` 为参数/参考/清单错误。JSON 具有 `size_matches_target`，未设置目标时为 `null`，不能当成已验证尺寸一致。

## 回归

```powershell
python Tools/Tests/SpriteWorldSize/test_validator.py
```

测试直接调用生产检测器，包含分辨率/PPU 成倍变化、透明留白、Alpha 阈值、镜像非均匀缩放、目标和参考不一致、建议 PPU、空图、非法数值、图集边界、平台降采样风险、外壳继承和本次钻木取火器实图；不启动 Unity。
