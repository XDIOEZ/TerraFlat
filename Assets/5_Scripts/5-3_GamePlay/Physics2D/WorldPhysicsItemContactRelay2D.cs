using UnityEngine;

/// <summary>独立物理宿主只转交原生接触事实，不持有伤害或物品业务规则。</summary>
internal sealed class WorldPhysicsItemContactRelay2D : MonoBehaviour
{
    #region 接触事实转发
    private Item owner;
    internal void Bind(Item item) => owner = item;
    internal void ClearBinding() => owner = null;
    private void OnCollisionEnter2D(Collision2D collision) => Forward(collision);
    private void OnCollisionStay2D(Collision2D collision) => Forward(collision);
    private void Forward(Collision2D collision)
    {
        if (owner != null) owner.GetComponent<ItemPhysicsProjection2D>()?.RecordContact(collision);
    }
    #endregion
}
