using Unity.Entities;
using Unity.Jobs;

namespace FlatWorld.AIECS
{
    /// <summary>
    /// 为独立 AIECS World 提供 IJobEntity 的 source-gen 调度入口。
    /// IJobEntity 的 Schedule/Run 调用必须位于 SystemBase/ISystem 的 source-gen 作用域内；
    /// AiecsSimulation 本身是普通托管资源所有者，因此统一经由本系统调度，避免命中生成代码的占位异常。
    /// </summary>
    [DisableAutoCreation]
    public partial class AiecsJobSchedulerSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            // 本系统不加入更新组，只作为私有 AIECS World 的显式调度入口。
        }

        internal JobHandle ScheduleParallel(AiecsBuildSpatialJob job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);

        internal JobHandle ScheduleParallel(AiecsPerceptionSystem job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);

        internal JobHandle ScheduleParallel(AiecsDecisionSystem job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);

        internal JobHandle ScheduleParallel(AiecsBehaviorSystem job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);

        internal JobHandle ScheduleParallel(AiecsGatherCrowdJob job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);

        internal JobHandle ScheduleParallel(AiecsFlowMoveJob job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);

        internal JobHandle ScheduleParallel(AiecsAttackSystem job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);

        internal JobHandle ScheduleParallel(AiecsBuffSystem job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);

        internal JobHandle ScheduleParallel(AiecsDamageSettlementSystem job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);

        internal JobHandle ScheduleParallel(AiecsDeathSystem job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);

        internal JobHandle ScheduleParallel(AiecsCaptureStateJob job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);
    }
}
