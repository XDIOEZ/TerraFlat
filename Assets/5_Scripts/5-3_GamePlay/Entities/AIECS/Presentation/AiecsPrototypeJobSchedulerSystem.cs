using Unity.Entities;
using Unity.Jobs;

namespace FlatWorld.AIECS
{
    /// <summary>为独立表现原型 World 提供 IJobEntity 的 source-gen 调度作用域。</summary>
    [DisableAutoCreation]
    internal partial class AiecsPrototypeJobSchedulerSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            // 仅由 AiecsRenderPrototype 显式调用，不加入自动更新组。
        }

        internal JobHandle ScheduleParallel(AiecsPrototypeMotion job, EntityQuery query, JobHandle dependency) =>
            job.ScheduleParallel(query, dependency);
    }
}
