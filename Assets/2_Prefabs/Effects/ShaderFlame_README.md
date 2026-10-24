# 可复用 Shader 火焰

将同目录的 `VFX_ShaderFlame.prefab` 拖入场景或作为物品的子物体即可；网格在启用时绑定为 Unity 内置 Quad，底部中心是火焰锚点。

## 配置

- `ShaderFlameEffect`：Size 为火焰宽高，Intensity 为火势，Burning 为开关；Sorting Layer Id / Sorting Order 决定独立使用时的排序。
- 默认材质：`Assets/9_Shaders/Resources/Combustion_Flame.mat`。使用硬轮廓、四色火舌与单像素火星，可调整颜色、上升速度、扰动、风向与火星可见时长。
- Pixels Per World Unit 默认 32，像素密度按实例世界尺寸计算，宽高变化与父级缩放后仍接近方形像素；Animation Frames Per Second 默认 12，由 GPU 时间分帧。需要另一种颜色或速度时复制材质并指定给实例。

```csharp
ShaderFlameEffect flame = instance.GetComponent<ShaderFlameEffect>();
flame.Configure(new Vector2(0.3f, 0.6f), 1.1f); // 使用共享默认材质。
flame.SetSorting(source.sortingLayerID, source.sortingOrder + 2);
flame.SetBurning(true);
// 熄灭或回池时调用 flame.SetBurning(false)。
```

`Configure` 第三个参数可传自定义材质；不传则恢复共享默认材质。角色下使用单独子节点，不与 SpriteRenderer 共占一个节点。

## 已接入的位置

`CombustionVisualEffect` 转发火把/设施的燃烧状态、灯开关、挂点与排序；`WorldItemCombustionVisual` 为普通与轻量掉落物创建相同火焰。掉落火焰使用独立表现根，只同步世界位置，不继承物品翻滚和缩放；轻量池淘汰或释放时一并销毁。燃料、温度、伤害和点火条件不由特效处理。

旧内容配置中的 `flameScale`、`flameHeightRatio`、挂点与跟随参数继续被读取；`flameEmissionMultiplier` 现在映射为火势，零值隐藏火焰，不再决定粒子数量。

手持火把通过定义中的 `rendererLocalEulerAngles.z = 315` 将棍身对齐通用本地 +X 瞄准轴；火焰位置跟随贴图燃烧头，`attachFlameToAnchor = false` 保持朝世界上方燃烧。放置火把保留直立棍身，也使用向上的火焰。

## 性能与边界

单个火源是四顶点、两个三角形；借用内置网格、不克隆材质、不分配粒子、不在 CPU 逐帧更新动画。Shader 使用 `_Time`；每个实例的相位固定，不消费游戏随机数。宿主适配器仍需读取动态挂点、光源和排序，只有变化时才写回。尺寸更新同时调整 CPU 裁剪边界。

实例参数使用 MaterialPropertyBlock，并提供 instancing 变体；不保证透明物体在不同排序键下能合为一次绘制，也不宣称走 SRP Batcher。透明填充与叠加仍有 GPU 成本，实际收益需要场景实测。

常驻烟雾与粒子火星已移除，火星由 Shader 绘制；仅入水熄灭保留一次性烟汽。此版本未进行游戏现场画面、完整 Lua 回归或 Android GPU 验收。
