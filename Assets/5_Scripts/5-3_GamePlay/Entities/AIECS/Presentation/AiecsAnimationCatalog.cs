using System;
using UnityEngine;

namespace FlatWorld.AIECS
{
    /// <summary>
    /// 从启用的 Actor Manifest 和实际动画控制器导出的只读表现目录。
    /// 帧几何保留原图尺寸、Pivot 和 PPU；图集只改变采样坐标，不参与生命或攻击结算。
    /// </summary>
    public sealed class AiecsAnimationCatalog : ScriptableObject
    {
        // 配置及动画依赖指纹，供重新导出时识别资源漂移。
        public string SourceFingerprint;
        // 共用 Lit 材质与点采样图集。
        public Material Material;
        // 原图几何和图集区域。
        public AiecsSpriteGeometry[] Sprites;
        // 继承合并后的物种表现定义。
        public AiecsActorVisual[] Actors;
        // 未支持的曲线、状态与事件说明，不能当成已迁移能力。
        public string[] Diagnostics;
    }

    /// <summary>一张 Sprite 的真实局部矩形与专用图集 UV，单位沿用原始 PPU。</summary>
    [Serializable]
    public sealed class AiecsSpriteGeometry
    {
        // 编辑器来源，用于定位导出问题。
        public string Source;
        // 带 Pivot 的局部几何范围。
        public Rect LocalRect;
        // 非透明像素的局部范围；导出时计算，脚底阴影不在运行时读回纹理。
        public Rect VisibleRect;
        // 无旋转的专用图集区域。
        public Rect AtlasRect;
    }

    /// <summary>单个 Actor 的表现配置；不包含旧 Item、Animator 或 Module 实例。</summary>
    [Serializable]
    public sealed class AiecsActorVisual
    {
        // Actor 稳定定义 ID。
        public string Id;
        // 原有外部排序语义。
        public int SortingLayerId;
        public int SortingOrder;
        // JSON 的颜色与默认镜像。
        public Color Color = Color.white;
        public bool FlipX;
        public bool FlipY;
        // 跨动作固定身体参考范围，避免切帧导致水线抖动。
        public Vector2 BodyYRange;
        // 实际状态名对应的独立帧表。
        public AiecsAnimationClip[] Clips;
    }

    /// <summary>一个 Animator 状态的图片时间轴；非循环动作必须停在末帧。</summary>
    [Serializable]
    public sealed class AiecsAnimationClip
    {
        // 完整状态路径，避免嵌套状态重名。
        public string State;
        // 已计入状态速度的时长与循环规则。
        public float Duration;
        public bool Loop;
        // 按开始时刻递增的帧。
        public AiecsAnimationFrame[] Frames;
        // 原始语义曲线与事件仅供后续迁移，不由渲染器调用。
        public AiecsAnimationMarker[] Markers;

        /// <summary>按每个实体独立的播放时钟查找帧，非循环动作保持最后一帧。</summary>
        public AiecsAnimationFrame Sample(float seconds)
        {
            float time = Loop ? Mathf.Repeat(seconds, Duration) : Mathf.Clamp(seconds, 0f, Duration);
            int low = 0;
            int high = Frames.Length - 1;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (Frames[mid].Start <= time) low = mid;
                else high = mid - 1;
            }
            return Frames[low];
        }
    }

    /// <summary>按实际 Sprite 键帧和 Transform 曲线采样的图片帧。</summary>
    [Serializable]
    public struct AiecsAnimationFrame
    {
        // 此帧开始时间与共享图片索引。
        public float Start;
        public int Sprite;
        // 包含 JSON 基础姿态的局部变换。
        public Vector3 Position;
        public Vector3 Scale;
        public float Rotation;
    }

    /// <summary>保留原始动画语义供 P3/P5 映射，绝不直接回调旧 MonoBehaviour。</summary>
    [Serializable]
    public sealed class AiecsAnimationMarker
    {
        // 来源类型、路径和属性或事件函数。
        public string Binding;
        // 原始曲线（包括切线）或事件时刻与参数文本。
        public AnimationCurve Curve;
        public float Time;
        public string Payload;
    }
}
