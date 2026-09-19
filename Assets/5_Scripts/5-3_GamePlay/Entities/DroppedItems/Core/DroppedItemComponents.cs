using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.DroppedItems
{
    /// <summary>掉落物的权威热数据；位置是地面位置，抛物线高度仅供显示，不改变水格与拾取距离。</summary>
    public struct DroppedBody : IComponentData
    {
        public int Id;
        public float2 Position;
        public float2 Scale;
        public float Rotation;
        public float VisualHeight;
        public float Amount;
        public float WaterDepth;
        public float SubmergedProgress; // 完全入水后的视觉远离进度，原始 Scale 保持不变。
        public byte WaterKind; // 0 陆地，1 漂浮，2 下沉。
        public byte Pickable;
    }

    /// <summary>只有抛掷中的实体才拥有此组件；落地后移除，静止掉落物不参加运动查询。</summary>
    public struct DroppedFlight : IComponentData
    {
        public float2 Start;
        public float2 End;
        public float2 Control;
        public float Duration;
        public float Elapsed;
        public float ArcHeight;
        public float RotationSpeed;
    }

    /// <summary>短期水线过渡；漂浮稳定后移除，水波留给共享 Shader，不保留逐物品 Update。</summary>
    public struct DroppedWaterTransition : IComponentData
    {
        public float StartDepth;
        public float TargetDepth;
        public float Duration;
        public float Elapsed;
        public float RecedeDuration; // 浸没后的缩小阶段；漂浮物为零。
    }

    /// <summary>运动系统交付给主线程的变化；只含稳定 ID，不把 Entity 地址写入存档。</summary>
    public struct DroppedChange
    {
        public int Id;
        public byte Kind; // 0 姿态改变，1 落地，2 漂浮稳定，3 沉没。
    }
}
