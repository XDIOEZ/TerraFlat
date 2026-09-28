using FlatWorld.Combat;
using Unity.Entities;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>旧 Unity 接口需要对象时使用的池化空壳；生命、AI 和画面仍以 ECS 为准。</summary>
    [DisallowMultipleComponent]
    public sealed class AiecsActorMirrorProxy : MonoBehaviour
    {
        #region 身份与外部输入

        private AiecsEcologyRuntimeHost owner;
        private BoxCollider2D interactionCollider;
        private uint generation;

        public Entity Entity { get; private set; }
        public CombatIdentity Identity { get; private set; }
        public int ActorGuid { get; private set; }
        public uint BindingGeneration => generation;
        public bool IsBound => owner != null && Entity != Unity.Entities.Entity.Null;

        internal void Bind(AiecsEcologyRuntimeHost host, Entity entity, CombatIdentity identity, int actorGuid)
        {
            generation++;
            if (generation == 0) generation++;
            owner = host;
            Entity = entity;
            Identity = identity;
            ActorGuid = actorGuid;
            if (interactionCollider != null) interactionCollider.enabled = false;
            gameObject.SetActive(true);
        }

        internal void Sync(Vector3 position) => transform.position = position;

        /// <summary>只有确实要求物理几何的旧入口才启用碰撞体，普通玩家攻击直接查询 ECS。</summary>
        public void SetInteractionCollider(bool enabled, Vector2 size, Vector2 offset)
        {
            if (!IsBound) return;
            if (interactionCollider == null)
            {
                interactionCollider = gameObject.GetComponent<BoxCollider2D>();
                if (interactionCollider == null) interactionCollider = gameObject.AddComponent<BoxCollider2D>();
                interactionCollider.isTrigger = true;
            }
            interactionCollider.size = size;
            interactionCollider.offset = offset;
            interactionCollider.enabled = enabled;
        }

        /// <summary>延迟回调必须带绑定代际，避免对象池复用后命中另一只生物。</summary>
        public bool TrySubmitHit(uint expectedGeneration, CombatDamageContext context)
        {
            return expectedGeneration == generation && owner != null &&
                   owner.TrySubmitMirrorHit(this, context);
        }

        internal void Unbind()
        {
            if (interactionCollider != null) interactionCollider.enabled = false;
            owner = null;
            Entity = Unity.Entities.Entity.Null;
            Identity = default;
            ActorGuid = 0;
            gameObject.SetActive(false);
        }

        #endregion
    }
}
