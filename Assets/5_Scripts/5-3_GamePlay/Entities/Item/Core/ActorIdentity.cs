/// <summary>角色身份查询只判断实际物品及其拥有者，不依赖规则的设置来源。</summary>
public static class ActorIdentity
{
    #region 玩家身份

    public static bool IsPlayer(Item candidate)
    {
        if (candidate == null)
            return false;

        Item owner = candidate.Owner != null ? candidate.Owner : candidate;
        // 手持物通过真实拥有者继承玩家身份，普通物品直接返回查询结果。
        return owner is Player || owner.TryGetComponent<Player>(out _);
    }

    #endregion
}
