using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed class SpacePhysicsContactRelay : MonoBehaviour
    {
        #region 原生碰撞回报
        internal SpacePhysicsWorld World;
        internal SpacePhysicsBody Body;
        // 原生求解器负责反作用，回调只把已经发生的接触交给业务损伤结算。
        private void OnCollisionEnter2D(Collision2D collision) => World?.RecordImpact(Body, collision);
        private void OnCollisionStay2D(Collision2D collision) => World?.RecordImpact(Body, collision);
        #endregion
    }
}
