# UI 动画系统

## 依赖

`BasePanel → BaseUIAnimation → UIAnimationManager → UIAnimations.json`

`BasePanel` 是面板开关状态的唯一权威，直接调用同 GameObject 上的 `BaseUIAnimation`。动画组件不引用、不监听 `BasePanel`，只负责 CanvasGroup、位移、缩放等视觉过渡；没有动画组件时保持原来的即时 Open/Close。

`UIAnimationManager` 缓存 JSON 配置、登记活动动画实例并提供统一播放倍率。统一倍率只写入本系统自己的 Tween，不修改 `DOTween.timeScale` 或 `Time.timeScale`。

## 配置

配置位于 `Resources/Config/UIAnimations.json`，`AnimationId` 为稳定匹配键。动画速度不存入 JSON，移动也不按物理分辨率二次换算；CanvasScaler 会自动缩放参考 UI 坐标。

```json
{
  "version": 1,
  "defaultId": "panel.default",
  "profiles": [
    {
      "id": "panel.slide",
      "data": {
        "openDuration": 0.24,
        "closeDuration": 0.18,
        "offsetX": 0,
        "offsetY": -48,
        "closedScale": 0.94,
        "openEase": "OutCubic",
        "closeEase": "InCubic"
      }
    }
  ]
}
```

`openDuration/closeDuration` 是完整行程秒数；`offsetX/offsetY` 是相对 Prefab 静止位置的 UI 坐标偏移；`closedScale` 是关闭态相对缩放。

## 扩展

- `BaseUIAnimation`：淡入淡出基类。
- `SlideUIAnimation`：淡入淡出 + 相对位移。
- `ScaleUIAnimation`：淡入淡出 + 相对缩放。
- 滑动/缩放优先指定独立 `MotionRoot`，避免与 Layout、SafeArea、拖拽器争写同一 RectTransform。
- 快速 Open/Close 反向时从当前进度继续，旧 Tween 回调不能覆盖最新状态。

测试配置 `panel.test.slide` 提供 120 UI 单位的横向滑入，用 PlayMode 分类 `UI.Animation` 验证真实 DOTween 位移和完成状态。
