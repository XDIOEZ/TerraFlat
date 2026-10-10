using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    public sealed partial class AiecsEcologyRuntimeHost
    {
        #region 外部只读占地查询
        public bool HasLivingActorInBounds(Bounds bounds)
        {
            if (!PrepareRead()) return _actors.Count > 0;
            foreach (EcologyActor actor in _actors.Values)
            {
                if (!IsAlive(actor, out float2 position)) continue;
                AiecsBody body = _bridge.Simulation.Entities.GetComponentData<AiecsBody>(actor.Entity);
                // 受击外形与移动半径共同提供保守占地，避免生成结构压住无外壳的生物。
                float2 extents = math.max(new float2(body.Radius), math.abs(body.Hit.Center) + body.Hit.Extents);
                Vector2 point = WorldLocalPresentation.ProjectPosition(new Vector2(position.x, position.y), bounds.center);
                var occupied = new Bounds(new Vector3(point.x, point.y, bounds.center.z),
                    new Vector3(extents.x * 2f, extents.y * 2f, bounds.size.z));
                if (bounds.Intersects(occupied)) return true;
            }
            return false;
        }
        #endregion
    }
}
