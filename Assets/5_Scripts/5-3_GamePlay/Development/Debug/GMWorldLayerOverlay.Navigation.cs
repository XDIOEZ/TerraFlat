using FlatWorld.Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

internal sealed partial class GMWorldLayerOverlay
{
    #region 导航观察批次

    private NativeArray<Color32> navigationPixels; // 自有输出缓冲，不跨帧在主线程访问借来的导航数组。
    private JobHandle navigationSamplingJob; // 已向导航所有者登记的只读任务。
    private bool navigationJobScheduled; // 完成后才上传纹理或释放输出。
    private FlowNavigationCache navigationCache; // 当前真实后端，不归观察层释放。
    private FlowGoalHandle navigationGoal; // 玩家共享目标，包含世界纪元和代际。
    private Player navigationPlayer; // 本地玩家；复活和换世界使旧显示失效。
    private uint navigationPlayerGeneration; // 对象池复用时也清除旧帧。

    /// <summary>每 0.25 秒借用实际已发布流场，按视口逐格批量采样，不新增目标或触发路径搜索。</summary>
    private void UpdateNavigationOverlay(Camera camera, long version)
    {
        Player player = ItemMgr.Instance?.User_Player;
        if (material == null ||
            !WorldNavigationFlowRegistry.TryGetPlayerFlow(player, out FlowNavigationCache cache, out FlowGoalHandle goal) ||
            !cache.TryReadPublished(goal, out FlowNavigationSnapshot snapshot))
        {
            HidePendingFrame();
            return;
        }

        bool sourceChanged = !ReferenceEquals(navigationCache, cache) || navigationPlayer != player ||
            navigationPlayerGeneration != player.RuntimeGeneration || contextVersion != version ||
            navigationGoal.Slot != goal.Slot || navigationGoal.Generation != goal.Generation || navigationGoal.Epoch != goal.Epoch;
        if (sourceChanged)
        {
            HidePendingFrame();
            navigationCache = cache;
            navigationGoal = goal;
            navigationPlayer = player;
            navigationPlayerGeneration = player.RuntimeGeneration;
            contextVersion = version;
            nextRefreshTime = 0f;
        }

        if (navigationJobScheduled)
        {
            if (!navigationSamplingJob.IsCompleted)
                return;
            navigationSamplingJob.Complete();
            navigationJobScheduled = false;
            texture.SetPixelData(navigationPixels, 0);
            texture.Apply(false, false);
            PublishOverlayMesh();
        }

        if (Time.unscaledTime < nextRefreshTime)
            return;
        nextRefreshTime = Time.unscaledTime + RefreshInterval;
        if (!TryGetViewportBounds(camera, out Vector2 minimum, out Vector2 maximum))
        {
            HidePendingFrame();
            return;
        }

        // 导航始终一个真实世界格对应一个 texel，不能沿用热力图的远景降采样。
        stride = 1;
        origin = new Vector2Int(Mathf.FloorToInt(minimum.x) - 1, Mathf.FloorToInt(minimum.y) - 1);
        columns = Mathf.CeilToInt(maximum.x) - origin.x + 1;
        rows = Mathf.CeilToInt(maximum.y) - origin.y + 1;
        if (columns <= 0 || rows <= 0 || columns > SystemInfo.maxTextureSize || rows > SystemInfo.maxTextureSize)
        {
            HidePendingFrame();
            return;
        }

        // 容量按二次幂增长；相机平移导致可见行列数差一格时不反复分配。
        int width = Mathf.NextPowerOfTwo(columns);
        int height = Mathf.NextPowerOfTwo(rows);
        if (isNavigationTexture && texture != null)
        {
            width = Mathf.Max(width, texture.width);
            height = Mathf.Max(height, texture.height);
        }
        int length = checked(width * height);
        if (!navigationPixels.IsCreated || navigationPixels.Length != length)
        {
            if (navigationPixels.IsCreated)
                navigationPixels.Dispose();
            navigationPixels = new NativeArray<Color32>(length, Allocator.Persistent);
        }
        EnsureOverlayTexture(width, height, true);

        using (SampleMarker.Auto())
        {
            navigationSamplingJob = new SampleNavigationCellsJob
            {
                Snapshot = snapshot,
                Goal = goal,
                GoalCell = (int2)math.floor(snapshot.Goals[goal.Slot].Position),
                Origin = new int2(origin.x, origin.y),
                Columns = columns,
                Rows = rows,
                TextureWidth = width,
                Pixels = navigationPixels
            }.Schedule(length, 128);
            navigationJobScheduled = true;
            cache.RegisterReader(navigationSamplingJob);
        }
    }

    /// <summary>切模式、失去玩家或换世界时完成读任务，不再发布该批次。</summary>
    private void ResetNavigationSampling()
    {
        if (navigationJobScheduled)
            navigationSamplingJob.Complete();
        navigationJobScheduled = false;
        navigationSamplingJob = default;
        navigationCache = null;
        navigationPlayer = null;
        navigationGoal = default;
        navigationPlayerGeneration = 0;
    }

    /// <summary>关闭导航或销毁观察层时释放自有输出；借用的导航表始终由原后端管理。</summary>
    private void ReleaseNavigationSamples()
    {
        ResetNavigationSampling();
        if (navigationPixels.IsCreated)
            navigationPixels.Dispose();
    }

    #endregion

    #region 逐格只读采样

    /// <summary>
    /// 一个 texel 保存一个格子的单位方向和状态：RG 为方向，B 的 64/128/192 为移动/目标/不可达。
    /// 不可用格透明；仅调用怪物同一份 Snapshot.Sample，保留绕障、水域权重和循环世界接缝。
    /// </summary>
    [BurstCompile]
    private struct SampleNavigationCellsJob : IJobParallelFor
    {
        [ReadOnly] public FlowNavigationSnapshot Snapshot;
        public FlowGoalHandle Goal; // 真实玩家目标。
        public int2 Origin, GoalCell; // 世界格原点及规范化目标格。
        public int Columns, Rows, TextureWidth; // 可见格数与输出行跨度。
        [WriteOnly] public NativeArray<Color32> Pixels;

        public void Execute(int index)
        {
            int x = index % TextureWidth;
            int y = index / TextureWidth;
            if (x >= Columns || y >= Rows)
            {
                Pixels[index] = default;
                return;
            }

            int2 cell = Origin + new int2(x, y);
            FlowSample sample = Snapshot.Sample((float2)cell + 0.5f, Goal, 0f);
            if (sample.Status == FlowSampleStatus.Unavailable)
            {
                Pixels[index] = default;
                return;
            }
            if (sample.Status == FlowSampleStatus.Unreachable)
            {
                Pixels[index] = new Color32(128, 128, 192, 255);
                return;
            }
            if (sample.Status == FlowSampleStatus.Arrived || math.all(Snapshot.Domain.Normalize(cell) == GoalCell))
            {
                Pixels[index] = new Color32(128, 128, 128, 255);
                return;
            }

            float2 direction = math.normalizesafe(sample.Delta);
            Pixels[index] = new Color32(
                (byte)math.round((direction.x * 0.5f + 0.5f) * 255f),
                (byte)math.round((direction.y * 0.5f + 0.5f) * 255f), 64, 255);
        }
    }

    #endregion
}
